using UnityEngine;
using UnityEngine.Tilemaps;
using UnityEngine.EventSystems;
using UnityEngine.UI;

public class Ingame_BuildManager : MonoBehaviour
{
    [Header("타일맵 연결")]
    public Tilemap tilemapFloor;         // 바닥
    public Tilemap tilemapInstallations; // 설치 레이어
    public Tilemap tilemapPreview;       // 미리보기 레이어

    [Header("매니저 연결")]
    public ingame_CodingManager codingManager; // ✅ 코딩 매니저 꼭 연결하세요!

    [Header("설정")]
    public Color activeColor = Color.green;
    public Color normalColor = Color.white;

    // 상태 변수
    private TileBase selectedTile;  
    private Image selectedButton;   
    public bool isPlacementAllowed = false; 

    public bool isBuildMode { get { return selectedTile != null; } }
    
    void Update()
    {
        if (selectedTile == null) return;

        Vector3 worldPos = Camera.main.ScreenToWorldPoint(Input.mousePosition);
        worldPos.z = 0;
        Vector3Int cellPos = tilemapInstallations.WorldToCell(worldPos);

        ShowPreview(cellPos);

        // 클릭 시 설치
        if (Input.GetMouseButtonDown(0) && !EventSystem.current.IsPointerOverGameObject())
        {
            if (isPlacementAllowed) BuildMachine(cellPos);
            else Debug.Log("🚫 코드가 완성되지 않았습니다.");
        }

        // 우클릭 취소
        if (Input.GetMouseButtonDown(1)) CancelBuildMode();
    }

    // ==========================================
    // 1. (중요) 기존 버튼들이 호출하는 함수
    // ==========================================
    public void SelectMachine(TileBase tile, Image buttonImage)
    {
        // 토글: 같은 버튼 또 누르면 취소
        if (selectedButton == buttonImage)
        {
            CancelBuildMode();
            return;
        }

        // 🚀 핵심: 직접 건설하지 않고 코딩 매니저에게 요청!
        if (codingManager != null)
        {
            // 타일의 이름을 그대로 파일명으로 씁니다 (예: Miner -> Miner.py)
            codingManager.OpenFromExternal(tile.name, tile, buttonImage);
        }
        else
        {
            Debug.LogError("❌ Ingame_BuildManager 인스펙터에 CodingManager를 연결해주세요!");
        }
    }

    // ==========================================
    // 2. 코딩 매니저가 "검사 준비됐어!" 하고 호출하는 함수
    // ==========================================
    public void StartBuildMode(TileBase tile, Image buttonImage)
    {
        // 이전 버튼 색 초기화
        if (selectedButton != null) selectedButton.color = normalColor;

        selectedTile = tile;
        selectedButton = buttonImage;
        isPlacementAllowed = false; // 일단 잠금 (코딩 대기)

        // 새 버튼 색 켜기
        if (selectedButton != null) selectedButton.color = activeColor;

        Debug.Log($"🔨 {tile.name} 건설 준비");
    }

    public void SetPlacementPermission(bool isAllowed)
    {
        isPlacementAllowed = isAllowed;
    }
    
    public void CancelBuildMode()
    {
        if (selectedButton != null) selectedButton.color = normalColor;
        
        selectedTile = null;
        selectedButton = null;
        isPlacementAllowed = false; 
        tilemapPreview.ClearAllTiles(); 

        // 코딩창도 닫기
        if(codingManager != null) codingManager.CloseWindowOnly();
    }

    void ShowPreview(Vector3Int pos)
    {
        tilemapPreview.ClearAllTiles();
        if (tilemapFloor.HasTile(pos))
        {
            tilemapPreview.SetTile(pos, selectedTile);
            tilemapPreview.color = isPlacementAllowed ? new Color(0, 1, 0, 0.6f) : new Color(1, 0, 0, 0.6f);
        }
    }

    void BuildMachine(Vector3Int pos)
    {
        if (tilemapFloor.HasTile(pos))
        {
            tilemapInstallations.SetTile(pos, selectedTile);
            Debug.Log($"✅ {selectedTile.name} 설치 완료!");
        }
    }
}