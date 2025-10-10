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
        // Reserve small initial capacity
        _currentCapacity = 1024;
        _instanceBuffer = new ComputeBuffer(_currentCapacity, sizeof(float) * 16, ComputeBufferType.Structured);

        // args buffer: 5 uints (indexCountPerInstance, instanceCount, startIndex, baseVertex, startInstance)
        _argsBuffer = new ComputeBuffer(5, sizeof(uint), ComputeBufferType.IndirectArguments);

        // Optional: set a default material/mesh fallback in case we can't read one from scene
        _mesh = null;
        _material = null;
    }

    protected override void OnDestroy()
    {
        base.OnDestroy();
        if (_instanceBuffer != null) { _instanceBuffer.Dispose(); _instanceBuffer = null; }
        if (_argsBuffer != null) { _argsBuffer.Dispose(); _argsBuffer = null; }
    }

    protected override void OnUpdate()
    {
        // Query all renderable, non-prefab entities that have LocalTransform and RenderMeshUnmanaged
        var query = GetEntityQuery(new EntityQueryDesc
        {
            All = new ComponentType[] { typeof(LocalTransform), typeof(RenderMeshUnmanaged) }
        });

        int count = query.CalculateEntityCount();

        if (count == 0)
            return;

        // Ensure instance buffer is large enough
        if (count > _currentCapacity)
        {
            // grow (power of two strategy)
            int newCap = math.max(count, _currentCapacity * 2);
            _instanceBuffer.Dispose();
            _instanceBuffer = new ComputeBuffer(newCap, sizeof(float) * 16, ComputeBufferType.Structured);
            _currentCapacity = newCap;
        }

        // Grab mesh & material from the first entity's shared component (assumes all instances use same mesh+material)
        using var entities = query.ToEntityArray(Allocator.TempJob);
        if (entities.Length == 0) return;

        var firstEntity = entities[0];
        // Get shared component RenderMeshUnmanaged via EntityManager
        var renderMesh = EntityManager.GetComponentData<RenderMeshUnmanaged>(firstEntity);
        _mesh = _mesh != null ? _mesh : renderMesh.mesh;
        _material = _material != null ? _material : renderMesh.materialForSubMesh;

        // Safety: if still null, abort
        if (_mesh == null || _material == null)
            return;

        // Read all LocalTransform components into a native array
        using var transforms = query.ToComponentDataArray<LocalTransform>(Allocator.Temp);
        // Convert to CPU Matrix4x4 array
        var matrices = new UnityEngine.Matrix4x4[count];
        for (int i = 0; i < count; ++i)
        {
            var lt = transforms[i];
            // LocalTransform has Position (float3), Rotation (quaternion) and Scale (float3)
            // Convert to UnityEngine types:
            var pos = new UnityEngine.Vector3(lt.Position.x, lt.Position.y, lt.Position.z);
            var rot = new UnityEngine.Quaternion(lt.Rotation.value.x, lt.Rotation.value.y, lt.Rotation.value.z, lt.Rotation.value.w);
            var scl = new UnityEngine.Vector3(lt.Scale, lt.Scale, lt.Scale);

            matrices[i] = UnityEngine.Matrix4x4.TRS(pos, rot, scl);
        }

        // Upload to GPU
        _instanceBuffer.SetData(matrices, 0, 0, count);

        // Build indirect args
        uint indexCountPerInstance = (uint)_mesh.GetIndexCount(0);
        uint[] args = new uint[5] { indexCountPerInstance, (uint)count, 0, 0, 0 };
        _argsBuffer.SetData(args);

        // Bind buffer to material - shader must read _PerInstanceMatrices StructuredBuffer<float4x4>
        _material.SetBuffer("_PerInstanceMatrices", _instanceBuffer);

        // Compute bounds (must be large enough not to be culled)
        var bounds = _mesh.bounds;
        // Expand bounds to cover field — here we choose a large bounds; you should compute real world bounds.
        var worldBounds = new Bounds(Vector3.zero, bounds.size * 1000f);

        // Issue indirect draw
        Graphics.DrawMeshInstancedIndirect(_mesh, 0, _material, worldBounds, _argsBuffer, 0, null, ShadowCastingMode.On, true, 0, null, LightProbeUsage.Off);
    }
}
