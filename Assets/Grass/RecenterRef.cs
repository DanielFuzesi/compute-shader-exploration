using UnityEngine;

public class RecenterRef : MonoBehaviour
{
    public Terrain terrain;
    public bool reset = true;
    
    private Vector3 size;

    void Update()
    {
        size = terrain.terrainData.size;

        if (reset) {
            Vector3 centerPos = new Vector3(size.x / 2, 1, size.z / 2);
            transform.position = centerPos;
        }
    }

}
