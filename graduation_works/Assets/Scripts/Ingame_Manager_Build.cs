using UnityEngine;
using UnityEngine.Tilemaps;
using UnityEngine.EventSystems;
using UnityEngine.UI;
using TMPro;
using System.Collections.Generic;

public enum BuildDirection { Down = 0, Left = 1, Up = 2, Right = 3 }

public class SpentCost {
    public int gold;
    public List<ResourceCost> resources = new List<ResourceCost>();
}

public class DemolishedInfo {
    public GameObject machineInstance;
    public BuildDirection dir;
    public SpentCost originalCost; 
    public TileBase originalTile;
    public SpentCost refundedCost; 
    // ✨ [추가] 2x2 등 여러 칸을 차지하는 건물의 철거 복구를 위해 모든 좌표를 저장합니다.
    public List<Vector3Int> occupiedCells = new List<Vector3Int>(); 
}

public class Ingame_Manager_Build : MonoBehaviour {
    public static Ingame_Manager_Build Instance;

    [Header("타일맵 연결")]
    public Tilemap tilemapFloor;
    public Tilemap tilemapInstallations;
    public Tilemap tilemapPreview;

    [Header("타일맵 확장 (부동산) 설정")]
    public TileBase floorTileBase; 
    public int currentMapSize = 4; 
    public int expandCount = 0;    
    public int baseExpandCost = 1000; 
    public int expandSizeStep = 2; 

    [Header("매니저 연결")]
    public Ingame_Manager_Coding codingManager;
    public Ingame_UI_MachineInfo machineInfoUI;

    [Header("설정")]
    public Color activeColor = Color.green;
    public Color normalColor = Color.white;
    public GameObject floatingTextPrefab;
    
    [Header("철거 설정")]
    public Texture2D cursorDemolish;
    public Vector2 cursorHotspot = Vector2.zero;
    public TileBase demolishBaseTile; 

    [Header("방향 설정")]
    public GameObject previewArrowPrefab; 
    public BuildDirection currentDirection = BuildDirection.Down; 
    private GameObject previewArrowInstance; 

    [Header("롤백 UI 설정")]
    public GameObject confirmPanel; 
    private bool isConfirming = false; 

    private List<Vector3Int> sessionBuilt = new List<Vector3Int>();
    private Dictionary<Vector3Int, DemolishedInfo> sessionDemolished = new Dictionary<Vector3Int, DemolishedInfo>();

    private TileBase selectedTile;  
    private Image selectedButton;   
    
    private Iteminfo_Base selectedInfo; 
    private logic_CodingBase selectedDemolishLogic; 

    public bool isPlacementAllowed = false;

    [Header("데이터 로드 시 프리팹")]
    public GameObject[] loadablePrefabs;

    private Dictionary<Vector3Int, SpentCost> installedCosts = new Dictionary<Vector3Int, SpentCost>();
    private Dictionary<Vector3Int, GameObject> installedObjects = new Dictionary<Vector3Int, GameObject>();
    public Dictionary<Vector3Int, BuildDirection> installedDirections = new Dictionary<Vector3Int, BuildDirection>();
    private Dictionary<Vector3Int, GameObject> installedArrowDict = new Dictionary<Vector3Int, GameObject>();

    private bool isDemolishMode { get { return selectedDemolishLogic != null; } }
    public bool isBuildMode { get { return selectedTile != null || isDemolishMode; } }
    
    void Awake() { if (Instance == null) Instance = this; }

    void Start() { GenerateFloor(); }

    public bool IsOccupied(Vector3Int pos) {
        return tilemapInstallations.HasTile(pos) || installedObjects.ContainsKey(pos);
    }

    // ✨ [핵심 추가 1] 건물의 크기(예: 2x2)와 방향을 계산해서 차지하는 모든 타일 좌표를 반환하는 함수
    public List<Vector3Int> GetBuildingCells(Vector3Int origin, Vector2Int baseSize, BuildDirection dir) {
        List<Vector3Int> cells = new List<Vector3Int>();
        int width = baseSize.x;
        int height = baseSize.y;

        // 회전 시 가로세로 길이 반전 (1x2 건물 등을 위함)
        if (dir == BuildDirection.Left || dir == BuildDirection.Right) {
            width = baseSize.y;
            height = baseSize.x;
        }

        for (int x = 0; x < width; x++) {
            for (int y = 0; y < height; y++) {
                cells.Add(new Vector3Int(origin.x + x, origin.y + y, 0));
            }
        }
        return cells;
    }

