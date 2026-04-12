using UnityEngine;
using UnityEngine.Networking;
using System.Collections;
using System.Collections.Generic;
using System.Text;

[System.Serializable]
public class MachineData {
    public int machine_type;
    public int tile_index;
    public float pos_x, pos_y, pos_z;
    public float rotation_y;
    public string source_code;
}

[System.Serializable]
public class LoadResources {
    public int resource_1, resource_2, resource_3, resource_4, resource_5, total_play_time, expand_count, quest_id, tutorial_step;
}

[System.Serializable]
public class GameSaveRequest {
    public string user_id;
    public int res1, res2, res3, res4, res5, play_time, expand_count, quest_id, tutorial_step;
    public List<MachineData> machines = new List<MachineData>();
}

[System.Serializable]
public class GameLoadResponse {
    public string status;
    public string msg;
    public LoadResources resources;
    public List<MachineData> machines;
}

public class Ingame_System_Save : MonoBehaviour { 
    public static Ingame_System_Save Instance;
    public static bool isLoadRequested = false; 

    private string serverUrl = "http://13.237.51.219:8000";

    [Header("튜토리얼 UI 연결")]
    public GameObject tutorialPanel; 

    void Awake() { 
        if (Instance == null) Instance = this; 
    }

    void Start() {
        if (isLoadRequested) {
            isLoadRequested = false;
            if (tutorialPanel != null) tutorialPanel.SetActive(false);
            OnClick_Load();
        } 
        else if (Shared_Manager_Session.IsVisiting) {
            if (tutorialPanel != null) tutorialPanel.SetActive(false);
        } 
        else {
            if (tutorialPanel != null) tutorialPanel.SetActive(true);
        }
    }

    public int GetMachineTypeInt(string name) {
        if (name.Contains("Miner_Common")) return 1;
        if (name.Contains("Miner_Advanced")) return 2;
        if (name.Contains("Miner_Hightech")) return 3;
        if (name.Contains("Miner_Superior")) return 4;
        if (name.Contains("Productor_Common")) return 5;
        if (name.Contains("Productor_Advanced")) return 6;
        if (name.Contains("Productor_Hightech")) return 7;
        if (name.Contains("Productor_Superior")) return 8;
        if (name.Contains("Conveyor")) return 9;
        if (name.Contains("Storage_Large") || name.Contains("대형 창고")) return 11;
        if (name.Contains("Storage")) return 10;
        if (name.Contains("Market_Large") || name.Contains("도매상")) return 13;
        if (name.Contains("Market")) return 12;
        return 0;
    }

    public void OnClick_Save() {
        if (Ingame_Manager_Build.Instance != null && Ingame_Manager_Build.Instance.codingManager != null) {
            Ingame_Manager_Build.Instance.codingManager.SaveCurrentInput();
        }
        
        string currentId = Shared_Manager_Session.IsVisiting ? Shared_Manager_Session.VisitTargetId : Shared_Manager_Session.CurrentUserId;
        if (string.IsNullOrEmpty(currentId)) currentId = "guest";
        
        StartCoroutine(SaveToServerCoroutine(GatherAllData(currentId)));
    }

