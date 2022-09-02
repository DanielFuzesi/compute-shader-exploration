using static System.Runtime.InteropServices.Marshal;
using System.Collections.Generic;
using UnityEngine;

public class Grass : MonoBehaviour
{
    private struct GrassData {
        public Vector4 position;
        public Vector2 uv;
        public float displacement;
        public uint placePosition;
    };

    private struct GrassChunk {
        public ComputeBuffer argsBuffer;
        public ComputeBuffer argsBufferLOD;
        public ComputeBuffer positionsBuffer;
        public ComputeBuffer culledPositionsBuffer;
        public Bounds bounds;
        public Material material;
        public Material lodMaterial;
    }

    [SerializeField] private Texture2D placementTexture;
    [SerializeField] private ComputeShader initPlacementShader, generateWindShader, cullGrassShader;
    [SerializeField] private Mesh grassMesh;
    [SerializeField] private Mesh grassLODMesh;
    [SerializeField] private Material grassMaterial;
    [SerializeField] private Material LOD_Material;
    [SerializeField] private int terrainDimension;
    [SerializeField] private int densityDividant = 100;
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
    
    private ComputeBuffer voteBuffer, scanBuffer, groupSumArrayBuffer, scannedGroupSumBuffer;
    
    private GrassChunk[] chunks;
    
    private uint[] args;
    private uint[] argsLOD;

    private Bounds fieldBounds;

    private float time;

    private void OnEnable() {
        time = Time.realtimeSinceStartup;

        // Get terrain data and components
        terrain = GetComponent<Terrain>();
        terrainData = terrain.terrainData;
        terrainData.size = new Vector3(terrainDimension, terrainData.size.y, terrainDimension);

        // Calculate chunk variables
        numInstancesPerChunk = Mathf.CeilToInt(terrainDimension / numChunks * scale);
        chunkDimension = numInstancesPerChunk;
        numInstancesPerChunk *= numInstancesPerChunk;

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

        // Convert placement texture to a RenderTexture format
        TextureToRenderTexture.ConvertTexture2dToRenderTexture(placementTexture, out renderTexture, placementTexture.width);

        // Set constant parameters for placement initialization shader
        initPlacementShader.SetTexture(0, "_PlacementMap", renderTexture);
        initPlacementShader.SetTexture(1, "_PlacementMap", renderTexture);
        initPlacementShader.SetTexture(0, "_HeightMap", heightMap);
        initPlacementShader.SetVector("_TerrainPosition", terrain.transform.position);
        initPlacementShader.SetVector("_TerrainResolution", new Vector2(numChunks * chunkDimension, numChunks * chunkDimension));
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

        // Initialize all of the chunks
        InitializeChunks();

        // Set terrain center
        Vector3 terrainCenter = (terrainData.size / 2) + terrain.transform.position;
        terrainCenter.y = 0;

        // Set terrain fields
        fieldBounds = new Bounds(terrainCenter, new Vector3(terrainDimension, terrainData.size.y / 2, terrainDimension));

        // CreateBallsAtPos();

        time = Time.realtimeSinceStartup - time;
        Debug.Log("Execution Time Overall: " + time);
    }

    // Method to initialize all chunks for the terrain
    private void InitializeChunks() {
        // Initialize GrassChunk list
        List<GrassChunk> tempChunksList = new List<GrassChunk>();

        // Add valid chunks to the list
        for (int x = 0; x < numChunks; ++x) {
            for (int y = 0; y < numChunks; ++y) {
                bool valid;
                GrassChunk tempChunk = InitializeChunkBuffer(x, y, out valid);

                if(valid) {
                    tempChunksList.Add(tempChunk);
                }
            }
        }

        // Update number of chunks in case some were culled
        numChunks = tempChunksList.Count;

        // Update chunks array
        chunks = new GrassChunk[numChunks];
        chunks = tempChunksList.ToArray();

        // Set chunk list to null
        tempChunksList = null;
    }

