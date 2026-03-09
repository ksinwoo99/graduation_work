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
}

public class Ingame_Manager_Build : MonoBehaviour {
    public static Ingame_Manager_Build Instance;

    [Header("타일맵 연결")]
    public Tilemap tilemapFloor;
    public Tilemap tilemapInstallations;
    public Tilemap tilemapPreview;

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

    public bool IsOccupied(Vector3Int pos) {
        return tilemapInstallations.HasTile(pos) || installedObjects.ContainsKey(pos);
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
            if (isDemolishMode) TryDemolishMachine(cellPos);
            else {
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
        if (sessionBuilt.Count > 0 || sessionDemolished.Count > 0) {
            ShowConfirmUI();
        } else {
            CancelBuildMode(); 
        }
    }

    private void ShowConfirmUI() {
        if (confirmPanel != null) {
            confirmPanel.SetActive(true);
            isConfirming = true;
            if (previewArrowInstance != null) previewArrowInstance.SetActive(false);
            tilemapPreview.ClearAllTiles();
        }
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
        foreach (Vector3Int pos in sessionBuilt) {
            if (installedObjects.ContainsKey(pos)) {
                Destroy(installedObjects[pos]);
                installedObjects.Remove(pos);
                installedDirections.Remove(pos);
                tilemapInstallations.SetTile(pos, null);
                
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
            installedObjects[pos] = info.machineInstance;
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

        if (codingManager != null) {
            codingManager.CloseWindowOnly();
        }

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
                codingManager.OpenFromExternal(info.machineName, info.iconTile, buttonImage, prefabLogic);
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
            
            if (selectedTile == null) {
                Debug.LogError($"[{selectedInfo.machineName}] 버튼의 'Icon Tile'이 비어있습니다! 인스펙터 창을 확인해주세요.");
            }
            if (machineInfoUI != null) machineInfoUI.ShowInfo(selectedInfo);
        }
        else {
            selectedTile = tile; 
        }

        selectedButton = buttonImage;
        isPlacementAllowed = false;
        if (selectedButton != null) selectedButton.color = activeColor;

        ShowAllInstalledArrows();
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
        currentDirection = BuildDirection.Down; 
        
        if (previewArrowInstance != null) previewArrowInstance.SetActive(false);

        HideAllInstalledArrows();

        Cursor.SetCursor(null, Vector2.zero, CursorMode.Auto);
        if(codingManager != null) codingManager.CloseWindowOnly();
        if (machineInfoUI != null) machineInfoUI.HideInfo();
    }

    void ShowPreview(Vector3Int pos) {
        tilemapPreview.ClearAllTiles();
        
        if (isDemolishMode) {
            if (previewArrowInstance != null) previewArrowInstance.SetActive(false); 
            if (IsOccupied(pos)) {
                tilemapPreview.SetTile(pos, demolishBaseTile); 
                tilemapPreview.color = new Color(1, 0, 0, 0.6f); 
            }
            return;
        }

        if (tilemapFloor.HasTile(pos)) {
            bool canBuild = !IsOccupied(pos); 
            tilemapPreview.SetTile(pos, selectedTile);
            tilemapPreview.color = (isPlacementAllowed && canBuild) ? new Color(0, 1, 0, 0.6f) : new Color(1, 0, 0, 0.6f);

            bool isConveyor = false;
            if (selectedInfo != null && selectedInfo.machinePrefab != null) {
                if (selectedInfo.machinePrefab.GetComponentInChildren<logic_Conveyor>() != null) {
                    isConveyor = true;
                }
            }

            if (isConveyor) {
                float angle = -(int)currentDirection * 90f; 
                Matrix4x4 matrix = Matrix4x4.TRS(Vector3.zero, Quaternion.Euler(0, 0, angle), Vector3.one);
                tilemapPreview.SetTileFlags(pos, TileFlags.None); 
                tilemapPreview.SetTransformMatrix(pos, matrix);
            } else {
                tilemapPreview.SetTileFlags(pos, TileFlags.None);
                tilemapPreview.SetTransformMatrix(pos, Matrix4x4.identity);
            }

            if (previewArrowPrefab != null) {
                if (previewArrowInstance == null) {
                    previewArrowInstance = Instantiate(previewArrowPrefab);
                    previewArrowInstance.name = "PreviewArrow";
                    SpriteRenderer sr = previewArrowInstance.GetComponent<SpriteRenderer>();
                    if(sr != null) { Color c = sr.color; c.a = 0.8f; sr.color = c; }
                }
                previewArrowInstance.SetActive(true);
                Vector3 arrowPos = tilemapInstallations.GetCellCenterWorld(pos);
                arrowPos.z = -2f; 
                previewArrowInstance.transform.position = arrowPos;

                float angle = -(int)currentDirection * 90f; 
                previewArrowInstance.transform.rotation = Quaternion.Euler(0, 0, angle);
            }
        } else {
            if (previewArrowInstance != null) previewArrowInstance.SetActive(false);
        }
    }

    void TryBuildMachine(Vector3Int pos) {
        Vector3 tileWorldPos = tilemapInstallations.GetCellCenterWorld(pos);
        tileWorldPos.z = -1f;

        if (!tilemapFloor.HasTile(pos) || IsOccupied(pos)) {
            ShowFloatingText("설치 불가능!", tileWorldPos);
            return;
        }

        if (sessionDemolished.ContainsKey(pos)) {
            if (sessionDemolished[pos].machineInstance != null) Destroy(sessionDemolished[pos].machineInstance);
            sessionDemolished.Remove(pos);
        }

        int goldCost = (selectedInfo != null) ? selectedInfo.buildCost : 0;
        List<ResourceCost> resCosts = (selectedInfo != null) ? selectedInfo.requiredResources : new List<ResourceCost>();
        
        if (Ingame_Manager_Resource.Instance != null) {
            
            if (!CanAfford(goldCost, resCosts)) {
                ShowFloatingText("자원이 부족합니다!", tileWorldPos);
                return;
            }

            PayCost(goldCost, resCosts);
            
            tilemapInstallations.SetTile(pos, null); 
            
            SpentCost newCost = new SpentCost { gold = goldCost };
            foreach(var r in resCosts) {
                newCost.resources.Add(new ResourceCost { resourceType = r.resourceType, amount = r.amount });
            }

            if (installedCosts.ContainsKey(pos)) installedCosts[pos] = newCost;
            else installedCosts.Add(pos, newCost);

            if (installedDirections.ContainsKey(pos)) installedDirections[pos] = currentDirection;
            else installedDirections.Add(pos, currentDirection);
            CreateInstalledArrow(pos, currentDirection);

            if (selectedInfo != null && selectedInfo.machinePrefab != null) {
                GameObject machine = Instantiate(selectedInfo.machinePrefab, tileWorldPos, Quaternion.identity);
                machine.SendMessage("SetDirection", (int)currentDirection, SendMessageOptions.DontRequireReceiver);

                logic_Miner_Master createdMiner = machine.GetComponent<logic_Miner_Master>();
                if (createdMiner != null) createdMiner.InitializeMiner(-1); 

                logic_Productor_Master createdProductor = machine.GetComponent<logic_Productor_Master>();
                if (createdProductor != null) createdProductor.InitializeProductor(-1); 

                if (installedObjects.ContainsKey(pos)) installedObjects[pos] = machine;
                else installedObjects.Add(pos, machine);
                
                if (!sessionBuilt.Contains(pos)) sessionBuilt.Add(pos);
            }

            Debug.Log($"설치 완료 (골드/자원 소모됨)");
        }
    }

    void TryDemolishMachine(Vector3Int pos) {
        if (!IsOccupied(pos)) return;
        Vector3 tileWorldPos = tilemapInstallations.GetCellCenterWorld(pos);
        tileWorldPos.z = -5f;

        SpentCost refund = new SpentCost();
        SpentCost original = new SpentCost();

        if (installedCosts.ContainsKey(pos)) {
            original = installedCosts[pos];
            refund.gold = (int)(original.gold * 0.8f); 
            
            foreach(var res in original.resources) {
                refund.resources.Add(new ResourceCost { 
                    resourceType = res.resourceType, 
                    amount = (int)(res.amount * 0.8f) 
                });
            }
        }

        DemolishedInfo info = new DemolishedInfo {
            machineInstance = installedObjects.ContainsKey(pos) ? installedObjects[pos] : null,
            dir = installedDirections.ContainsKey(pos) ? installedDirections[pos] : BuildDirection.Down,
            originalCost = original,
            originalTile = tilemapInstallations.GetTile(pos),
            refundedCost = refund
        };
        
        if (info.machineInstance != null) info.machineInstance.SetActive(false); 

        installedCosts.Remove(pos);
        installedDirections.Remove(pos);
        tilemapInstallations.SetTile(pos, null);
        installedObjects.Remove(pos);

        if (installedArrowDict.ContainsKey(pos)) {
            Destroy(installedArrowDict[pos]);
            installedArrowDict.Remove(pos);
        }

        if (sessionBuilt.Contains(pos)) {
            sessionBuilt.Remove(pos); 
            if (info.machineInstance != null) Destroy(info.machineInstance); 
        } else {
            sessionDemolished.Add(pos, info);
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

    public void LoadBuilding(string type, Vector3Int pos, int remainingCount) { }
    public Dictionary<Vector3Int, GameObject> GetInstalledObjects() { return installedObjects; }
    
    void UpdateCursor(Vector3Int cellPos) {
        if (isDemolishMode && IsOccupied(cellPos)) Cursor.SetCursor(cursorDemolish, cursorHotspot, CursorMode.Auto);
        else Cursor.SetCursor(null, Vector2.zero, CursorMode.Auto);
    }

    void ShowAllInstalledArrows() {
        foreach (var kvp in installedDirections) CreateInstalledArrow(kvp.Key, kvp.Value);
    }

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

        foreach (var res in resources) {
            if (!resMgr.HasEnoughResource(res.resourceType, res.amount)) return false;
        }
        return true;
    }

    private void PayCost(int gold, List<ResourceCost> resources) {
        var resMgr = Ingame_Manager_Resource.Instance;
        if (resMgr == null) return;

        resMgr.SpendGold(gold);
        foreach (var res in resources) {
            resMgr.ConsumeResource(res.resourceType, res.amount);
        }
    }

    private void RefundCost(int gold, List<ResourceCost> resources) {
        var resMgr = Ingame_Manager_Resource.Instance;
        if (resMgr == null) return;

        resMgr.RefundGold(gold);
        foreach (var res in resources) {
            resMgr.AddResource(res.resourceType, res.amount); 
        }
    }

    public void UpdateQuestMachineCounts() {
        if (Ingame_Manager_Quest.Instance == null) return;

        int minerCount = 0;
        int productorCount = 0;
        int storageCount = 0;
        int marketCount = 0;

        foreach (GameObject obj in installedObjects.Values) {
            if (obj == null) continue;
            
            // 🔥 [수정] Common을 Master로 변경!
            if (obj.GetComponent<logic_Miner_Master>() != null) minerCount++;
            else if (obj.GetComponent<logic_Productor_Master>() != null) productorCount++;
            
            string objName = obj.name.ToLower();
            if (objName.Contains("storage")) storageCount++;
            if (objName.Contains("market")) marketCount++;
        }

        Ingame_Manager_Quest.Instance.builtMinerCount = minerCount;
        Ingame_Manager_Quest.Instance.builtProductorCount = productorCount;

        if (Ingame_Manager_Resource.Instance != null) {
            Ingame_Manager_Resource.Instance.UpdateCapacities(storageCount, marketCount);
        }
    }
}