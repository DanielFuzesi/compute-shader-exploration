using static System.Runtime.InteropServices.Marshal;
using UnityEngine;


public class Grass : MonoBehaviour
{
    private struct GrassData {
        public Vector4 position;
        public Vector2 uv;
        public float displacement;
        public bool placePosition;
    };

    private struct GrassChunk {
        public ComputeBuffer argsBuffer;
        public ComputeBuffer argsBufferLOD;
        public ComputeBuffer positionsBuffer;
        public ComputeBuffer culledPositionsBuffer;
        public Bounds bounds;
        public Material material;
    }

    [SerializeField] private GameObject player;
    [SerializeField] private Texture2D placementTexture;
    [SerializeField] private ComputeShader initPlacementShader, generateWindShader, cullGrassShader;
    [SerializeField] private Mesh grassMesh;
    [SerializeField] private Mesh grassLODMesh;
    [SerializeField] private Material grassMaterial;
    [SerializeField] private int terrainDimension;
    [SerializeField] private int numChunks = 1;
    [SerializeField] private float scale = 1;

    [Header("Culling")]
    [Range(0, 1000.0f)]
    [SerializeField] private float lodCutoff = 1000.0f;
    [Range(0, 1000.0f)]
    [SerializeField] private float distanceCutoff = 1000.0f;

    [Space]

    [Header("Wind")]
    [SerializeField] private float windSpeed = 1.0f;
    [SerializeField] private float frequency = 1.0f;
    [SerializeField] private float windStrength = 1.0f;

    [Space]

    private int numInstancesPerChunk, chunkDimension, numThreadGroups, numWindThreadGroups, numVoteThreadGroups, numGroupScanThreadGroups;
    
    private Terrain terrain;
    private TerrainData terrainData;

    private RenderTexture renderTexture, heightMap, wind;
    
    private ComputeBuffer grassBuffer, culledPositionsBuffer, argsBuffer, voteBuffer, scanBuffer, groupSumArrayBuffer, scannedGroupSumBuffer;
    
    private GrassData[] grassData;
    private GrassChunk[] chunks;
    
    private uint[] args;
    private uint[] argsLOD;
    private float[,] heights;

    private Bounds fieldBounds;

    private void OnEnable() {
        // Get terrain data and components
        terrain = GetComponent<Terrain>();
        terrainData = terrain.terrainData;
        terrainData.size = new Vector3(terrainDimension, terrainData.size.y, terrainDimension);
        numInstancesPerChunk = Mathf.CeilToInt(terrainDimension / numChunks * scale);
        chunkDimension = numInstancesPerChunk;
        numInstancesPerChunk *= numInstancesPerChunk;

        player.SendMessage("UpdatePlayerPos");

        // Calculate dispatch groups for width and height of the placement map render texture
        numThreadGroups = Mathf.CeilToInt(numInstancesPerChunk / 128.0f);

        if (numThreadGroups > 128) {
            int powerOfTwo = 128;
            while (powerOfTwo < numThreadGroups)
                powerOfTwo *= 2;
            
            numThreadGroups = powerOfTwo;
        } else {
            while (128 % numThreadGroups != 0)
                numThreadGroups++;
        }

        numVoteThreadGroups = Mathf.CeilToInt(numInstancesPerChunk / 128.0f);
        numGroupScanThreadGroups = Mathf.CeilToInt(numInstancesPerChunk / 1024.0f);

        // ADD RESOURCES LOAD HERE LATER FOR COMPUTE SHADER LOADING!
        // TODO: Load compute shaders

        // Initialize vote and scan buffers for culling
        voteBuffer = new ComputeBuffer(numInstancesPerChunk, 4);
        scanBuffer = new ComputeBuffer(numInstancesPerChunk, 4);
        groupSumArrayBuffer = new ComputeBuffer(numThreadGroups, 4);
        scannedGroupSumBuffer = new ComputeBuffer(numThreadGroups, 4);

        // Make a copy of the terrain heightmap
        heightMap = new RenderTexture(terrainData.heightmapTexture.width, terrainData.heightmapTexture.width, terrainData.heightmapTexture.depth, terrainData.heightmapTexture.format);
        heightMap.enableRandomWrite = true;
        Graphics.CopyTexture(terrainData.heightmapTexture, heightMap);

        // Instantiate a copy of the placement texture
        placementTexture = Instantiate(placementTexture);

        // Scale and convert placement texture to a RenderTexture format
        int texDimension = (int) (terrainDimension * scale);
        // Texture2D heightMapTex = toTexture2D(heightMap);

        TextureScale.Bilinear(placementTexture, texDimension, texDimension);
        // TextureScale.Bilinear(heightMapTex, texDimension, texDimension);

        TextureToRenderTexture.ConvertTexture2dToRenderTexture(placementTexture, out renderTexture, placementTexture.width);
        // TextureToRenderTexture.ConvertTexture2dToRenderTexture(heightMapTex, out heightMap, heightMapTex.width);

        Debug.Log(heightMap.width);
        Debug.Log(renderTexture.width);

        // Set constant parameters for placement initialization shader
        initPlacementShader.SetTexture(0, "_PlacementMap", renderTexture);
        initPlacementShader.SetTexture(0, "_HeightMap", heightMap);
        initPlacementShader.SetVector("_TerrainPosition", terrain.transform.position);
        initPlacementShader.SetVector("_Resolution", new Vector2(renderTexture.width, renderTexture.height));
        initPlacementShader.SetFloat("_MaxTerrainHeight", terrainData.size.y);
        initPlacementShader.SetFloat("_Scale", scale);
        initPlacementShader.SetFloat("_MeshHeight", grassMesh.bounds.size.y);
        initPlacementShader.SetInt("_HeightMapRes", heightMap.width);
        initPlacementShader.SetInt("_TerrainDim", terrainDimension);
        initPlacementShader.SetInt("_NumChunks", numChunks);
        initPlacementShader.SetInt("_ChunkDimension", chunkDimension);

        // Create wind texture
        wind = new RenderTexture(1024, 1024, 0, RenderTextureFormat.ARGBFloat, RenderTextureReadWrite.Linear);
        wind.enableRandomWrite = true;
        wind.Create();
        numWindThreadGroups = Mathf.CeilToInt(wind.height / 8.0f);

        // Set draw mesh instanced indirect arguments
        args = new uint[5] { 0, 0, 0, 0, 0 };
        args[0] = (uint)grassMesh.GetIndexCount(0);
        args[1] = (uint)0;
        args[2] = (uint)grassMesh.GetIndexStart(0);
        args[3] = (uint)grassMesh.GetBaseVertex(0);

        argsLOD = new uint[5] { 0, 0, 0, 0, 0 };
        argsLOD[0] = (uint)grassLODMesh.GetIndexCount(0);
        argsLOD[1] = (uint)0;
        argsLOD[2] = (uint)grassLODMesh.GetIndexStart(0);
        argsLOD[3] = (uint)grassLODMesh.GetBaseVertex(0);

        InitializeChunks();

        Vector3 terrainCenter = (terrainData.size / 2) + terrain.transform.position;
        terrainCenter.y = 0;

        fieldBounds = new Bounds(terrainCenter, new Vector3(terrainDimension, terrainData.size.y / 2, terrainDimension));

        Debug.Log("Execution Time Overall: " + Time.realtimeSinceStartup);
    }

