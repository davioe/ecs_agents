using Unity.Burst;
using Unity.Collections;
using Unity.Entities;
using Unity.Jobs;
using Unity.Mathematics;
using Unity.Transforms;

[BurstCompile]
public partial struct SpawnerJob : IJobParallelFor
{
    public EntityCommandBuffer.ParallelWriter Ecb;
    [ReadOnly] public Entity Prefab;
    [ReadOnly] public float3 FieldSize;
    [ReadOnly] public uint Seed;

    [BurstCompile]
    public void Execute(int index)
    {
        uint hashedSeed = math.hash(new uint2(Seed, (uint)index));
        var random = new Random(hashedSeed);
        var randomPosition = random.NextFloat3(-FieldSize / 2, FieldSize / 2);

        var newEntity = Ecb.Instantiate(index, Prefab);

        Ecb.SetComponent(index, newEntity, LocalTransform.FromPosition(randomPosition));
    }
}