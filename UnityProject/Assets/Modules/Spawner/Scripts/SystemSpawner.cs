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
        // System nur einmal ausführen
        state.Enabled = false;

        EntityCommandBuffer.ParallelWriter ecb = GetEntityCommandBuffer(ref state);

        // --- Variante 1: Mit Prefab ---
        if (SystemAPI.TryGetSingleton<SpawnerComponentWithPrefab>(out var spawnerWithPrefab))
        {
            state.Dependency = new SpawnerJobWithPrefab
            {
                Prefab = spawnerWithPrefab.Prefab,
                FieldSize = spawnerWithPrefab.FieldSize,
                Seed = spawnerWithPrefab.Seed,
                Ecb = ecb
            }.Schedule(spawnerWithPrefab.Count, 64, state.Dependency);
        }

        // --- Variante 2: Ohne Prefab ---
        if (SystemAPI.TryGetSingleton<SpawnerComponentNoPrefab>(out var spawnerNoPrefab))
        {
            state.Dependency = new SpawnerJobNoPrefab
            {
                FieldSize = spawnerNoPrefab.FieldSize,
                Seed = spawnerNoPrefab.Seed,
                Ecb = ecb
            }.Schedule(spawnerNoPrefab.Count, 64, state.Dependency);
        }
    }

    private readonly EntityCommandBuffer.ParallelWriter GetEntityCommandBuffer(ref SystemState state)
    {
        var ecbSingleton = SystemAPI.GetSingleton<BeginSimulationEntityCommandBufferSystem.Singleton>();
        var ecb = ecbSingleton.CreateCommandBuffer(state.WorldUnmanaged);
        return ecb.AsParallelWriter();
    }
}