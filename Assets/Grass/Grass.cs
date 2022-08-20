using static System.Runtime.InteropServices.Marshal;
using UnityEngine;


public class Grass : MonoBehaviour
{
    private struct GrassData {
        public Vector4 position;
        public Vector2 uv;
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
    private RenderTexture heightMap;
    private ComputeBuffer grassBuffer, argsBuffer, heightsBuffer;
    private GrassData[] grassData;
    // private float[,] terrainHeights;

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
        terrain = GetComponent<Terrain>();
        terrainData = terrain.terrainData;
        terrainData.size = new Vector3(terrainDimension, terrainData.size.y, terrainDimension);
        // terrainHeights = terrainData.GetHeights(0, 0, terrainData.heightmapResolution, terrainData.heightmapResolution);

        // GetTerrainHeights(terrain, out terrainHeights, terrainDimension);

        grassBuffer = new ComputeBuffer(terrainDimension * terrainDimension, SizeOf(typeof(GrassData)));
        // heightsBuffer = new ComputeBuffer(terrainData.heightmapResolution * terrainData.heightmapResolution, sizeof(float));
        argsBuffer = new ComputeBuffer(1, 5 * sizeof(uint), ComputeBufferType.IndirectArguments);

        heightMap = new RenderTexture(terrainData.heightmapTexture.width, terrainData.heightmapTexture.width, terrainData.heightmapTexture.depth, terrainData.heightmapTexture.format);
        heightMap.enableRandomWrite = true;
        Graphics.CopyTexture(terrainData.heightmapTexture, heightMap);

        // heightsBuffer.SetData(terrainHeights);

        placementTexture = Instantiate(placementTexture);

        TextureScale.Bilinear(placementTexture, terrainDimension, terrainDimension);
        ConvertTexture2dToRenderTexture(out renderTexture, placementTexture, placementTexture.width);
        
        UpdateGrassBuffer();

        Debug.Log("Execution Time Overall: " + Time.realtimeSinceStartup);
    }

    private void OnDisable() {
        grassBuffer.Release();
        // heightsBuffer.Release();
        argsBuffer.Release();

        grassBuffer = null;
        // heightsBuffer = null;
        argsBuffer = null;
    }

    private void GetTerrainHeights(Terrain ter, out float[,] heights, int dim) {
        heights = new float[dim, dim];

        for (int x = 0; x < dim; x++) {
            for (int z = 0; z < dim; z++) {
                heights[z,x] = ter.SampleHeight(new Vector3(x, 0, z));
            }
        }
    }

    private void ConvertTexture2dToRenderTexture(out RenderTexture rendTex, in Texture2D inputTex, int res) {
        rendTex = new RenderTexture(res, res, 0);
        rendTex.enableRandomWrite = true;
        RenderTexture.active = rendTex;

        Graphics.Blit(inputTex, rendTex);
    }

    private void UpdateGrassBuffer() {
        int initKernelHandle = placementShader.FindKernel("FindGrassPoints");

        int groupW = Mathf.CeilToInt(renderTexture.width / 8f);
        int groupH = Mathf.CeilToInt(renderTexture.height / 8f);

        grassData = new GrassData[renderTexture.width * renderTexture.height];

        placementShader.SetBuffer(initKernelHandle, "_GrassBuffer", grassBuffer);
        // placementShader.SetBuffer(initKernelHandle, "_HeightBuffer", heightsBuffer);
        placementShader.SetTexture(initKernelHandle, "_PlacementMap", renderTexture);
        placementShader.SetTexture(initKernelHandle, "_HeightMap", heightMap);
        placementShader.SetVector("_TerrainPosition", terrain.transform.position);
        placementShader.SetVector("_Resolution", new Vector2(renderTexture.width, renderTexture.height));
        placementShader.SetFloat(Shader.PropertyToID("_GlobalOffset"), 0.0f);
        placementShader.SetFloat(Shader.PropertyToID("_MaxTerrainHeight"), terrainData.size.y);
        placementShader.SetInt(Shader.PropertyToID("_Scale"), 1);
        placementShader.SetInt(Shader.PropertyToID("_HeightMapRes"), terrainData.heightmapResolution);
        placementShader.Dispatch(initKernelHandle, groupW, groupH, 1);

        uint[] args = new uint[5] { 0, 0, 0, 0, 0 };
        // Arguments for drawing mesh.
        // 0 == number of triangle indices, 1 == population, others are only relevant if drawing submeshes.
        args[0] = (uint)grassMesh.GetIndexCount(0);
        args[1] = (uint)grassBuffer.count;
        args[2] = (uint)grassMesh.GetIndexStart(0);
        args[3] = (uint)grassMesh.GetBaseVertex(0);
        argsBuffer.SetData(args);

        grassMaterial.SetBuffer("positionBuffer", grassBuffer);
        grassMaterial.SetFloat("_Scale", 0.5f);
        Graphics.DrawMeshInstancedIndirect(grassMesh, 0, grassMaterial, new Bounds(Vector3.zero, new Vector3(-1500.0f, 200.0f, 1500.0f)), argsBuffer);
    }

    private void Update() {
        grassMaterial.SetBuffer("positionBuffer", grassBuffer);
        Graphics.DrawMeshInstancedIndirect(grassMesh, 0, grassMaterial, new Bounds(Vector3.zero, new Vector3(-1500.0f, 200.0f, 1500.0f)), argsBuffer);

        if (updateGrass) {
            UpdateGrassBuffer();
        }
    }
}
