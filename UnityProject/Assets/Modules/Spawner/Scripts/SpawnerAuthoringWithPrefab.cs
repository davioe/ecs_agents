using Unity.Entities;
using Unity.Mathematics;
using UnityEngine;

public class SpawnerAuthoringWithPrefab : MonoBehaviour
{
    public GameObject Prefab;
    public int Count;
    public float3 FieldSize = new(100f, 100f, 100f);
    public uint Seed = 12345;

    public class SpawnerBaker : Baker<SpawnerAuthoringWithPrefab>
    {
        public override void Bake(SpawnerAuthoringWithPrefab authoring)
        {
            var entity = GetEntity(TransformUsageFlags.None);

            AddComponent(entity, new SpawnerComponentWithPrefab
            {
                Prefab = GetEntity(authoring.Prefab, TransformUsageFlags.Dynamic),
                Count = math.max(0, authoring.Count),
                FieldSize = authoring.FieldSize,
                Seed = authoring.Seed
            });
        }
    }
}
