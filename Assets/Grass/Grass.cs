using UnityEngine;


public class Grass : MonoBehaviour
{
    private struct GrassData {
        public Vector3 position;
        public Vector2 offset;
    };

    [SerializeField] private Texture2D placementTexture;
    [SerializeField] private ComputeShader computeShader;

    private Terrain terrain;
    private TerrainData terrainData;
    private RenderTexture renderTexture;
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

    private void OnEnable() {
        terrain = GetComponent<Terrain>();
        terrainData = terrain.terrainData;

        int size = (int)terrainData.size.x;

        placementTexture = ScaleTexture(size, placementTexture);
        ConvertTexture2dToRenderTexture(out renderTexture, placementTexture, size);

        SpawnGrass();
    }

    private void ConvertTexture2dToRenderTexture(out RenderTexture rendTex, in Texture2D inputTex, int res) {
        rendTex = new RenderTexture(res, res, 0);
        rendTex.enableRandomWrite = true;
        RenderTexture.active = rendTex;

        Graphics.Blit(inputTex, rendTex);
    }

    private void SpawnGrass() {
        int vector3Size = sizeof(float) * 3;
        int vector2Size = sizeof(float) * 2;
        int totalSize = vector3Size + vector2Size;

        grassData = new GrassData[placementTexture.width * placementTexture.height];
        ComputeBuffer grassBuffer = new ComputeBuffer(grassData.Length, totalSize);

        int groups = Mathf.CeilToInt(grassData.Length / 8.0f);

        foreach (GrassData g in grassData) {
            if (g.position != Vector3.zero) {
                Debug.Log(g.position);
            }
        }

        Debug.Log("-----------------------------------------------------");

        computeShader.SetBuffer(0, "_Grass", grassBuffer);
        computeShader.SetTexture(0, "_PlacementTexture", renderTexture);
        computeShader.SetVector("_Resolution", new Vector2(placementTexture.width, placementTexture.height));
        computeShader.Dispatch(0, groups, groups, 1);

        grassBuffer.GetData(grassData);
        
        foreach (GrassData g in grassData) {
            if (g.position != Vector3.zero) {
                Debug.Log(g.position);
            }
        }

        grassBuffer.Dispose();
    }

    // Add this to a tools script to keep it reusable
    private Texture2D ScaleTexture(int targetWidth, Texture2D placementMap) {
        int terrainDim = targetWidth;

        Texture2D result = new Texture2D(terrainDim, terrainDim, TextureFormat.RGB24, false);

        int incX = (int)(1.0f / result.width);
        int incY = (int)(1.0f / result.height);

        Color[] colors = new Color[result.width * result.width];
        int colorIndex = 0;

        for (int y = 0; y < result.height; ++y) {
            for (int x = 0; x < result.width; ++x) {
                colors[colorIndex] = placementMap.GetPixelBilinear((float)x / (float)result.width, (float)y / (float)result.height);
                colorIndex += 1;
            }
        }

        result.SetPixels(colors);
        result.Apply();

        return result;
    }

}