    // Method to initialize the chunk buffers for each individual chunk
    private GrassChunk InitializeChunkBuffer(int xOffset, int yOffset, out bool validChunk) {
        // Initialize chunk and buffers
        GrassChunk chunk = new GrassChunk();

        // Initialize buffer to hold counters
        uint[] chunkCounterData = {0};
        ComputeBuffer chunkCounter = new ComputeBuffer(1, sizeof(uint));
        chunkCounter.SetData(chunkCounterData);

        // Dispatch shader to calculate number of actual grass positions based on texture
        initPlacementShader.SetInt("_XOffset", xOffset);
        initPlacementShader.SetInt("_YOffset", yOffset);
        initPlacementShader.SetBuffer(1, "_ChunkCounter", chunkCounter);
        initPlacementShader.Dispatch(1, chunkDimension, chunkDimension, 1);

        // Retreive counter buffer data and release
        chunkCounter.GetData(chunkCounterData);
        chunkCounter.Release();
        chunkCounter = null;

        // If the chunk doesn't contain any grass, invalidate the chunk and return
        if (chunkCounterData[0] < 1) {
            validChunk = false;
            return chunk;
        }

        // Setup position and argument buffers
        chunk.positionsBuffer = new ComputeBuffer((int) chunkCounterData[0], SizeOf(typeof(GrassData)), ComputeBufferType.Append);
        chunk.culledPositionsBuffer = new ComputeBuffer((int) chunkCounterData[0], SizeOf(typeof(GrassData)));
        chunk.argsBuffer = new ComputeBuffer(1, 5 * sizeof(uint), ComputeBufferType.IndirectArguments);
        chunk.argsBufferLOD = new ComputeBuffer(1, 5 * sizeof(uint), ComputeBufferType.IndirectArguments);

        // Reset append buffer counter value so it always starts from index = 0
        chunk.positionsBuffer.SetCounterValue(0);

        // Set buffer data
        chunk.argsBuffer.SetData(args);
        chunk.argsBufferLOD.SetData(argsLOD);

        // Calculate the dimension of each chunk
        int chunkDim = Mathf.CeilToInt(terrainDimension / numChunks);
        
        // Find center of chunk
        Vector3 c = new Vector3(0.0f, 0.0f, 0.0f);

        c.y = 0.0f;
        c.x += (chunkDim * xOffset) + chunkDim * 0.5f;
        c.z += (chunkDim * yOffset) + chunkDim * 0.5f;
        c.x += terrain.transform.position.x;
        c.z += terrain.transform.position.z;
        
        // Set chunk bounds
        chunk.bounds = new Bounds(c, new Vector3(-chunkDim, terrainData.size.y, chunkDim));

        // Set final initilization compute shader variables and dispatch kernel
        initPlacementShader.SetBuffer(0, "_GrassBuffer", chunk.positionsBuffer);
        initPlacementShader.Dispatch(0, chunkDimension, chunkDimension, 1);

        // Set material variables
        chunk.material = new Material(grassMaterial);
        chunk.material.SetBuffer("positionBuffer", chunk.culledPositionsBuffer);
        chunk.material.SetTexture("_WindTex", wind);
        chunk.material.SetInt("_ChunkNum", xOffset + yOffset * numChunks);

        // Set LOD material
        chunk.lodMaterial = new Material(LOD_Material);
        chunk.lodMaterial.SetBuffer("positionBuffer", chunk.culledPositionsBuffer);
        chunk.lodMaterial.SetTexture("_WindTex", wind);
        chunk.lodMaterial.SetInt("_ChunkNum", xOffset + yOffset * numChunks);

        // Set chunk to valid and return
        validChunk = true;

        return chunk;
    }

    // Method to cull grass
    private void CullGrass(GrassChunk chunk, Matrix4x4 VP, bool noLOD) {
        // Reset Args
        if (noLOD) {
            // All grass is visible
            chunk.argsBuffer.SetData(args);

            cullGrassShader.SetInt("_GrassBufferSize", chunk.positionsBuffer.count);
            cullGrassShader.SetBuffer(5, "_GrassDataBuffer", chunk.positionsBuffer);
            cullGrassShader.Dispatch(5, chunk.positionsBuffer.count, 1, 1);
        } else {
            // Less visible grass
            chunk.argsBufferLOD.SetData(argsLOD);

            cullGrassShader.SetInt("_GrassBufferSize", chunk.positionsBuffer.count);
            cullGrassShader.SetInt("_DensityDividant", densityDividant);
            cullGrassShader.SetBuffer(6, "_GrassDataBuffer", chunk.positionsBuffer);
            cullGrassShader.Dispatch(6, chunk.positionsBuffer.count, 1, 1);
        }

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

    // Method to generate wind texture
    private void GenerateWind() {
        generateWindShader.SetTexture(0, "_WindMap", wind);
        generateWindShader.SetFloat("_Time", Time.time * windSpeed);
        generateWindShader.SetFloat("_Frequency", frequency);
        generateWindShader.SetFloat("_Amplitude", windStrength);
        generateWindShader.Dispatch(0, numWindThreadGroups, numWindThreadGroups, 1);
    }

    private void Update() {
        // Find camera matrix
        Matrix4x4 P = Camera.main.projectionMatrix;
        Matrix4x4 V = Camera.main.transform.worldToLocalMatrix;
        Matrix4x4 VP = P * V;

        // Generate wind texture
        GenerateWind();

        // Cull and draw grass for each chunk
        for (int i = 0; i < chunks.Length; ++i) {
            float dist = Vector3.Distance(Camera.main.transform.position, chunks[i].bounds.center);

            bool noLOD = dist < lodCutoff;

            CullGrass(chunks[i], VP, noLOD);
            if (noLOD)
                Graphics.DrawMeshInstancedIndirect(grassMesh, 0, chunks[i].material, fieldBounds, chunks[i].argsBuffer);
            else
                Graphics.DrawMeshInstancedIndirect(grassLODMesh, 0, chunks[i].lodMaterial, fieldBounds, chunks[i].argsBufferLOD);
        }
    }

    // Release all buffers and set them to null
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


        for (int i = 0; i < chunks.Length; ++i) {
            FreeChunk(chunks[i]);
        }

        chunks = null;
    }

    // Method to release all buffers in the chunk and set them to null
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
            for (int i = 0; i < chunks.Length; ++i) {
                Gizmos.DrawWireCube(chunks[i].bounds.center, chunks[i].bounds.size);
            }
        }

        Gizmos.color = Color.green;
        Gizmos.DrawWireCube(fieldBounds.center, fieldBounds.size);
    }
}
