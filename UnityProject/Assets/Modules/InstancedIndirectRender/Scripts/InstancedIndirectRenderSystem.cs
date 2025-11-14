using Unity.Burst;
using Unity.Collections;
using Unity.Entities;
using Unity.Jobs;
using Unity.Mathematics;
using Unity.Rendering;
using Unity.Transforms;
using UnityEngine;
using UnityEngine.Rendering;

[UpdateInGroup(typeof(PresentationSystemGroup))]
[BurstCompile]
public partial class InstancedIndirectRenderSystem : SystemBase
{
    ComputeBuffer _instanceBuffer;
    ComputeBuffer _argsBuffer;
    readonly uint[] _args = new uint[5] { 0, 0, 0, 0, 0 };

    Mesh _mesh;
    Material _material;
    EntityQuery _query;
    uint _indexCountPerInstance = 0;

    // --- ⭐ Pipelining-Variablen ---

    // Wir brauchen einen permanenten Speicher für die Job-Ergebnisse
    NativeArray<float4x4> _matrixArray;

    // Das JobHandle, das die Daten für den *nächsten* Draw-Call vorbereitet
    JobHandle _dataPrepHandle;

    int _currentCapacity = 0;
    int _previousFrameCount = 0; // Die Anzahl der Instanzen aus dem letzten Frame
    bool _buffersAreInitialized = false;

    // ---------------------------------

    protected override void OnCreate()
    {
        base.OnCreate();
        RequireForUpdate<InstancedIndirectRenderingComponent>();
        _query = GetEntityQuery(ComponentType.ReadOnly<LocalToWorld>(), ComponentType.ReadOnly<FaceMouseComponent>());
    }

    protected override void OnStartRunning()
    {
        var renderConfig = SystemAPI.GetSingleton<InstancedIndirectRenderingComponent>();
        _mesh = renderConfig.Mesh;
        _material = renderConfig.Material;

        if (_mesh == null) return;

        _indexCountPerInstance = (uint)_mesh.GetIndexCount(0);
        _args[0] = _indexCountPerInstance;

        // Initiale Kapazität
        _currentCapacity = 1024;

        // ⭐ Initialisiere die persistenten Buffer
        _instanceBuffer = new ComputeBuffer(_currentCapacity, sizeof(float) * 16, ComputeBufferType.Structured);
        _argsBuffer = new ComputeBuffer(5, sizeof(uint), ComputeBufferType.IndirectArguments);
        _matrixArray = new NativeArray<float4x4>(_currentCapacity, Allocator.Persistent);

        _buffersAreInitialized = true;
    }

    protected override void OnDestroy()
    {
        base.OnDestroy();

        // ⭐ WICHTIG: Auf den letzten Job warten, bevor wir die Daten löschen
        _dataPrepHandle.Complete();

        _instanceBuffer?.Dispose();
        _argsBuffer?.Dispose();
        if (_matrixArray.IsCreated) _matrixArray.Dispose();

        _buffersAreInitialized = false;
    }

    // Job bleibt gleich
    [BurstCompile]
    partial struct GatherMatricesJob : IJobEntity
    {
        [WriteOnly] public NativeArray<float4x4> InstanceData;
        public void Execute([ReadOnly] in LocalToWorld ltw, [EntityIndexInQuery] int entityInQueryIndex)
        {
            InstanceData[entityInQueryIndex] = ltw.Value;
        }
    }

    /// <summary>
    /// Stellt sicher, dass alle Buffer die benötigte Kapazität haben.
    /// Muss auf den Job warten, wenn eine Neuerstellung nötig ist.
    /// </summary>
    void EnsureCapacity(int count)
    {
        if (count <= _currentCapacity)
            return; // Alles gut

        // ⭐ Wir müssen die Größe ändern. Wir MÜSSEN auf den Job warten,
        // da er vielleicht gerade in das Array schreibt, das wir löschen.
        _dataPrepHandle.Complete();

        // Alte Buffer/Arrays freigeben
        _instanceBuffer?.Dispose();
        if (_matrixArray.IsCreated) _matrixArray.Dispose();

        // Neue Kapazität berechnen
        int newCap = math.max(count, _currentCapacity * 2);
        _currentCapacity = newCap;

        // Neu erstellen
        _instanceBuffer = new ComputeBuffer(_currentCapacity, sizeof(float) * 16, ComputeBufferType.Structured);
        _matrixArray = new NativeArray<float4x4>(_currentCapacity, Allocator.Persistent);
    }

    protected override void OnUpdate()
    {
        if (!_buffersAreInitialized || _mesh == null || _material == null)
            return;

        // --- 1. Auf den Job von Frame N-1 warten ---
        // Dieser Job hat die Daten vorbereitet, die wir *jetzt* zeichnen.
        // Im ersten Frame ist dieser Handle leer.
        _dataPrepHandle.Complete();

        // --- 2. Daten von Frame N-1 hochladen & zeichnen ---
        // (Nur wenn wir tatsächlich Daten aus dem Vor-Frame haben)
        if (_previousFrameCount > 0)
        {
            // ⭐ Upload zur GPU (schnell, da Daten bereit sind)
            _instanceBuffer.SetData(_matrixArray, 0, 0, _previousFrameCount);

            // Argumente setzen (Anzahl von N-1)
            _args[1] = (uint)_previousFrameCount;
            _argsBuffer.SetData(_args);

            _material.SetBuffer("_PerInstanceMatrices", _instanceBuffer);

            // Bounds (immer noch das Culling-Problem)
            var worldBounds = new Bounds(Vector3.zero, _mesh.bounds.size * 1000f);

            // ⭐ Draw Call für Frame N (mit Daten von N-1)
            Graphics.DrawMeshInstancedIndirect(
                _mesh, 0, _material, worldBounds, _argsBuffer,
                0, null, ShadowCastingMode.On, true, 0, null, LightProbeUsage.Off
            );
        }

        // --- 3. Daten für Frame N+1 vorbereiten ---

        int currentFrameCount = _query.CalculateEntityCount();
        if (currentFrameCount == 0)
        {
            _previousFrameCount = 0; // Nichts zu tun für den nächsten Frame
            return;
        }

        // Buffer-Größe für *diesen* Frame anpassen (falls nötig)
        EnsureCapacity(currentFrameCount);

        // Job schedulen, der in unser persistentes Array schreibt
        var gatherJob = new GatherMatricesJob
        {
            // Wichtig: Nur den Teil des Arrays übergeben, den wir brauchen
            InstanceData = _matrixArray.GetSubArray(0, currentFrameCount)
        };

        // ⭐ Job für Frame N starten (wird in N+1 fertig sein)
        _dataPrepHandle = gatherJob.ScheduleParallel(_query, Dependency);

        // System-Dependency aktualisieren
        Dependency = _dataPrepHandle;

        // Anzahl für den nächsten Frame speichern
        _previousFrameCount = currentFrameCount;
    }
}