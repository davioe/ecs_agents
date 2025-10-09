using Unity.Burst;
using Unity.Entities;
using Unity.Jobs;

[BurstCompile]
public partial struct SpawnerSystem : ISystem
{
    [BurstCompile]
    public void OnCreate(ref SystemState state) { }

    [BurstCompile]
    public void OnDestroy(ref SystemState state) { }

    [BurstCompile]
    public void OnUpdate(ref SystemState state)
    {
        state.Enabled = false;

        var spawner = SystemAPI.GetSingleton<SpawnerComponent>();

        EntityCommandBuffer.ParallelWriter ecb = GetEntityCommandBuffer(ref state);

        state.Dependency = new SpawnerJob
        {
            Prefab = spawner.Prefab,
            FieldSize = spawner.FieldSize,
            Seed = spawner.Seed,
            Ecb = ecb
        }.Schedule(spawner.Count, 64, state.Dependency);
    }

    private readonly EntityCommandBuffer.ParallelWriter GetEntityCommandBuffer(ref SystemState state)
    {
        var ecbSingleton = SystemAPI.GetSingleton<BeginSimulationEntityCommandBufferSystem.Singleton>();
        var ecb = ecbSingleton.CreateCommandBuffer(state.WorldUnmanaged);
        return ecb.AsParallelWriter();
    }
}