    // ✨ [핵심 추가 2] 여러 칸이 모두 비어있고, 바닥이 깔려있는지 검사
    public bool CanBuildArea(List<Vector3Int> cells) {
        foreach (var cell in cells) {
            if (!tilemapFloor.HasTile(cell) || IsOccupied(cell)) return false;
        }
        return true;
    }

    void Update() {
        if (isConfirming) return;

        if (selectedTile == null && !isDemolishMode) {
            Cursor.SetCursor(null, Vector2.zero, CursorMode.Auto);
            return;
        }

        if (Input.GetKeyDown(KeyCode.R) && !isDemolishMode) {
            bool isTyping = false;
            
            if (EventSystem.current != null && EventSystem.current.currentSelectedGameObject != null) {
                if (EventSystem.current.currentSelectedGameObject.GetComponent<TMP_InputField>() != null) {
                    isTyping = true; 
                }
            }

            if (!isTyping) {
                currentDirection = (BuildDirection)(((int)currentDirection + 1) % 4);
            }
        }

        Vector3 worldPos = Camera.main.ScreenToWorldPoint(Input.mousePosition);
        worldPos.z = 0;
        Vector3Int cellPos = tilemapInstallations.WorldToCell(worldPos);

        ShowPreview(cellPos);
        UpdateCursor(cellPos);

        if (Input.GetMouseButtonDown(0) && !EventSystem.current.IsPointerOverGameObject()) {
            
            if (Shared_Manager_Session.IsVisiting) return; 

            if (isDemolishMode) TryDemolishMachine(cellPos);
            // ✨ [수정됨] 선택된 타일이 있을 때(건축 모드일 때)만 건축을 시도합니다!
            else if (selectedTile != null) { 
                if (!isPlacementAllowed) {
                    Vector3 clickPos = Camera.main.ScreenToWorldPoint(Input.mousePosition);
                    clickPos.z = -5f;
                    ShowFloatingText("코드 오류!", clickPos);
                } else TryBuildMachine(cellPos);
            }
        }
        
        if (Input.GetMouseButtonDown(1) || Input.GetKeyDown(KeyCode.Escape)) {
            TryCancelBuildMode();
        }
    }

    public void TryCancelBuildMode() {
        if (sessionBuilt.Count > 0 || sessionDemolished.Count > 0) ShowConfirmUI();
        else CancelBuildMode(); 
    }

    private void ShowConfirmUI() {
        if (confirmPanel != null) {
            confirmPanel.SetActive(true);
            isConfirming = true;
            if (previewArrowInstance != null) previewArrowInstance.SetActive(false);
            tilemapPreview.ClearAllTiles();
        }
    }

    public void ClearSessionLists() {
        sessionBuilt.Clear();       
        sessionDemolished.Clear();  
        isConfirming = false;       
        if (confirmPanel != null) confirmPanel.SetActive(false); 
    }

    public void OnClick_ConfirmSave() {
        foreach (var kvp in sessionDemolished) {
            if (kvp.Value.machineInstance != null) Destroy(kvp.Value.machineInstance);
        }
        sessionBuilt.Clear();
        sessionDemolished.Clear();
        
        if (confirmPanel != null) confirmPanel.SetActive(false);
        isConfirming = false;
        CancelBuildMode(); 

        UpdateQuestMachineCounts();
    }

