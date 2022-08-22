using static System.Runtime.InteropServices.Marshal;
using UnityEngine;


public class Grass : MonoBehaviour
{
    private struct GrassData {
        public Vector4 position;
        public Vector2 uv;
        public uint placePosition;
    };

    [SerializeField] private GameObject player;
    [SerializeField] private Texture2D placementTexture;
    [SerializeField] private ComputeShader placementShader;
    [SerializeField] private Mesh grassMesh;
    [SerializeField] private Material grassMaterial;
    [SerializeField] private int terrainDimension;
    [SerializeField] private float scale = 1;
    [SerializeField] private bool updateGrass = false;

    private int groupW, groupH;
    private Terrain terrain;
    private TerrainData terrainData;
    private RenderTexture renderTexture;
    private RenderTexture heightMap;
    private ComputeBuffer grassBuffer, argsBuffer, heightsBuffer;
    private GrassData[] grassData;

    private void Start() {
        // Get terrain data and components
        terrain = GetComponent<Terrain>();
        terrainData = terrain.terrainData;
        terrainData.size = new Vector3(terrainDimension, terrainData.size.y, terrainDimension);
        int dims = (int) (terrainDimension * scale);

        player.SendMessage("UpdatePlayerPos");

        // Initialize compute buffers
        grassBuffer = new ComputeBuffer(dims * dims, SizeOf(typeof(GrassData)));
        argsBuffer = new ComputeBuffer(1, 5 * sizeof(uint), ComputeBufferType.IndirectArguments);

        // Make a copy of the terrain heightmap
        heightMap = new RenderTexture(terrainData.heightmapTexture.width, terrainData.heightmapTexture.width, terrainData.heightmapTexture.depth, terrainData.heightmapTexture.format);
        heightMap.enableRandomWrite = true;
        Graphics.CopyTexture(terrainData.heightmapTexture, heightMap);

        // Instantiate a copy of the placement texture
        placementTexture = Instantiate(placementTexture);

        // Scale and convert placement texture to a RenderTexture format
        TextureScale.Bilinear(placementTexture, dims, dims);
        TextureToRenderTexture.ConvertTexture2dToRenderTexture(placementTexture, out renderTexture, placementTexture.width);

        // Calculate dispatch groups for width and height of the placement map render texture
        groupW = Mathf.CeilToInt(renderTexture.width / 8f);
        groupH = Mathf.CeilToInt(renderTexture.height / 8f);

        // Initialize GrassData array with the scaled placement map dimensions
        grassData = new GrassData[renderTexture.width * renderTexture.height];

        // Update the compute buffers
        UpdateGrassBuffer();

        Debug.Log("Execution Time Overall: " + Time.realtimeSinceStartup);
    }

    private void OnDisable() {
        // Release and reset buffers
        grassBuffer.Release();
        argsBuffer.Release();
        grassBuffer = null;
        argsBuffer = null;
    }

    // Method to update all of the grass buffers
    private void UpdateGrassBuffer() {
        // Get kernel handle inside of the compute shader
        int initKernelHandle = placementShader.FindKernel("FindGrassPoints");

        // Set compute shader variables and dispatch kernel
        placementShader.SetBuffer(initKernelHandle, "_GrassBuffer", grassBuffer);
        placementShader.SetTexture(initKernelHandle, "_PlacementMap", renderTexture);
        placementShader.SetTexture(initKernelHandle, "_HeightMap", heightMap);
        placementShader.SetVector("_TerrainPosition", terrain.transform.position);
        placementShader.SetVector("_Resolution", new Vector2(renderTexture.width, renderTexture.height));
        placementShader.SetFloat(Shader.PropertyToID("_GlobalOffset"), 0.0f);
        placementShader.SetFloat(Shader.PropertyToID("_MaxTerrainHeight"), terrainData.size.y);
        placementShader.SetFloat(Shader.PropertyToID("_Scale"), scale);
        placementShader.SetInt(Shader.PropertyToID("_HeightMapRes"), terrainData.heightmapResolution);
        placementShader.SetInt(Shader.PropertyToID("_TerrainDim"), terrainDimension);
        placementShader.Dispatch(initKernelHandle, groupW, groupH, 1);

        // Set draw mesh instanced indirect arguments
        uint[] args = new uint[5] { 0, 0, 0, 0, 0 };
        // Arguments for drawing mesh.
        // 0 == number of triangle indices, 1 == population, others are only relevant if drawing submeshes.
        args[0] = (uint)grassMesh.GetIndexCount(0);
        args[1] = (uint)grassBuffer.count;
        args[2] = (uint)grassMesh.GetIndexStart(0);
        args[3] = (uint)grassMesh.GetBaseVertex(0);
        argsBuffer.SetData(args);

        // Set material variables
        grassMaterial.SetBuffer("positionBuffer", grassBuffer);
        grassMaterial.SetFloat("_Scale", scale);
        grassMaterial.SetFloat("_WindStrength", 1);
        grassMaterial.SetFloat("_Rotation", 89);

        // Draw all grass
        Graphics.DrawMeshInstancedIndirect(grassMesh, 0, grassMaterial, new Bounds(Vector3.zero, new Vector3(-1500.0f, 200.0f, 1500.0f)), argsBuffer);
    }

    private void Update() {
        // Redraw the grass
        grassMaterial.SetBuffer("positionBuffer", grassBuffer);
        Graphics.DrawMeshInstancedIndirect(grassMesh, 0, grassMaterial, new Bounds(Vector3.zero, new Vector3(-terrainDimension, terrainData.size.y, terrainDimension)), argsBuffer);

        if (updateGrass) {
            UpdateGrassBuffer();
            updateGrass = false;
        }
    }
}
