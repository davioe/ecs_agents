using Unity.Entities;
using Unity.Mathematics;

public struct SpawnerComponent : IComponentData
{
    public Entity Prefab;
    public int Count;
    public float3 FieldSize;
    public uint Seed;
}