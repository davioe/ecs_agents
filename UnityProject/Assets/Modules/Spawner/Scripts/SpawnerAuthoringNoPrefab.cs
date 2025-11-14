using Unity.Entities;
using Unity.Mathematics;
using UnityEngine;

public class SpawnerAuthoringNoPrefab : MonoBehaviour
{
    public int Count;
    public float3 FieldSize = new(100f, 100f, 100f);
    public uint Seed = 12345;

    public class SpawnerBaker : Baker<SpawnerAuthoringNoPrefab>
    {
        public override void Bake(SpawnerAuthoringNoPrefab authoring)
        {
            var entity = GetEntity(TransformUsageFlags.None);

            AddComponent(entity, new SpawnerComponentNoPrefab
            {
                Count = math.max(0, authoring.Count),
                FieldSize = authoring.FieldSize,
                Seed = authoring.Seed
            });
        }
    }
}