    // ✨ [완벽 수정] 기계 중복 저장 방지 로직
    private GameSaveRequest GatherAllData(string userId) {
        GameSaveRequest data = new GameSaveRequest { user_id = userId };
        
        if (Ingame_Manager_Resource.Instance != null) {
            var mgr = Ingame_Manager_Resource.Instance;
            data.res1 = mgr.resCommon; 
            data.res2 = mgr.resRare; 
            data.res3 = mgr.resSpecial;   
            data.res4 = mgr.currentGold;  
            data.res5 = mgr.resExotic; 
        }

        if (Ingame_Manager_Time.Instance != null) {
            data.play_time = (int)Ingame_Manager_Time.Instance.gameTime;
        }

        if (Ingame_Manager_Quest.Instance != null) {
            data.quest_id = Ingame_Manager_Quest.Instance.currentQuestId;
        }

        if (Ingame_UI_Tutorial.Instance != null) {
            data.tutorial_step = Ingame_UI_Tutorial.Instance.isTutorialActive ? Ingame_UI_Tutorial.Instance.currentStep : -1;
        }

        if (Ingame_Manager_Build.Instance != null) {
            var buildMgr = Ingame_Manager_Build.Instance;
            data.expand_count = buildMgr.expandCount;
            var codingMgr = buildMgr.codingManager;

            HashSet<GameObject> savedMachines = new HashSet<GameObject>();

            foreach (var kvp in buildMgr.installedDirections) {
                Vector3Int originPos = kvp.Key;
                BuildDirection dir = kvp.Value;

                if (!buildMgr.GetInstalledObjects().ContainsKey(originPos)) continue;
                GameObject machineObj = buildMgr.GetInstalledObjects()[originPos];
                if (machineObj == null) continue;

                if (savedMachines.Contains(machineObj)) continue;
                savedMachines.Add(machineObj);

                string engName = machineObj.name.Replace("(Clone)", "").Trim();
                int mId = GetMachineTypeInt(engName);

                MachineData mData = new MachineData {
                    machine_type = mId,
                    pos_x = originPos.x, 
                    pos_y = originPos.y, 
                    pos_z = originPos.z,
                    rotation_y = -(int)dir * 90f,
                    source_code = (codingMgr != null) ? codingMgr.GetSavedCode(mId) : ""
                };
                data.machines.Add(mData);
            }
        }
        return data;
    }

    IEnumerator SaveToServerCoroutine(GameSaveRequest requestData) {
        string json = JsonUtility.ToJson(requestData);
        UnityWebRequest www = new UnityWebRequest($"{serverUrl}/save/game", "POST");
        www.uploadHandler = new UploadHandlerRaw(Encoding.UTF8.GetBytes(json));
        www.downloadHandler = new DownloadHandlerBuffer();
        www.SetRequestHeader("Content-Type", "application/json");
        
        yield return www.SendWebRequest();
        
        if (www.result == UnityWebRequest.Result.Success) {
            Debug.Log("저장 성공!");

            if (Ingame_Manager_Build.Instance != null) {
                Ingame_Manager_Build.Instance.ClearSessionLists();
            }
            if (Ingame_Manager_Menu.Instance != null) {
                Ingame_Manager_Menu.Instance.isSaved = true;
            }
        } else {
            Debug.LogError("저장 실패: " + www.error);
        }
    }

    public void OnClick_Load() {
        string cid = Shared_Manager_Session.IsVisiting ? Shared_Manager_Session.VisitTargetId : Shared_Manager_Session.CurrentUserId;
        if (string.IsNullOrEmpty(cid)) cid = "guest";
        StartCoroutine(LoadFromServerCoroutine(cid));
    }

    IEnumerator LoadFromServerCoroutine(string userId) {
        UnityWebRequest www = UnityWebRequest.Get($"{serverUrl}/load/game?user_id={userId}");
        yield return www.SendWebRequest();
        
        if (www.result == UnityWebRequest.Result.Success) {
            GameLoadResponse response = JsonUtility.FromJson<GameLoadResponse>(www.downloadHandler.text);
            if (response.status == "SUCCESS") ApplyGameData(response);
        } else {
            Debug.LogError("로드 실패: " + www.error);
        }
    }
    
