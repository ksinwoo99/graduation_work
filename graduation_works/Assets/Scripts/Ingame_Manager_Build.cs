using UnityEngine;
using UnityEngine.Tilemaps;
using UnityEngine.EventSystems;
using UnityEngine.UI;
using System.Collections.Generic;

public class Ingame_Manager_Build : MonoBehaviour {
    public static Ingame_Manager_Build Instance;

    [Header("타일맵 연결")]
    public Tilemap tilemapFloor;
    public Tilemap tilemapInstallations;
    public Tilemap tilemapPreview;

    [Header("매니저 연결")]
    public Ingame_Manager_Coding codingManager;

    [Header("설정")]
    public Color activeColor = Color.green;
    public Color normalColor = Color.white;
    public GameObject floatingTextPrefab;
    
    [Header("철거 설정")]
    public Texture2D cursorDemolish;
    public Vector2 cursorHotspot = Vector2.zero;
    // 🔥 [추가] 철거할 때 보여줄 타일 (빨간색으로 변할 녀석)
    public TileBase demolishBaseTile; 

    private TileBase selectedTile;  
    private Image selectedButton;   
    
    private Iteminfo_Base selectedInfo; 
    private logic_CodingBase selectedDemolishLogic; 

    public bool isPlacementAllowed = false;

    [Header("데이터 로드 시 프리팹")]
    public GameObject[] loadablePrefabs;

    private Dictionary<Vector3Int, int> installedCosts = new Dictionary<Vector3Int, int>();
    private Dictionary<Vector3Int, GameObject> installedObjects = new Dictionary<Vector3Int, GameObject>();

    private bool isDemolishMode { get { return selectedDemolishLogic != null; } }

    // 🔥 [수정] 철거 모드일 때도 "건설 모드"로 쳐줍니다 (그래야 시간이 멈춤)
    public bool isBuildMode { get { return selectedTile != null || isDemolishMode; } }
    
    void Awake() { if (Instance == null) Instance = this; }

    public bool IsOccupied(Vector3Int pos) {
        return tilemapInstallations.HasTile(pos) || installedObjects.ContainsKey(pos);
    }

    void Update() {
        // 🔥 [수정] 타일이 없어도 '철거 모드'라면 통과!
        if (selectedTile == null && !isDemolishMode) {
            Cursor.SetCursor(null, Vector2.zero, CursorMode.Auto);
            return;
        }

        Vector3 worldPos = Camera.main.ScreenToWorldPoint(Input.mousePosition);
        worldPos.z = 0;
        Vector3Int cellPos = tilemapInstallations.WorldToCell(worldPos);

        ShowPreview(cellPos);
        UpdateCursor(cellPos);

        if (Input.GetMouseButtonDown(0) && !EventSystem.current.IsPointerOverGameObject()) {
            if (isDemolishMode) TryDemolishMachine(cellPos);
            else {
                if (!isPlacementAllowed) {
                    Vector3 clickPos = Camera.main.ScreenToWorldPoint(Input.mousePosition);
                    clickPos.z = -5f;
                    ShowFloatingText("코드 오류!", clickPos);
                } else TryBuildMachine(cellPos);
            }
        }
        if (Input.GetMouseButtonDown(1)) CancelBuildMode();
    }

    public void SelectMachine(Image buttonImage) {
        if (selectedButton == buttonImage) {
            CancelBuildMode();
            return;
        }

        logic_Demolish demolish = buttonImage.GetComponent<logic_Demolish>();
        if (demolish != null) {
            selectedDemolishLogic = demolish;
            if (codingManager != null) codingManager.CloseWindowOnly();
            StartBuildMode(null, buttonImage);
            isPlacementAllowed = true;
            return;
        }

        Iteminfo_Base info = buttonImage.GetComponent<Iteminfo_Base>();
        if (info == null) return;
        
        selectedInfo = info;
        
        if (codingManager != null) {
            logic_CodingBase prefabLogic = info.GetLogicFromPrefab();
            codingManager.OpenFromExternal(info.machineName, info.iconTile, buttonImage, prefabLogic);
        }
    }

    public void StartBuildMode(TileBase tile, Image buttonImage) {
        if (selectedButton != null) selectedButton.color = normalColor;
        
        if (selectedInfo != null) selectedTile = selectedInfo.iconTile;
        else selectedTile = tile; 

        selectedButton = buttonImage;
        isPlacementAllowed = false;
        if (selectedButton != null) selectedButton.color = activeColor;
    }

    public void SetPlacementPermission(bool isAllowed) {
        isPlacementAllowed = isAllowed;
    }
    
    public void CancelBuildMode() {
        if (selectedButton != null) selectedButton.color = normalColor;
        
        selectedTile = null;
        selectedButton = null;
        selectedInfo = null;
        selectedDemolishLogic = null;
        isPlacementAllowed = false; 
        tilemapPreview.ClearAllTiles(); 
        
        Cursor.SetCursor(null, Vector2.zero, CursorMode.Auto);
        
        if(codingManager != null) codingManager.CloseWindowOnly();
    }

