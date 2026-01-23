using UnityEngine;
using UnityEngine.Tilemaps; // 타일맵 필수

public class Ingame_MapManager : MonoBehaviour
{
    // 바닥 및 설치물 타일맵
    [Header("타일맵")]
    public Tilemap floor;
    public Tilemap installation;
    
    [Header("타일 에셋")]
    public TileBase floor_Img; // 바닥으로 쓸 타일 이미지 (Project창에서 Tile 생성 후 연결)
    public TileBase floor_Locked; // (선택사항) 아직 확장 안 된 곳을 어둡게 표시하고 싶다면 사용

    [Header("설정")]
    public int currentSize = 6; // 현재 크기 (5x5)

    void Start()
    {
        UpdateMapSize(currentSize);
        CenterCamera();
    }
    
    public void UpdateMapSize(int newSize)
    {
        currentSize = newSize;
        GenerateFloor();
    }

    private void GenerateFloor()
    {
        // 기존 바닥 초기화 (필요하다면)
        floor.ClearAllTiles();
        int range = currentSize / 2; 

        for (int x = -range; x <= range; x++)
        {
            for (int y = -range; y <= range; y++)
            {
                Vector3Int pos = new Vector3Int(x, y, 0);
                floor.SetTile(pos, floor_Img);
            }
        }
    }

    // 나중에 버튼을 눌러서 맵을 확장할 때 이 함수를 호출
    public void ExpandMap()
    {
        UpdateMapSize(currentSize + 2);
        Debug.Log("맵이 확장되었습니다! 현재 크기: " + currentSize);
    }

    // 카메라를 맵 중앙으로 이동
    void CenterCamera()
    {
        Camera.main.transform.position = new Vector3(0, 0, -10);
    }
}