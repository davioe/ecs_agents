using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;
using Unity.Rendering;
using Unity.Transforms;
using UnityEngine;
using UnityEngine.Rendering;

[UpdateInGroup(typeof(PresentationSystemGroup))]
public partial class InstancedIndirectRenderSystem : SystemBase
{
    ComputeBuffer _instanceBuffer;
    ComputeBuffer _argsBuffer;
    int _currentCapacity = 0;

    Mesh _mesh;
    Material _material;

    protected override void OnCreate()
    {
        base.OnCreate();

        RequireForUpdate<InstancedIndirectRenderingComponent>();
    }

    protected override void OnStartRunning()
    {
        // Safe: now the singleton is guaranteed to exist
        var renderConfig = SystemAPI.GetSingleton<InstancedIndirectRenderingComponent>();
        _mesh = renderConfig.Mesh;
        _material = renderConfig.Material;

        // Init buffers
        _currentCapacity = 1024;
        _instanceBuffer = new ComputeBuffer(_currentCapacity, sizeof(float) * 16, ComputeBufferType.Structured);
        _argsBuffer = new ComputeBuffer(5, sizeof(uint), ComputeBufferType.IndirectArguments);
    }

    protected override void OnDestroy()
    {
        base.OnDestroy();
        if (_instanceBuffer != null) { _instanceBuffer.Dispose(); _instanceBuffer = null; }
        if (_argsBuffer != null) { _argsBuffer.Dispose(); _argsBuffer = null; }
    }

    protected override void OnUpdate()
    {
        var query = GetEntityQuery(new EntityQueryDesc
        {
            All = new ComponentType[] { typeof(LocalToWorld), typeof(FaceMouseComponent) }
        });

        int count = query.CalculateEntityCount();
        if (count == 0)
            return;

        // Resize buffer if needed
        if (count > _currentCapacity)
        {
            int newCap = math.max(count, _currentCapacity * 2);
            _instanceBuffer?.Dispose();
            _instanceBuffer = new ComputeBuffer(newCap, sizeof(float) * 16, ComputeBufferType.Structured);
            _currentCapacity = newCap;
        }

        // Mesh + Material must be valid (you set them in OnStartRunning)
        if (_mesh == null || _material == null)
            return;

        // ⭐ ZERO-COPY read: get LocalToWorld array
        using var ltw = query.ToComponentDataArray<LocalToWorld>(Allocator.Temp);

        // ⭐ Reinterpret LocalToWorld array as float4x4 array (no copy!)
        var matrices = ltw.Reinterpret<Unity.Mathematics.float4x4>();

        // ⭐ Upload directly from the NativeArray<float4x4> to the GPU
        _instanceBuffer.SetData(matrices);

        // Build indirect args
        uint indexCountPerInstance = (uint)_mesh.GetIndexCount(0);
        uint[] args = new uint[5] { indexCountPerInstance, (uint)count, 0, 0, 0 };
        _argsBuffer.SetData(args);

        // Bind using StructuredBuffer<float4x4>
        _material.SetBuffer("_PerInstanceMatrices", _instanceBuffer);

        // Compute world bounds (fix later if needed)
        var bounds = _mesh.bounds;
        var worldBounds = new Bounds(Vector3.zero, bounds.size * 1000f);

        // Indirect draw
        Graphics.DrawMeshInstancedIndirect(
            _mesh,
            0,
            _material,
            worldBounds,
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
