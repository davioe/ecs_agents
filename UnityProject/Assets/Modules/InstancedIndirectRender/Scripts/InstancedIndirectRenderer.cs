using UnityEngine;

public class IndirectInstancedRenderer : MonoBehaviour
{
    public Mesh mesh; // Z. B. ein Cube-Mesh
    public Material material; // Der obige Shader-Material
    public int instanceCount = 1000;

    private GraphicsBuffer instanceMatricesBuffer;
    private GraphicsBuffer indirectArgsBuffer;
    private RenderParams renderParams;
    private Matrix4x4[] matrices;

    void Start()
    {
        // Initialize matrices
        matrices = new Matrix4x4[instanceCount];
        for (int i = 0; i < instanceCount; i++)
        {
            Vector3 position = Random.insideUnitSphere * 10f;
            Quaternion rotation = Random.rotation;
            Vector3 scale = Vector3.one * Random.Range(0.5f, 1.5f);
            matrices[i] = Matrix4x4.TRS(position, rotation, scale);
        }

        // Setup buffer for matrices
        instanceMatricesBuffer = new GraphicsBuffer(GraphicsBuffer.Target.Structured, instanceCount, sizeof(float) * 16);
        instanceMatricesBuffer.SetData(matrices);

        // Setup indirect args buffer
        indirectArgsBuffer = new GraphicsBuffer(GraphicsBuffer.Target.IndirectArguments, 1, GraphicsBuffer.IndirectDrawIndexedArgs.size);
        var args = new GraphicsBuffer.IndirectDrawIndexedArgs
        {
            indexCountPerInstance = mesh.GetIndexCount(0),
            instanceCount = (uint)instanceCount,
            startIndex = 0,
            baseVertexIndex = 0,
            startInstance = 0
        };
        indirectArgsBuffer.SetData(new[] { args });

        // Render params
        renderParams = new RenderParams(material)
        {
            worldBounds = new Bounds(Vector3.zero, Vector3.one * 100f), // Adjust bounds
            matProps = new MaterialPropertyBlock()
        };
        renderParams.matProps.SetBuffer("_InstanceMatrices", instanceMatricesBuffer);
    }

    void Update()
    {
        Graphics.RenderMeshIndirect(renderParams, mesh, indirectArgsBuffer);
    }

    void OnDestroy()
    {
        instanceMatricesBuffer?.Release();
        indirectArgsBuffer?.Release();
    }
}