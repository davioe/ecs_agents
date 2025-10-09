using Unity.Entities;
using Unity.Mathematics;
using UnityEngine;

public class SpawnerAuthoring : MonoBehaviour
{
    public GameObject Prefab;
    public int Count;
    public float3 FieldSize = new(100f, 100f, 100f);
    public uint Seed = 12345;

    public class SpawnerBaker : Baker<SpawnerAuthoring>
    {
        public override void Bake(SpawnerAuthoring authoring)
        {
            var entity = GetEntity(TransformUsageFlags.None);

            AddComponent(entity, new SpawnerComponent
            {
                Prefab = GetEntity(authoring.Prefab, TransformUsageFlags.Dynamic),
                Count = math.max(0, authoring.Count),
                FieldSize = authoring.FieldSize,
                Seed = authoring.Seed
            });
        }
    }
}
