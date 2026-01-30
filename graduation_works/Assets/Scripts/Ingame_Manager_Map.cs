using UnityEngine;
using UnityEngine.Tilemaps;

public class Ingame_Manager_Map : MonoBehaviour {
    [Header("맵 크기 설정")]
    public int currentSize = 6;
    
    [Header("타일맵 연결")]
    public Tilemap tilemapFloor;
    public Tilemap tilemapInstallations;
    public Tilemap tilemapPreview;

    [Header("타일 리소스")]
    public TileBase floorTile;

    void Start() {
        GenerateMap(currentSize);
    }

    public void GenerateMap(int size) {
        if (tilemapFloor != null) tilemapFloor.ClearAllTiles();
        if (tilemapInstallations != null) tilemapInstallations.ClearAllTiles();
        if (tilemapPreview != null) tilemapPreview.ClearAllTiles();

        if (tilemapFloor == null || floorTile == null) {
            Debug.LogError("MapManager: Tilemap or FloorTile missing!");
            return;
        }

        int startX = -(size / 2);
        int startY = -(size / 2);

        for (int x = 0; x < size; x++) {
            for (int y = 0; y < size; y++) {
                Vector3Int pos = new Vector3Int(startX + x, startY + y, 0);
                tilemapFloor.SetTile(pos, floorTile);
            }
        }
        CenterCamera();
    }
    
    void CenterCamera() {
        if (Camera.main != null) {
            Camera.main.transform.position = new Vector3(0, 0, -10);
        }
    }
}