    public void OnClick_RollbackSave() {
        // ✨ [수정] 2x2 건물 롤백 시 4칸을 모두 삭제하도록 수정
        foreach (Vector3Int pos in sessionBuilt) {
            if (installedObjects.ContainsKey(pos)) {
                GameObject machine = installedObjects[pos];
                
                List<Vector3Int> cellsToRemove = new List<Vector3Int>();
                foreach(var kvp in installedObjects) {
                    if (kvp.Value == machine) cellsToRemove.Add(kvp.Key);
                }
                foreach(var c in cellsToRemove) {
                    installedObjects.Remove(c);
                    tilemapInstallations.SetTile(c, null);
                }

                Destroy(machine);
                installedDirections.Remove(pos);
                RefundCost(installedCosts[pos].gold, installedCosts[pos].resources);
                installedCosts.Remove(pos);
                
                if (installedArrowDict.ContainsKey(pos)) {
                    Destroy(installedArrowDict[pos]);
                    installedArrowDict.Remove(pos);
                }
            }
        }

        foreach (var kvp in sessionDemolished) {
            Vector3Int pos = kvp.Key;
            DemolishedInfo info = kvp.Value;

            if (info.machineInstance != null) info.machineInstance.SetActive(true);
            
            // ✨ [수정] 2x2 건물 복구 시 4칸 모두 딕셔너리에 복구
            foreach(var cell in info.occupiedCells) {
                installedObjects[cell] = info.machineInstance;
            }

            installedDirections[pos] = info.dir;
            installedCosts[pos] = info.originalCost; 
            tilemapInstallations.SetTile(pos, info.originalTile);
            
            PayCost(info.refundedCost.gold, info.refundedCost.resources);
        }

        sessionBuilt.Clear();
        sessionDemolished.Clear();

        if (confirmPanel != null) confirmPanel.SetActive(false);
        isConfirming = false;
        CancelBuildMode(); 
    }

    public void SelectMachine(Image buttonImage) {
        if (selectedButton == buttonImage) {
            TryCancelBuildMode();
            return;
        }

        if (codingManager != null) codingManager.CloseWindowOnly();

        logic_Demolish demolish = buttonImage.GetComponent<logic_Demolish>();
        if (demolish != null) {
            selectedDemolishLogic = demolish;
            selectedInfo = null;
            if (machineInfoUI != null) machineInfoUI.HideInfo();
            StartBuildMode(null, buttonImage);
            isPlacementAllowed = true;
            return;
        }

        Iteminfo_Base info = buttonImage.GetComponent<Iteminfo_Base>();
        if (info == null) return;
        
        selectedInfo = info;
        selectedDemolishLogic = null; 
        currentDirection = BuildDirection.Down; 
        
        if (machineInfoUI != null) machineInfoUI.ShowInfo(selectedInfo);

        if (codingManager != null) {
            logic_CodingBase prefabLogic = info.GetLogicFromPrefab();
            if (prefabLogic != null) {
                string engName = info.machinePrefab != null ? info.machinePrefab.name : info.machineName;
                int mId = Ingame_System_Save.Instance.GetMachineTypeInt(engName);
                codingManager.OpenFromExternal(mId, info.machineName, info.iconTile, buttonImage, prefabLogic);
            } else {
                StartBuildMode(null, buttonImage);
                isPlacementAllowed = true;
            }
        } else {
            StartBuildMode(null, buttonImage);
            isPlacementAllowed = true;
        }
    }

    public void StartBuildMode(TileBase tile, Image buttonImage) {
        if (selectedButton != null) selectedButton.color = normalColor;
        
        if (selectedInfo != null) {
            selectedTile = selectedInfo.iconTile;
            if (machineInfoUI != null) machineInfoUI.ShowInfo(selectedInfo);
        } else {
            selectedTile = tile; 
        }

        selectedButton = buttonImage;
        isPlacementAllowed = false;
        if (selectedButton != null) selectedButton.color = activeColor;
        ShowAllInstalledArrows();
    }

    public void SetPlacementPermission(bool isAllowed) { isPlacementAllowed = isAllowed; }
    
    public void CancelBuildMode() {
        if (selectedButton != null) selectedButton.color = normalColor;
        
        selectedTile = null;
        selectedButton = null;
        selectedInfo = null;
        selectedDemolishLogic = null;
        isPlacementAllowed = false; 
        tilemapPreview.ClearAllTiles(); 
        currentDirection = BuildDirection.Down; 
        
        if (previewArrowInstance != null) previewArrowInstance.SetActive(false);

        HideAllInstalledArrows();

        Cursor.SetCursor(null, Vector2.zero, CursorMode.Auto);
        if(codingManager != null) codingManager.CloseWindowOnly();
        if (machineInfoUI != null) machineInfoUI.HideInfo();
    }