    private void InitializeChunks() {
        // Initialize GrassData array with the scaled placement map dimensions
        chunks = new GrassChunk[numChunks * numChunks];

        for (int x = 0; x < numChunks; ++x) {
            for (int y = 0; y < numChunks; ++y) {
                chunks[x + y * numChunks] = InitializeChunkBuffer(x, y);
            }
        }
    }

    // Method to update all of the grass buffers
    private GrassChunk InitializeChunkBuffer(int xOffset, int yOffset) {
        GrassChunk chunk = new GrassChunk();

        chunk.argsBuffer = new ComputeBuffer(1, 5 * sizeof(uint), ComputeBufferType.IndirectArguments);
        chunk.argsBufferLOD = new ComputeBuffer(1, 5 * sizeof(uint), ComputeBufferType.IndirectArguments);

        chunk.argsBuffer.SetData(args);
        chunk.argsBufferLOD.SetData(argsLOD);

        chunk.positionsBuffer = new ComputeBuffer(numInstancesPerChunk, SizeOf(typeof(GrassData)));
        chunk.culledPositionsBuffer = new ComputeBuffer(numInstancesPerChunk, SizeOf(typeof(GrassData)));
        int chunkDim = Mathf.CeilToInt(terrainDimension / numChunks);
        
        Vector3 c = new Vector3(0.0f, 0.0f, 0.0f);

        c.y = 0.0f;
        c.x += (chunkDim * xOffset) + chunkDim * 0.5f;
        c.z += (chunkDim * yOffset) + chunkDim * 0.5f;
        c.x += terrain.transform.position.x;
        c.z += terrain.transform.position.z;
        
        chunk.bounds = new Bounds(c, new Vector3(-chunkDim, terrainData.size.y, chunkDim));

        // Set compute shader variables and dispatch kernel
        initPlacementShader.SetInt("_XOffset", xOffset);
        initPlacementShader.SetInt("_YOffset", yOffset);
        initPlacementShader.SetBuffer(0, "_GrassBuffer", chunk.positionsBuffer);
        initPlacementShader.Dispatch(0, chunkDimension, chunkDimension, 1);
        
        // Set material variables
        chunk.material = new Material(grassMaterial);
        chunk.material.SetBuffer("positionBuffer", chunk.culledPositionsBuffer);
        chunk.material.SetTexture("_WindTex", wind);
        chunk.material.SetInt("_ChunkNum", xOffset + yOffset * numChunks);

        return chunk;
    }

