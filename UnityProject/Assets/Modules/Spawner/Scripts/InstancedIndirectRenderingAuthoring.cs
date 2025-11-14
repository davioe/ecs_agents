using Unity.Entities;
using UnityEngine;

public class InstancedIndirectRenderingAuthoring : MonoBehaviour
{
    public Mesh Mesh;
    public Material Material;

    class Baker : Baker<InstancedIndirectRenderingAuthoring>
    {
        public override void Bake(InstancedIndirectRenderingAuthoring authoring)
        {
            // Singleton-Entity erzeugen
            var entity = GetEntity(TransformUsageFlags.None);

            // Komponente hinzufügen + UnityObjectRef zuweisen
            AddComponent(entity, new InstancedIndirectRenderingComponent
            {
                Mesh = authoring.Mesh,
                Material = authoring.Material
            });
        }
    }
}
