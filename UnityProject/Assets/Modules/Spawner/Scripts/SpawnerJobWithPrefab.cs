using Unity.Burst;
using Unity.Collections;
using Unity.Entities;
using Unity.Jobs;
using Unity.Mathematics;
using Unity.Transforms;

[BurstCompile]
public partial struct SpawnerJobWithPrefab : IJobParallelFor
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

        var faceMouseComponent = new FaceMouseComponent
        {
            RotationSpeed = 100f,
            LockPitch = 1,
            PlaneY = 0f

        };

        Ecb.AddComponent(index, newEntity, faceMouseComponent);
    }
}

[BurstCompile]
public partial struct SpawnerJobNoPrefab : IJobParallelFor
{
    public EntityCommandBuffer.ParallelWriter Ecb;
    [ReadOnly] public float3 FieldSize;
    [ReadOnly] public uint Seed;

    [BurstCompile]
    public void Execute(int index)
    {
        uint hashedSeed = math.hash(new uint2(Seed, (uint)index));
        var random = new Random(hashedSeed);
        var randomPosition = random.NextFloat3(-FieldSize / 2, FieldSize / 2);

        // 🔹 Neue leere Entity erzeugen
        var newEntity = Ecb.CreateEntity(index);

        // 🔹 LocalTransform setzen (Position + Rotation + Scale)
        //Ecb.AddComponent(index, newEntity, LocalTransform.FromPositionRotationScale(
        //    randomPosition,
        //    quaternion.identity,
        //    1f
        //));

        Ecb.AddComponent(index, newEntity, new LocalToWorld
        {
            Value = float4x4.TRS(randomPosition, quaternion.identity, new float3(1f, 1f, 1f))
        });

        // 🔹 Gameplay-Komponente hinzufügen
        var faceMouseComponent = new FaceMouseComponent
        {
            RotationSpeed = 100f,
            LockPitch = 1,
            PlaneY = 0f
        };
        Ecb.AddComponent(index, newEntity, faceMouseComponent);
    }
}