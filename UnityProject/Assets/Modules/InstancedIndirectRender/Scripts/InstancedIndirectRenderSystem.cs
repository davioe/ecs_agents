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
    // ⭐ Persistente ECS-Daten
    ComputeBuffer _instanceBuffer;
    ComputeBuffer _argsBuffer;
    NativeArray<float4x4> _matrixArray;

    // Allgemeine Rendering-Daten
    Mesh _mesh;
    Material _material;
    EntityQuery _query;

    // Konstante oder statische Daten
    readonly uint[] _args = new uint[5] { 0, 0, 0, 0, 0 };
    // Bounds persistent deklariert, um Heap-Allokationen pro Frame zu vermeiden
    readonly Bounds _worldBounds = new(Vector3.zero, new Vector3(1000f, 1000f, 1000f));
    uint _indexCountPerInstance = 0;

    // ⭐ Pipelining-Variablen (Status von Frame N-1)
    JobHandle _dataPrepHandle;
    int _currentCapacity = 0;
    int _previousFrameCount = 0;
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

        if (_mesh == null || _material == null) return;

        _indexCountPerInstance = (uint)_mesh.GetIndexCount(0);
        _args[0] = _indexCountPerInstance;

        _currentCapacity = 1024;

        // Initialisiere die persistenten Buffer und das NativeArray
        _instanceBuffer = new ComputeBuffer(_currentCapacity, sizeof(float) * 16, ComputeBufferType.Structured);
        _argsBuffer = new ComputeBuffer(5, sizeof(uint), ComputeBufferType.IndirectArguments);
        _matrixArray = new NativeArray<float4x4>(_currentCapacity, Allocator.Persistent);

        _buffersAreInitialized = true;
    }

    protected override void OnDestroy()
    {
        base.OnDestroy();

        _dataPrepHandle.Complete();

        _instanceBuffer?.Dispose();
        _argsBuffer?.Dispose();
        if (_matrixArray.IsCreated) _matrixArray.Dispose();

        _buffersAreInitialized = false;
    }

    // ⭐ Job-Struktur (entspricht IJobEntity) wieder eingeführt, um Entities.ForEach zu vermeiden
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
    /// </summary>
    void EnsureCapacity(int count)
    {
        if (count <= _currentCapacity)
            return;

        // Muss auf den Job warten, da er sonst in das Array schreiben könnte, das wir löschen.
        _dataPrepHandle.Complete();

        // Freigabe
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
        _dataPrepHandle.Complete();

        // --- 2. Daten von Frame N-1 hochladen & zeichnen ---
        if (_previousFrameCount > 0)
        {
            _instanceBuffer.SetData(_matrixArray, 0, 0, _previousFrameCount);

            _args[1] = (uint)_previousFrameCount;
            _argsBuffer.SetData(_args);

            _material.SetBuffer("_PerInstanceMatrices", _instanceBuffer);

            // Draw Call für Frame N (mit Daten von N-1)
            Graphics.DrawMeshInstancedIndirect(
                _mesh, 0, _material, _worldBounds, _argsBuffer,
                0, null, ShadowCastingMode.On, true, 0, null, LightProbeUsage.Off
            );
        }

        // --- 3. Daten für Frame N+1 vorbereiten (Job starten) ---

        int currentFrameCount = _query.CalculateEntityCount();
        if (currentFrameCount == 0)
        {
            _previousFrameCount = 0;
            return;
        }

        EnsureCapacity(currentFrameCount);

        // Job schedulen
        var gatherJob = new GatherMatricesJob
        {
            // Wichtig: Nur den Teil des Arrays übergeben
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