    void ShowPreview(Vector3Int pos) {
        tilemapPreview.ClearAllTiles();
        if (Shared_Manager_Session.IsVisiting) {
            if (previewArrowInstance != null) previewArrowInstance.SetActive(false);
            return;
        }
        
        if (isDemolishMode) {
            if (previewArrowInstance != null) previewArrowInstance.SetActive(false); 
            if (IsOccupied(pos)) {
                GameObject targetObj = installedObjects.ContainsKey(pos) ? installedObjects[pos] : null;
                if (targetObj != null) {
                    // 철거 모드 시 2x2 건물의 모든 타일이 빨갛게 변하도록
                    foreach (var kvp in installedObjects) {
                        if (kvp.Value == targetObj) {
                            tilemapPreview.SetTile(kvp.Key, demolishBaseTile); 
                            tilemapPreview.SetColor(kvp.Key, new Color(1, 0, 0, 0.6f)); 
                        }
                    }
                }
            }
            return;
        }

        if (selectedInfo != null) {
            // 1. 건물이 차지할 모든 칸을 가져옵니다. (2x2라면 4칸)
            List<Vector3Int> cells = GetBuildingCells(pos, selectedInfo.buildingSize, currentDirection);
            
            // 2. 4칸 중 단 한 칸이라도 막혀있거나 타일 바깥이라면 false!
            bool canBuild = CanBuildArea(cells);
            
            // 3. 상태에 따라 전체 이미지의 색상을 초록색 또는 빨간색으로 결정
            Color previewColor = (isPlacementAllowed && canBuild) ? new Color(0, 1, 0, 0.6f) : new Color(1, 0, 0, 0.6f);

            // ✨ 4. 기존처럼 여러 칸에 그리지 않고, 기준점(pos)에 딱 하나의 이미지만 그립니다!
            tilemapPreview.SetTile(pos, selectedTile);
            tilemapPreview.SetTileFlags(pos, TileFlags.None);
            tilemapPreview.SetColor(pos, previewColor); // 이미지 하나가 통째로 붉게/초록으로 변함

            // ✨ 5. 이 하나의 이미지를 2x2 (또는 지정된 크기)로 쫙 늘리고 정중앙으로 이동시킵니다.
            int w = selectedInfo.buildingSize.x;
            int h = selectedInfo.buildingSize.y;
            
            bool isConveyor = (selectedInfo.machinePrefab != null && selectedInfo.machinePrefab.GetComponentInChildren<logic_Conveyor>() != null);
            float angle = 0f;
            
            if (currentDirection == BuildDirection.Left || currentDirection == BuildDirection.Right) {
                w = selectedInfo.buildingSize.y;
                h = selectedInfo.buildingSize.x;
            }

            if (isConveyor) {
                angle = -(int)currentDirection * 90f;
            }

            // 오프셋: 기준점(좌하단)에 있는 이미지를 2x2 영역의 정중앙으로 끌어옵니다.
            Vector3 offset = new Vector3((w - 1) / 2f, (h - 1) / 2f, 0f);
            
            // 스케일: 이미지를 2배 (건물의 크기 비율)로 잡아늘립니다.
            Vector3 scale = new Vector3(selectedInfo.buildingSize.x, selectedInfo.buildingSize.y, 1f);

            // 계산된 위치, 회전, 크기를 적용!
            Matrix4x4 matrix = Matrix4x4.TRS(offset, Quaternion.Euler(0, 0, angle), scale);
            tilemapPreview.SetTransformMatrix(pos, matrix);

            // 화살표가 있다면 화살표 위치도 건물의 정중앙으로 예쁘게 맞춰줍니다.
            if (previewArrowPrefab != null) {
                if (previewArrowInstance == null) {
                    previewArrowInstance = Instantiate(previewArrowPrefab);
                    previewArrowInstance.name = "PreviewArrow";
                    SpriteRenderer sr = previewArrowInstance.GetComponent<SpriteRenderer>();
                    if(sr != null) { Color c = sr.color; c.a = 0.8f; sr.color = c; }
                }
                previewArrowInstance.SetActive(true);
                
                Vector3 arrowPos = tilemapInstallations.GetCellCenterWorld(pos) + offset;
                arrowPos.z = -2f; 
                previewArrowInstance.transform.position = arrowPos;
                
                float arrowAngle = -(int)currentDirection * 90f; 
                previewArrowInstance.transform.rotation = Quaternion.Euler(0, 0, arrowAngle);
            }
        } else {
            if (previewArrowInstance != null) previewArrowInstance.SetActive(false);
        }
    }