    void ShowPreview(Vector3Int pos) {
        tilemapPreview.ClearAllTiles();
        
        if (isDemolishMode) {
            if (IsOccupied(pos)) {
                // 🔥 [수정] 미리 설정해둔 '철거용 타일'을 빨갛게 보여줌
                tilemapPreview.SetTile(pos, demolishBaseTile); 
                tilemapPreview.color = new Color(1, 0, 0, 0.6f); // 반투명 빨강
            }
            return;
        }

        if (tilemapFloor.HasTile(pos)) {
            bool canBuild = !IsOccupied(pos); 
            tilemapPreview.SetTile(pos, selectedTile);
            tilemapPreview.color = (isPlacementAllowed && canBuild) ? new Color(0, 1, 0, 0.6f) : new Color(1, 0, 0, 0.6f);
        }
    }

    void TryBuildMachine(Vector3Int pos) {
        Vector3 tileWorldPos = tilemapInstallations.GetCellCenterWorld(pos);
        tileWorldPos.z = -1f;

        if (!tilemapFloor.HasTile(pos) || IsOccupied(pos)) {
            ShowFloatingText("설치 불가능!", tileWorldPos);
            return;
        }

        int cost = (selectedInfo != null) ? selectedInfo.buildCost : 0;
        
        if (Ingame_Manager_Resource.Instance != null) {
            if (Ingame_Manager_Resource.Instance.SpendGold(cost)) {
                
                tilemapInstallations.SetTile(pos, null); 
                
                if (installedCosts.ContainsKey(pos)) installedCosts[pos] = cost;
                else installedCosts.Add(pos, cost);

                if (selectedInfo != null && selectedInfo.machinePrefab != null) {
                    GameObject machine = Instantiate(selectedInfo.machinePrefab, tileWorldPos, Quaternion.identity);
                    
                    logic_Miner_Common createdMiner = machine.GetComponent<logic_Miner_Common>();
                    if (createdMiner != null) {
                        createdMiner.InitializeMiner(1); 
                    }

                    if (installedObjects.ContainsKey(pos)) installedObjects[pos] = machine;
                    else installedObjects.Add(pos, machine);
                }

                Debug.Log($"설치 완료 (-{cost} G)");
            } else {
                ShowFloatingText("골드가 부족합니다!", tileWorldPos);
            }
        }
    }

    void TryDemolishMachine(Vector3Int pos) {
        if (!IsOccupied(pos)) return;
        Vector3 tileWorldPos = tilemapInstallations.GetCellCenterWorld(pos);
        tileWorldPos.z = -5f;

        int refund = 0;
        if (installedCosts.ContainsKey(pos)) {
            refund = (int)(installedCosts[pos] * 0.8f); 
            installedCosts.Remove(pos);
        }
        tilemapInstallations.SetTile(pos, null);
        if (installedObjects.ContainsKey(pos)) {
            GameObject targetMachine = installedObjects[pos];
            if (targetMachine != null) Destroy(targetMachine);
            installedObjects.Remove(pos);
        }
        if (Ingame_Manager_Resource.Instance != null) {
            Ingame_Manager_Resource.Instance.EarnGold(refund);
            ShowFloatingText($"+{refund} G", tileWorldPos);
        }
    }
    
    public void ShowFloatingText(string msg, Vector3 worldPos) {
        if (floatingTextPrefab != null) {
            worldPos.z = -5f; 
            GameObject go = Instantiate(floatingTextPrefab, worldPos, Quaternion.identity);
            Ingame_UI_Message uiMsg = go.GetComponent<Ingame_UI_Message>();
            if (uiMsg != null) uiMsg.Setup(msg, worldPos);
        }
    }

    public void LoadBuilding(string type, Vector3Int pos, int remainingCount) {
        GameObject targetPrefab = null;
        foreach (var prefab in loadablePrefabs) {
            if (prefab.name.Contains(type)) { targetPrefab = prefab; break; }
        }
        if (targetPrefab == null) return;

        Vector3 worldPos = tilemapInstallations.GetCellCenterWorld(pos);
        worldPos.z = -1f;
        GameObject machine = Instantiate(targetPrefab, worldPos, Quaternion.identity);
        logic_Miner_Common miner = machine.GetComponent<logic_Miner_Common>();
        if (miner != null) miner.InitializeMiner(remainingCount);

        if (installedObjects.ContainsKey(pos)) installedObjects[pos] = machine;
        else installedObjects.Add(pos, machine);
        if (installedCosts.ContainsKey(pos)) installedCosts[pos] = 0;
        else installedCosts.Add(pos, 0);
        tilemapInstallations.SetTile(pos, null);
    }
    
    public Dictionary<Vector3Int, GameObject> GetInstalledObjects() { return installedObjects; }
    void UpdateCursor(Vector3Int cellPos) {
        if (isDemolishMode && IsOccupied(cellPos)) Cursor.SetCursor(cursorDemolish, cursorHotspot, CursorMode.Auto);
        else Cursor.SetCursor(null, Vector2.zero, CursorMode.Auto);
    }
}