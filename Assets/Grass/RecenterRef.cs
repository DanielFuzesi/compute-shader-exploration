using UnityEngine;

public class RecenterRef : MonoBehaviour
{
    public Terrain terrain;
    public bool reset = true;
    
    private Vector3 size;
    private RaycastHit hit;
    private int groundMask;

    private void Start() {
        groundMask = LayerMask.GetMask("Ground");
    }

    public void UpdatePlayerPos() {
        size = terrain.terrainData.size;

        Vector3 centerPos = new Vector3(size.x / 2, 800, size.z / 2);
        Physics.Raycast(centerPos, Vector3.down, out hit, Mathf.Infinity, groundMask);

        centerPos.y = hit.point.y + 2.8f;

        transform.position = centerPos;
    }
}