    // ✨ [완벽 수정] 절대 방어막이 포함된 불러오기 로직
    private void ApplyGameData(GameLoadResponse data) {
        if (data == null) return;

        if (Ingame_Manager_Build.Instance != null) {
            Ingame_Manager_Build.Instance.CancelBuildMode(); 
        }

        if (Ingame_Manager_Resource.Instance != null && data.resources != null) {
            var mgr = Ingame_Manager_Resource.Instance;
            mgr.resCommon = data.resources.resource_1; 
            mgr.resRare = data.resources.resource_2;
            mgr.resSpecial = data.resources.resource_3; 
            mgr.currentGold = data.resources.resource_4;
            mgr.resExotic = data.resources.resource_5;
        }
        
        if (Ingame_Manager_Time.Instance != null && data.resources != null) {
            Ingame_Manager_Time.Instance.gameTime = data.resources.total_play_time;
        }

        if (Ingame_Manager_Quest.Instance != null && data.resources != null) {
            Ingame_Manager_Quest.Instance.currentQuestId = data.resources.quest_id;
            Ingame_Manager_Quest.Instance.RefreshButtonStates(); 
            Ingame_Manager_Quest.Instance.SendMessage("UpdateQuestUI", SendMessageOptions.DontRequireReceiver);
        }

        if (Ingame_UI_Tutorial.Instance != null && data.resources != null) {
            int savedStep = data.resources.tutorial_step;
            if (savedStep == -1 || (savedStep == 0 && data.machines.Count > 0)) {
                Ingame_UI_Tutorial.Instance.EndTutorial();
            } else {
                if (tutorialPanel != null) tutorialPanel.SetActive(true);
                if (savedStep == 0) {
                    Ingame_UI_Tutorial.Instance.ShowSkipPrompt();
                } else {
                    Ingame_UI_Tutorial.Instance.isTutorialActive = true;
                    Ingame_UI_Tutorial.Instance.currentStep = savedStep;
                    Ingame_UI_Tutorial.Instance.PlayStep(savedStep);
                }
            }
        }

        if (Ingame_Manager_Build.Instance != null && data.resources != null) {
            var buildMgr = Ingame_Manager_Build.Instance;
            buildMgr.expandCount = data.resources.expand_count;
            buildMgr.currentMapSize = 4 + (buildMgr.expandCount * 2);
            buildMgr.GenerateFloor(); 

            if (data.machines != null) {
                buildMgr.ClearAllBuildingsForLoad();
                foreach (var mData in data.machines) {
                    Vector3Int checkPos = new Vector3Int(Mathf.RoundToInt(mData.pos_x), Mathf.RoundToInt(mData.pos_y), Mathf.RoundToInt(mData.pos_z));
                    
                    GameObject prefab = GetPrefabFromInt(mData.machine_type);
                    if (prefab == null) continue;

                    Vector2Int size = new Vector2Int(1, 1);
                    string pName = prefab.name.ToLower();
                    if (pName.Contains("대형") || pName.Contains("도매상") || pName.Contains("large") || pName.Contains("2x2")) {
                        size = new Vector2Int(2, 2);
                    }
                    
                    BuildDirection dir = (BuildDirection)(-(int)(mData.rotation_y / 90f));
                    List<Vector3Int> needCells = buildMgr.GetBuildingCells(checkPos, size, dir);

                    bool isOccupied = false;
                    foreach (var cell in needCells) {
                        if (buildMgr.GetInstalledObjects().ContainsKey(cell)) {
                            isOccupied = true;
                            break;
                        }
                    }
                    if (isOccupied) continue; 

                    buildMgr.LoadBuildingFromServer(mData, prefab);
                }
                buildMgr.UpdateQuestMachineCounts(); 
                if (buildMgr.codingManager != null) buildMgr.codingManager.SyncAllButtonNames();
            }

            if (Ingame_UI_SystemControl.Instance != null) {
                Ingame_UI_SystemControl.Instance.UpdateAllUI();
            }
        }
    }

    private GameObject GetPrefabFromInt(int type) {
        var buildMgr = Ingame_Manager_Build.Instance;
        if (buildMgr == null || buildMgr.loadablePrefabs == null) return null;
        
        if (type > 0 && type <= buildMgr.loadablePrefabs.Length) {
            return buildMgr.loadablePrefabs[type - 1];
        }
        return null;
    }
}