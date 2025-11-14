using Unity.Entities;
using Unity.Mathematics;
using UnityEngine;

public struct SpawnerComponentWithPrefab : IComponentData
{
    public Entity Prefab;
    public int Count;
    public float3 FieldSize;
    public uint Seed;
}

public struct SpawnerComponentNoPrefab : IComponentData
{
    public int Count;
    public float3 FieldSize;
    public uint Seed;
}

public struct InstancedIndirectRenderingComponent : IComponentData
{
    public UnityObjectRef<Mesh> Mesh;
    public UnityObjectRef<Material> Material;
}