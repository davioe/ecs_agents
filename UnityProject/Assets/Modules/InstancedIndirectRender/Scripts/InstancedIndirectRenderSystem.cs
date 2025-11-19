using Unity.Burst;
using Unity.Collections;
using Unity.Entities;
using Unity.Jobs;
using Unity.Mathematics;
using Unity.Rendering;
using Unity.Transforms;
using UnityEngine;
using UnityEngine.Rendering;

// NEUES STRUCT: Exakt 32 Bytes
public struct InstanceData
{
    public float4 PosScale; // xyz = Pos, w = Scale
    public float4 Rot;      // Quaternion
}

[UpdateInGroup(typeof(PresentationSystemGroup))]
public partial class InstancedIndirectRenderManagedSystem : SystemBase
{
    public ComputeBuffer InstanceBuffer;
    public ComputeBuffer ArgsBuffer;

    // Geändert: Von float4x4 zu InstanceData
    public NativeArray<InstanceData> InstanceDataArray;

    public JobHandle DataPrepHandle = default;
    private MaterialPropertyBlock _mpb;

    public bool BuffersInitialized { get; private set; } = false;
    public int CurrentCapacity { get; private set; } = 0;
    public int CountToDrawFromLastFrame = 0;

    private readonly uint[] _args = new uint[5] { 0, 0, 0, 0, 0 };
    private readonly Bounds _worldBounds = new Bounds(Vector3.zero, Vector3.one * 100000f);

    protected override void OnCreate()
    {
        base.OnCreate();
        _mpb = new MaterialPropertyBlock();
    }

    public void EnsureInitialized(Mesh mesh, Material material, int initialCapacity = 1024)
    {
        if (BuffersInitialized) return;

        _args[0] = (uint)mesh.GetIndexCount(0);
        _args[2] = (uint)mesh.GetIndexStart(0);
        _args[3] = (uint)mesh.GetBaseVertex(0);

        CurrentCapacity = math.max(64, initialCapacity);

        // Stride ist jetzt 32 (sizeof(float) * 8) statt 64
        InstanceBuffer = new ComputeBuffer(CurrentCapacity, sizeof(float) * 8, ComputeBufferType.Structured);
        ArgsBuffer = new ComputeBuffer(5, sizeof(uint), ComputeBufferType.IndirectArguments);
        ArgsBuffer.SetData(_args);

        InstanceDataArray = new NativeArray<InstanceData>(CurrentCapacity, Allocator.Persistent);

        // Name muss zum Shader passen ("_PerInstanceData")
        _mpb.SetBuffer("_PerInstanceData", InstanceBuffer);

        BuffersInitialized = true;
    }

    public void ResizeIfNeeded(int requiredCount)
    {
        if (requiredCount <= CurrentCapacity) return;

        DataPrepHandle.Complete();

        InstanceBuffer.Dispose();
        InstanceDataArray.Dispose();

        CurrentCapacity = math.max(requiredCount, CurrentCapacity * 2);

        // Stride 32
        InstanceBuffer = new ComputeBuffer(CurrentCapacity, sizeof(float) * 8, ComputeBufferType.Structured);
        InstanceDataArray = new NativeArray<InstanceData>(CurrentCapacity, Allocator.Persistent);
        _mpb.SetBuffer("_PerInstanceData", InstanceBuffer);
    }

    public void UploadAndDraw(Mesh mesh, Material material)
    {
        if (!BuffersInitialized || CountToDrawFromLastFrame == 0) return;

        DataPrepHandle.Complete();

        // Upload des neuen Struct Arrays
        InstanceBuffer.SetData(InstanceDataArray, 0, 0, CountToDrawFromLastFrame);

        _args[1] = (uint)CountToDrawFromLastFrame;
        ArgsBuffer.SetData(_args);

        Graphics.DrawMeshInstancedIndirect(
            mesh, 0, material, _worldBounds, ArgsBuffer,
            0, _mpb, ShadowCastingMode.On, true, 0, null, LightProbeUsage.Off
        );
    }

    protected override void OnUpdate() { }

    protected override void OnDestroy()
    {
        DataPrepHandle.Complete();
        InstanceBuffer?.Dispose();
        ArgsBuffer?.Dispose();
        if (InstanceDataArray.IsCreated) InstanceDataArray.Dispose();
        base.OnDestroy();
    }
}

[UpdateInGroup(typeof(PresentationSystemGroup))]
[UpdateAfter(typeof(UpdatePresentationSystemGroup))]
public partial struct InstancedIndirectRenderSystem : ISystem
{
    private EntityQuery _renderQuery;

    [BurstCompile(OptimizeFor = OptimizeFor.Performance)]
    partial struct GatherMatricesJob : IJobEntity
    {
        [WriteOnly] public NativeArray<InstanceData> InstanceData;

        public void Execute([ReadOnly] in LocalToWorld ltw, [EntityIndexInQuery] int entityInQueryIndex)
        {
            // Wir zerlegen die Matrix in Position, Rotation und Scale
            float4x4 mat = ltw.Value;

            // Position extrahieren
            float3 pos = mat.c3.xyz;

            // Rotation (Quaternion) aus der Matrix extrahieren
            quaternion q = new quaternion(mat);

            // Scale berechnen (Annahme: Uniform Scale, Länge der ersten Spalte)
            float scale = math.length(mat.c0.xyz);

            InstanceData[entityInQueryIndex] = new InstanceData
            {
                PosScale = new float4(pos, scale),
                Rot = q.value
            };
        }
    }

    public void OnCreate(ref SystemState state)
    {
        state.RequireForUpdate<InstancedIndirectRenderingComponent>();
        _renderQuery = state.GetEntityQuery(ComponentType.ReadOnly<LocalToWorld>());
        state.World.GetOrCreateSystemManaged<InstancedIndirectRenderManagedSystem>();
    }

    public void OnUpdate(ref SystemState state)
    {
        var managed = state.World.GetExistingSystemManaged<InstancedIndirectRenderManagedSystem>();
        var cfg = SystemAPI.GetSingleton<InstancedIndirectRenderingComponent>();

        if (managed == null || cfg.Mesh == null || cfg.Material == null) return;

        if (!managed.BuffersInitialized)
            managed.EnsureInitialized(cfg.Mesh, cfg.Material, 1024);

        managed.UploadAndDraw(cfg.Mesh, cfg.Material);

        int count = _renderQuery.CalculateEntityCount();
        if (count == 0)
        {
            managed.CountToDrawFromLastFrame = 0;
            return;
        }

        managed.ResizeIfNeeded(count);
        managed.CountToDrawFromLastFrame = count;

        var job = new GatherMatricesJob
        {
            InstanceData = managed.InstanceDataArray
        };

        JobHandle handle = job.ScheduleParallel(_renderQuery, state.Dependency);

        managed.DataPrepHandle = handle;
        state.Dependency = handle;
    }

    public void OnDestroy(ref SystemState state) { }
}