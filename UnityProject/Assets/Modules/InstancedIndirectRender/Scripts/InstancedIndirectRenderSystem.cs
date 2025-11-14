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
    int _currentCapacity = 0;

    // Wiederverwendbares Array für Argumente
    readonly uint[] _args = new uint[5] { 0, 0, 0, 0, 0 };

    Mesh _mesh;
    Material _material;
    EntityQuery _query;
    uint _indexCountPerInstance = 0;

    protected override void OnCreate()
    {
        base.OnCreate();
        RequireForUpdate<InstancedIndirectRenderingComponent>();

        // ⭐ Query nur einmal erstellen
        _query = GetEntityQuery(ComponentType.ReadOnly<LocalToWorld>(), ComponentType.ReadOnly<FaceMouseComponent>());
    }

    protected override void OnStartRunning()
    {
        var renderConfig = SystemAPI.GetSingleton<InstancedIndirectRenderingComponent>();
        _mesh = renderConfig.Mesh;
        _material = renderConfig.Material;

        if (_mesh == null)
        {
            Debug.LogError("InstancedIndirectRenderSystem: Mesh is null.");
            return;
        }

        _indexCountPerInstance = (uint)_mesh.GetIndexCount(0);
        _args[0] = _indexCountPerInstance;

        _currentCapacity = 1024;

        // ⭐ Wir bleiben bei ComputeBuffer wie im Original
        _instanceBuffer = new ComputeBuffer(_currentCapacity, sizeof(float) * 16, ComputeBufferType.Structured);
        _argsBuffer = new ComputeBuffer(5, sizeof(uint), ComputeBufferType.IndirectArguments);
    }

    protected override void OnDestroy()
    {
        base.OnDestroy();
        _instanceBuffer?.Dispose();
        _instanceBuffer = null;
        _argsBuffer?.Dispose();
        _argsBuffer = null;
    }

    // ⭐ Job bleibt gleich: Er schreibt parallel in ein NativeArray
    [BurstCompile]
    partial struct GatherMatricesJob : IJobEntity
    {
        [WriteOnly]
        public NativeArray<float4x4> InstanceData;

        // [ReadOnly] ist wichtig für die Performance
        public void Execute([ReadOnly] in LocalToWorld ltw, [EntityIndexInQuery] int entityInQueryIndex)
        {
            InstanceData[entityInQueryIndex] = ltw.Value;
        }
    }

    protected override void OnUpdate()
    {
        int count = _query.CalculateEntityCount();
        if (count == 0 || _mesh == null || _material == null)
            return;

        // --- 1. Buffer-Resize (falls nötig) ---
        if (count > _currentCapacity)
        {
            // WICHTIG: Auf alle alten Jobs warten, bevor wir Buffer löschen
            Dependency.Complete();

            _instanceBuffer?.Dispose();

            int newCap = math.max(count, _currentCapacity * 2);
            _instanceBuffer = new ComputeBuffer(newCap, sizeof(float) * 16, ComputeBufferType.Structured);
            _currentCapacity = newCap;
        }

        // ⭐ 2. Temporäres Array für Job-Output erstellen
        // Verwenden Sie UninitializedMemory, da der Job garantiert jeden Index schreibt
        var tempMatrixArray = new NativeArray<float4x4>(count, Allocator.TempJob, NativeArrayOptions.UninitializedMemory);

        // ⭐ 3. Job schedulen, der in das temporäre Array schreibt
        var gatherJob = new GatherMatricesJob
        {
            InstanceData = tempMatrixArray
        };

        // Job schedulen und Dependency verwalten
        var jobHandle = gatherJob.ScheduleParallel(_query, Dependency);

        // --- Argument-Buffer und Material (schnell) ---
        _args[1] = (uint)count;
        _argsBuffer.SetData(_args);
        _material.SetBuffer("_PerInstanceMatrices", _instanceBuffer);

        // --- Bounds (immer noch das Culling-Problem) ---
        var worldBounds = new Bounds(Vector3.zero, _mesh.bounds.size * 1000f);

        // ⭐ 4. Auf Job warten (Sync-Punkt)
        // Wir MÜSSEN warten, bis die Daten gesammelt sind, bevor wir sie hochladen.
        // Das ist der Kompromiss, aber er ist viel kleiner als der alte Flaschenhals.
        jobHandle.Complete();

        // ⭐ 5. Schneller Upload von NativeArray -> ComputeBuffer
        _instanceBuffer.SetData(tempMatrixArray);

        // ⭐ 6. Temporäres Array freigeben
        tempMatrixArray.Dispose();

        // JobHandle an das System übergeben (obwohl wir schon gewartet haben)
        Dependency = jobHandle;

        // --- 7. Draw Call ---
        Graphics.DrawMeshInstancedIndirect(
            _mesh,
            0,
            _material,
            worldBounds, // 🚨 Culling ist immer noch das NÄCHSTE Problem!
            _argsBuffer,
            0,
            null,
            ShadowCastingMode.On,
            true,
            0,
            null,
            LightProbeUsage.Off
        );
    }
}