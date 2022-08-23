using UnityEngine;

public class RecenterRef : MonoBehaviour
{
    public Terrain terrain;
    public bool reset = true;
    
    private Vector3 size;
    private Vector3 pos;
    private RaycastHit hit;
    private int groundMask;

    private void Start() {
        groundMask = LayerMask.GetMask("Ground");
    }

    public void UpdatePlayerPos() {
        size = terrain.terrainData.size;
        pos = terrain.transform.position;

        Vector3 centerPos = new Vector3((size.x / 2) + pos.x, 800, (size.z / 2) + pos.z);
        Physics.Raycast(centerPos, Vector3.down, out hit, Mathf.Infinity, groundMask);

        centerPos.y = hit.point.y + 1.5f;

        transform.position = centerPos;
    }
}