    void TryBuildMachine(Vector3Int pos) {
        if (Shared_Manager_Session.IsVisiting || selectedInfo == null) return;
        
        Vector3 tileWorldPos = tilemapInstallations.GetCellCenterWorld(pos);
        tileWorldPos.z = -1f;

        // ✨ [수정] 건물의 크기만큼 공간이 비어있는지 확인!
        List<Vector3Int> cells = GetBuildingCells(pos, selectedInfo.buildingSize, currentDirection);
        if (!CanBuildArea(cells)) {
            ShowFloatingText("설치 공간이 부족합니다!", tileWorldPos);
            return;
        }

        if (sessionDemolished.ContainsKey(pos)) {
            if (sessionDemolished[pos].machineInstance != null) Destroy(sessionDemolished[pos].machineInstance);
            sessionDemolished.Remove(pos);
        }

        int goldCost = selectedInfo.buildCost;
        List<ResourceCost> resCosts = selectedInfo.requiredResources;
        
        if (Ingame_Manager_Resource.Instance != null) {
            if (!CanAfford(goldCost, resCosts)) {
                ShowFloatingText("자원이 부족합니다!", tileWorldPos);
                return;
            }

            PayCost(goldCost, resCosts);
            
            SpentCost newCost = new SpentCost { gold = goldCost };
            foreach(var r in resCosts) newCost.resources.Add(new ResourceCost { resourceType = r.resourceType, amount = r.amount });

            if (installedCosts.ContainsKey(pos)) installedCosts[pos] = newCost;
            else installedCosts.Add(pos, newCost);

            if (installedDirections.ContainsKey(pos)) installedDirections[pos] = currentDirection;
            else installedDirections.Add(pos, currentDirection);
            CreateInstalledArrow(pos, currentDirection);

            if (selectedInfo.machinePrefab != null) {
                GameObject machine = Instantiate(selectedInfo.machinePrefab, tileWorldPos, Quaternion.identity);
                machine.SendMessage("SetDirection", (int)currentDirection, SendMessageOptions.DontRequireReceiver);

                logic_Miner_Master createdMiner = machine.GetComponent<logic_Miner_Master>();
                if (createdMiner != null) createdMiner.InitializeMiner(-1); 

                logic_Productor_Master createdProductor = machine.GetComponent<logic_Productor_Master>();
                if (createdProductor != null) createdProductor.InitializeProductor(-1); 

                // ✨ [핵심] 차지하는 모든 칸을 딕셔너리에 등록 (어디를 눌러도 인식되도록)
                foreach (var cell in cells) {
                    tilemapInstallations.SetTile(cell, null); 
                    if (installedObjects.ContainsKey(cell)) installedObjects[cell] = machine;
                    else installedObjects.Add(cell, machine);
                }
                
                if (!sessionBuilt.Contains(pos)) sessionBuilt.Add(pos);
            }
        }
    }

