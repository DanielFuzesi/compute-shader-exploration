using UnityEngine;


public class Grass : MonoBehaviour
{
    private struct GrassData {
        public Vector4 position;
        public Vector2 offset;
        public uint placePosition;
    };

    [SerializeField] private Texture2D placementTexture;
    [SerializeField] private ComputeShader placementShader;
    [SerializeField] private Mesh grassMesh;
    [SerializeField] private Material grassMaterial;
    [SerializeField] private int terrainDimension;
    [SerializeField] private bool updateGrass = false;

    private Terrain terrain;
    private TerrainData terrainData;
    private RenderTexture renderTexture;
    private ComputeBuffer grassBuffer, argsBuffer;
    private GrassData[] grassData;

    /*
    Steps to generate grass points on plane using a compute shader:
        1. Read in grass placement texture
        2. Resize grass placement texture to fit terrain
        3. Find all black pixels within texture
        4. Return all black pixel indexes found on texture
        5. Translate black pixel position data onto plane
        6. Spawn grass on position
    
    What does the grass struct need to know:
        1. Position
        2. Offset from world center (0, 0, 0)
        3. Displacement
    */

    private void Start() {
        Debug.Log("Execution Time Start: " + Time.realtimeSinceStartup);
        int vector4Size = sizeof(float) * 4;
        int vector2Size = sizeof(float) * 2;
        int intSize = sizeof(uint);
        int totalSize = vector4Size + vector2Size + intSize;

        terrain = GetComponent<Terrain>();
        terrainData = terrain.terrainData;
        terrainData.size = new Vector3(terrainDimension, terrainData.size.y, terrainDimension);

        grassBuffer = new ComputeBuffer(terrainDimension * terrainDimension, totalSize);
        argsBuffer = new ComputeBuffer(1, 5 * sizeof(uint), ComputeBufferType.IndirectArguments);

        placementTexture = Instantiate(placementTexture);

        TextureScale.Bilinear(placementTexture, terrainDimension, terrainDimension);
        ConvertTexture2dToRenderTexture(out renderTexture, placementTexture, placementTexture.width);
        
        Debug.Log("Execution Time After Texture Rescale: " + Time.realtimeSinceStartup);

        UpdateGrassBuffer();

        Debug.Log("Execution Time Overall: " + Time.realtimeSinceStartup);
    }

    private void OnDisable() {
        grassBuffer.Release();
        argsBuffer.Release();
        grassBuffer = null;
        argsBuffer = null;
    }

    private void ConvertTexture2dToRenderTexture(out RenderTexture rendTex, in Texture2D inputTex, int res) {
        rendTex = new RenderTexture(res, res, 0);
        rendTex.enableRandomWrite = true;
        RenderTexture.active = rendTex;

        Graphics.Blit(inputTex, rendTex);
    }

    private void UpdateGrassBuffer() {
        int kernelHandle = placementShader.FindKernel("FindGrassPoints");

        int groupW = Mathf.CeilToInt(renderTexture.width / 8f);
        int groupH = Mathf.CeilToInt(renderTexture.height / 8f);

        grassData = new GrassData[renderTexture.width * renderTexture.height];

        placementShader.SetBuffer(kernelHandle, "_GrassBuffer", grassBuffer);
        placementShader.SetTexture(kernelHandle, "_TextureData", renderTexture);
        placementShader.SetVector("_Resolution", new Vector2(renderTexture.width, renderTexture.height));
        placementShader.SetFloat(Shader.PropertyToID("_GlobalOffset"), 0.5f);
        placementShader.Dispatch(kernelHandle, groupW, groupH, 1);

        uint[] args = new uint[5] { 0, 0, 0, 0, 0 };
        // Arguments for drawing mesh.
        // 0 == number of triangle indices, 1 == population, others are only relevant if drawing submeshes.
        args[0] = (uint)grassMesh.GetIndexCount(0);
        args[1] = (uint)grassBuffer.count;
        args[2] = (uint)grassMesh.GetIndexStart(0);
        args[3] = (uint)grassMesh.GetBaseVertex(0);
        argsBuffer.SetData(args);

        Debug.Log("Position ComputeShader Complete: " + Time.realtimeSinceStartup);

        grassMaterial.SetBuffer("positionBuffer", grassBuffer);
        Graphics.DrawMeshInstancedIndirect(grassMesh, 0, grassMaterial, new Bounds(Vector3.zero, new Vector3(-500.0f, 200.0f, 500.0f)), argsBuffer);

        Debug.Log("Grass Material Shader Complete: " + Time.realtimeSinceStartup);
    }

    private void Update() {
        grassMaterial.SetBuffer("positionBuffer", grassBuffer);
        Graphics.DrawMeshInstancedIndirect(grassMesh, 0, grassMaterial, new Bounds(Vector3.zero, new Vector3(-500.0f, 200.0f, 500.0f)), argsBuffer);

        if (updateGrass) {
            UpdateGrassBuffer();
        }
    }
}