    private void CullGrass(GrassChunk chunk, Matrix4x4 VP, bool noLOD) {
        // Reset Args
        if (noLOD)
            chunk.argsBuffer.SetData(args);
        else
            chunk.argsBufferLOD.SetData(argsLOD);

        // Vote
        cullGrassShader.SetMatrix("MATRIX_VP", VP);
        cullGrassShader.SetBuffer(0, "_GrassDataBuffer", chunk.positionsBuffer);
        cullGrassShader.SetBuffer(0, "_VoteBuffer", voteBuffer);
        cullGrassShader.SetVector("_CameraPosition",  Camera.main.transform.position);
        cullGrassShader.SetVector("_TerrainSize",  terrainData.size);
        cullGrassShader.SetVector("_TerrainPosition",  terrain.transform.position);
        cullGrassShader.SetFloat("_Distance", distanceCutoff);
        cullGrassShader.Dispatch(0, numVoteThreadGroups, 1, 1);

        // Scan Instances
        cullGrassShader.SetBuffer(1, "_VoteBuffer", voteBuffer);
        cullGrassShader.SetBuffer(1, "_ScanBuffer", scanBuffer);
        cullGrassShader.SetBuffer(1, "_GroupSumArray", groupSumArrayBuffer);
        cullGrassShader.Dispatch(1, numThreadGroups, 1, 1);

        // Scan Groups
        cullGrassShader.SetInt("_NumOfGroups", numThreadGroups);
        cullGrassShader.SetBuffer(2, "_GroupSumArrayIn", groupSumArrayBuffer);
        cullGrassShader.SetBuffer(2, "_GroupSumArrayOut", scannedGroupSumBuffer);
        cullGrassShader.Dispatch(2, numGroupScanThreadGroups, 1, 1);

        // Compact
        cullGrassShader.SetBuffer(3, "_GrassDataBuffer", chunk.positionsBuffer);
        cullGrassShader.SetBuffer(3, "_VoteBuffer", voteBuffer);
        cullGrassShader.SetBuffer(3, "_ScanBuffer", scanBuffer);
        cullGrassShader.SetBuffer(3, "_ArgsBuffer", noLOD ? chunk.argsBuffer : chunk.argsBufferLOD);
        cullGrassShader.SetBuffer(3, "_CulledGrassOutputBuffer", chunk.culledPositionsBuffer);
        cullGrassShader.SetBuffer(3, "_GroupSumArray", scannedGroupSumBuffer);
        cullGrassShader.Dispatch(3, numThreadGroups, 1, 1);
    }

    private void GenerateWind() {
        generateWindShader.SetTexture(0, "_WindMap", wind);
        generateWindShader.SetFloat("_Time", Time.time * windSpeed);
        generateWindShader.SetFloat("_Frequency", frequency);
        generateWindShader.SetFloat("_Amplitude", windStrength);
        generateWindShader.Dispatch(0, numWindThreadGroups, numWindThreadGroups, 1);
    }

    private void Update() {
        Matrix4x4 P = Camera.main.projectionMatrix;
        Matrix4x4 V = Camera.main.transform.worldToLocalMatrix;
        Matrix4x4 VP = P * V;



        GenerateWind();

        for (int i = 0; i < numChunks * numChunks; ++i) {
            float dist = Vector3.Distance(Camera.main.transform.position, chunks[i].bounds.center);

            bool noLOD = dist < lodCutoff;

            CullGrass(chunks[i], VP, noLOD);
            if (noLOD)
                Graphics.DrawMeshInstancedIndirect(grassMesh, 0, chunks[i].material, fieldBounds, chunks[i].argsBuffer);
            else
                Graphics.DrawMeshInstancedIndirect(grassLODMesh, 0, chunks[i].material, fieldBounds, chunks[i].argsBufferLOD);
        }
    }

    void OnDisable() {
        voteBuffer.Release();
        scanBuffer.Release();
        groupSumArrayBuffer.Release();
        scannedGroupSumBuffer.Release();
        wind.Release();
        wind = null;
        scannedGroupSumBuffer = null;
        voteBuffer = null;
        scanBuffer = null;
        groupSumArrayBuffer = null;


        for (int i = 0; i < numChunks * numChunks; ++i) {
            FreeChunk(chunks[i]);
        }

        chunks = null;
    }

    void FreeChunk(GrassChunk chunk) {
        chunk.positionsBuffer.Release();
        chunk.positionsBuffer = null;
        chunk.culledPositionsBuffer.Release();
        chunk.culledPositionsBuffer = null;
        chunk.argsBuffer.Release();
        chunk.argsBuffer = null;
        chunk.argsBufferLOD.Release();
        chunk.argsBufferLOD = null;
    }
    void OnDrawGizmos() {
        Gizmos.color = Color.yellow;
        if (chunks != null) {
            for (int i = 0; i < numChunks * numChunks; ++i) {
                Gizmos.DrawWireCube(chunks[i].bounds.center, chunks[i].bounds.size);
            }
        }
    }
}