    void TryDemolishMachine(Vector3Int clickPos) {
        if (Shared_Manager_Session.IsVisiting) return;
        if (!IsOccupied(clickPos)) return;

        GameObject targetMachine = installedObjects.ContainsKey(clickPos) ? installedObjects[clickPos] : null;
        if (targetMachine == null) return;

        // ✨ [핵심] 2x2 중 아무 칸이나 클릭해도 건물의 기준점(Origin)과 차지하던 모든 칸을 찾아냅니다.
        Vector3Int originPos = clickPos;
        List<Vector3Int> occupiedCells = new List<Vector3Int>();

        foreach (var kvp in installedObjects) {
            if (kvp.Value == targetMachine) {
                occupiedCells.Add(kvp.Key);
                // 기준점(결제/방향 데이터가 들어있는 진짜 좌표) 찾기
                if (installedCosts.ContainsKey(kvp.Key)) originPos = kvp.Key; 
            }
        }

        Vector3 tileWorldPos = tilemapInstallations.GetCellCenterWorld(originPos);
        tileWorldPos.z = -5f;

        SpentCost refund = new SpentCost();
        SpentCost original = new SpentCost();

        if (installedCosts.ContainsKey(originPos)) {
            original = installedCosts[originPos];
            refund.gold = (int)(original.gold * 0.8f); 
            foreach(var res in original.resources) {
                refund.resources.Add(new ResourceCost { resourceType = res.resourceType, amount = (int)(res.amount * 0.8f) });
            }
        }

        DemolishedInfo info = new DemolishedInfo {
            machineInstance = targetMachine,
            dir = installedDirections.ContainsKey(originPos) ? installedDirections[originPos] : BuildDirection.Down,
            originalCost = original,
            originalTile = tilemapInstallations.GetTile(originPos),
            refundedCost = refund,
            occupiedCells = occupiedCells // 롤백을 위해 저장
        };
        
        if (info.machineInstance != null) info.machineInstance.SetActive(false); 

        installedCosts.Remove(originPos);
        installedDirections.Remove(originPos);
        
        // 차지하던 모든 칸을 비워줍니다.
        foreach (var cell in occupiedCells) {
            tilemapInstallations.SetTile(cell, null);
            installedObjects.Remove(cell);
        }

        if (installedArrowDict.ContainsKey(originPos)) {
            Destroy(installedArrowDict[originPos]);
            installedArrowDict.Remove(originPos);
        }

        if (sessionBuilt.Contains(originPos)) {
            sessionBuilt.Remove(originPos); 
            if (info.machineInstance != null) Destroy(info.machineInstance); 
        } else {
            sessionDemolished.Add(originPos, info);
        }

        if (Ingame_Manager_Resource.Instance != null) {
            RefundCost(refund.gold, refund.resources); 
            ShowFloatingText("철거 완료", tileWorldPos); 
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

    public void LoadBuildingFromServer(MachineData data, GameObject prefab) {
        Vector3Int pos = new Vector3Int((int)data.pos_x, (int)data.pos_y, (int)data.pos_z);
        Vector3 worldPos = tilemapInstallations.GetCellCenterWorld(pos);
        worldPos.z = -1f;

        GameObject machine = Instantiate(prefab, worldPos, Quaternion.identity);
        BuildDirection dir = (BuildDirection)(-(int)(data.rotation_y / 90f));
        machine.SendMessage("SetDirection", (int)dir, SendMessageOptions.DontRequireReceiver);

        logic_Miner_Master createdMiner = machine.GetComponent<logic_Miner_Master>();
        if (createdMiner != null) createdMiner.InitializeMiner(-1);
        
        logic_Productor_Master createdProductor = machine.GetComponent<logic_Productor_Master>();
        if (createdProductor != null) createdProductor.InitializeProductor(-1);

        if (codingManager != null && !string.IsNullOrEmpty(data.source_code)) {
            codingManager.SetSavedCode(data.machine_type, data.source_code);
        }

        Iteminfo_Base info = prefab.GetComponent<Iteminfo_Base>();
        if (info != null) {
            SpentCost cost = new SpentCost { gold = info.buildCost };
            foreach(var r in info.requiredResources) cost.resources.Add(new ResourceCost { resourceType = r.resourceType, amount = r.amount });
            if (!installedCosts.ContainsKey(pos)) installedCosts.Add(pos, cost);
        }

        // ✨ [수정] 로드할 때도 대형 건물이면 여러 칸을 선점하도록 처리 (이름으로 추론 방어 코드)
        Vector2Int size = new Vector2Int(1, 1);
        if (prefab.name.Contains("대형") || prefab.name.Contains("도매상") || prefab.name.Contains("Large") || prefab.name.Contains("2x2")) {
            size = new Vector2Int(2, 2);
        }
        
        List<Vector3Int> cells = GetBuildingCells(pos, size, dir);
        foreach (var cell in cells) {
            if (!installedObjects.ContainsKey(cell)) installedObjects.Add(cell, machine);
        }

        if (!installedDirections.ContainsKey(pos)) installedDirections.Add(pos, dir);
        CreateInstalledArrow(pos, dir);
    }

    public void ClearAllBuildingsForLoad() {
        // ✨ [핵심 추가] 불러오기 직전, 바닥에 떨어진 모든 자원/상품을 찾아 싹 지워줍니다!
        Ingame_Item_Dropped[] droppedItems = FindObjectsOfType<Ingame_Item_Dropped>();
        foreach (var item in droppedItems) {
            if (item != null) Destroy(item.gameObject);
        }
        foreach (var obj in installedObjects.Values) if (obj != null) Destroy(obj);
        installedObjects.Clear();
        installedCosts.Clear();
        installedDirections.Clear();
        tilemapInstallations.ClearAllTiles();
        HideAllInstalledArrows();
    }
    
    public Dictionary<Vector3Int, GameObject> GetInstalledObjects() { return installedObjects; }
    
    void UpdateCursor(Vector3Int cellPos) {
        if (isDemolishMode && IsOccupied(cellPos)) Cursor.SetCursor(cursorDemolish, cursorHotspot, CursorMode.Auto);
        else Cursor.SetCursor(null, Vector2.zero, CursorMode.Auto);
    }

    void ShowAllInstalledArrows() { foreach (var kvp in installedDirections) CreateInstalledArrow(kvp.Key, kvp.Value); }

    void HideAllInstalledArrows() {
        foreach (var arrow in installedArrowDict.Values) if (arrow != null) Destroy(arrow);
        installedArrowDict.Clear();
    }

    void CreateInstalledArrow(Vector3Int pos, BuildDirection dir) {
        if (installedArrowDict.ContainsKey(pos)) return;
        if (previewArrowPrefab == null) return;

        GameObject arrow = Instantiate(previewArrowPrefab);
        arrow.name = $"InstalledArrow_{pos.x}_{pos.y}";
        Vector3 arrowPos = tilemapInstallations.GetCellCenterWorld(pos);
        arrowPos.z = -3f; 
        arrow.transform.position = arrowPos;

        float angle = -(int)dir * 90f;
        arrow.transform.rotation = Quaternion.Euler(0, 0, angle);
        
        SpriteRenderer sr = arrow.GetComponent<SpriteRenderer>();
        if (sr != null) { Color c = sr.color; c.a = 0.5f; sr.color = c; }

        installedArrowDict.Add(pos, arrow);
    }

    private Vector3Int GetDirectionVector(BuildDirection dir) {
        switch (dir) {
            case BuildDirection.Down: return new Vector3Int(0, -1, 0);
            case BuildDirection.Left: return new Vector3Int(-1, 0, 0);
            case BuildDirection.Up: return new Vector3Int(0, 1, 0);
            case BuildDirection.Right: return new Vector3Int(1, 0, 0);
            default: return new Vector3Int(0, -1, 0);
        }
    }

    public Vector3 GetDropPosition(Vector3Int machinePos) {
        BuildDirection dir = BuildDirection.Down;
        if (installedDirections.ContainsKey(machinePos)) dir = installedDirections[machinePos];

        Vector3Int dirVec = GetDirectionVector(dir);
        Vector3Int targetCell = machinePos;

        for (int i = 1; i <= 3; i++) {
            Vector3Int checkCell = machinePos + (dirVec * i);
            if (!tilemapFloor.HasTile(checkCell)) break;

            if (IsOccupied(checkCell)) {
                if (installedObjects.ContainsKey(checkCell)) {
                    GameObject obj = installedObjects[checkCell];
                    if (obj != null && (obj.name.ToLower().Contains("conveyor") || obj.CompareTag("Conveyor"))) {
                        targetCell = checkCell;
                        break; 
                    }
                }
            } else {
                targetCell = checkCell;
                break;
            }
        }

        if (targetCell == machinePos) targetCell = FindFallbackEmptyCell(machinePos);

        Vector3 dropWorldPos = tilemapInstallations.GetCellCenterWorld(targetCell);
        dropWorldPos.z = -2f; 
        return dropWorldPos;
    }

    private Vector3Int FindFallbackEmptyCell(Vector3Int center) {
        Vector3Int[] neighbors = new Vector3Int[] {
            new Vector3Int(0, -1, 0), new Vector3Int(-1, 0, 0), new Vector3Int(1, 0, 0), new Vector3Int(0, 1, 0),
            new Vector3Int(-1, -1, 0), new Vector3Int(1, -1, 0), new Vector3Int(-1, 1, 0), new Vector3Int(1, 1, 0)
        };

        foreach (var offset in neighbors) {
            Vector3Int checkCell = center + offset;
            if (tilemapFloor.HasTile(checkCell) && !IsOccupied(checkCell)) return checkCell; 
        }
        return center; 
    }

    private bool CanAfford(int gold, List<ResourceCost> resources) {
        var resMgr = Ingame_Manager_Resource.Instance;
        if (resMgr == null) return false;

        if (!resMgr.HasEnoughGold(gold)) return false; 
        foreach (var res in resources) if (!resMgr.HasEnoughResource(res.resourceType, res.amount)) return false;
        
        return true;
    }

    private void PayCost(int gold, List<ResourceCost> resources) {
        var resMgr = Ingame_Manager_Resource.Instance;
        if (resMgr == null) return;

        resMgr.SpendGold(gold);
        foreach (var res in resources) resMgr.ConsumeResource(res.resourceType, res.amount);
    }

    private void RefundCost(int gold, List<ResourceCost> resources) {
        var resMgr = Ingame_Manager_Resource.Instance;
        if (resMgr == null) return;

        resMgr.RefundGold(gold);
        foreach (var res in resources) resMgr.AddResource(res.resourceType, res.amount); 
    }

    public void UpdateQuestMachineCounts() {
        if (Ingame_Manager_Quest.Instance == null) return;

        int minerCount = 0;
        int productorCount = 0;
        int storageCount = 0;
        int marketCount = 0;

        // ✨ [핵심 수정] 2x2 건물은 딕셔너리에 4번 들어가 있으므로, 중복 계산을 방지합니다.
        Dictionary<GameObject, int> machineAreaMap = new Dictionary<GameObject, int>();

        foreach (GameObject obj in installedObjects.Values) {
            if (obj == null) continue;
            if (!machineAreaMap.ContainsKey(obj)) machineAreaMap[obj] = 0;
            machineAreaMap[obj]++; // 1칸이면 1, 4칸(2x2)이면 4가 저장됨
        }

        foreach (var kvp in machineAreaMap) {
            GameObject obj = kvp.Key;
            int area = kvp.Value; 

            // 🎯 면적이 4칸 이상인 건물은 1.5배 효율 가중치 부여! (4칸 -> 6배 효율)
            int weight = area >= 4 ? (int)(area * 1.5f) : area;

            if (obj.GetComponent<logic_Miner_Master>() != null) minerCount++;
            else if (obj.GetComponent<logic_Productor_Master>() != null) productorCount++;
            
            string objName = obj.name.ToLower();
            if (objName.Contains("storage") || objName.Contains("창고")) storageCount += weight;
            if (objName.Contains("market") || objName.Contains("도매상") || objName.Contains("판매")) marketCount += weight;
        }

        Ingame_Manager_Quest.Instance.builtMinerCount = minerCount;
        Ingame_Manager_Quest.Instance.builtProductorCount = productorCount;

        if (Ingame_Manager_Resource.Instance != null) {
            Ingame_Manager_Resource.Instance.UpdateCapacities(storageCount, marketCount);
        }
    }

    public void GenerateFloor() {
        if (floorTileBase == null) return;
        tilemapFloor.ClearAllTiles();

        int halfSize = currentMapSize / 2;
        int oddOffset = currentMapSize % 2; 

        for (int x = -halfSize; x < halfSize + oddOffset; x++) {
            for (int y = -halfSize; y < halfSize + oddOffset; y++) {
                tilemapFloor.SetTile(new Vector3Int(x, y, 0), floorTileBase);
            }
        }
    }

    public int GetCurrentExpandCost() { return baseExpandCost * (expandCount + 1); }

    public bool TryExpandMap() {
        var resMgr = Ingame_Manager_Resource.Instance;
        int cost = GetCurrentExpandCost();
        
        if (resMgr != null && resMgr.HasEnoughGold(cost)) {
            resMgr.SpendGold(cost);
            expandCount++;
            currentMapSize += expandSizeStep; 
            GenerateFloor(); 
            return true;
        }
        return false;
    }
    
    public void CenterCamera() {
        if (Camera.main != null) Camera.main.transform.position = new Vector3(0, 0, -10);
    }
}