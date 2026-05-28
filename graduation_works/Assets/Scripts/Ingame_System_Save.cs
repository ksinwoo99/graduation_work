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
    public int resource_1, resource_2, resource_3, resource_4, resource_5, total_play_time, expand_count, quest_id, tutorial_step, conveyor_level;
}

[System.Serializable]
public class GameSaveRequest {
    public string user_id;
    public int res1, res2, res3, res4, res5, play_time, expand_count, quest_id, tutorial_step;
    public int conveyor_level;
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
    public static bool isNewGameRequested = false;

    private string serverUrl = "http://13.237.51.219:8000";

    [Header("튜토리얼 UI 연결")]
    public GameObject tutorialPanel; 

    private string lastSavedSnapshot = ""; 
    private float lastSaveTime = 0f;

    void Awake() { 
        if (Instance == null) Instance = this; 
    }

    void Start() {
        lastSaveTime = Time.time;

        if (isNewGameRequested) {
            if (tutorialPanel != null) tutorialPanel.SetActive(true);
            if (Ingame_UI_Tutorial.Instance != null) {
                Ingame_UI_Tutorial.Instance.ShowSkipPrompt(); 
            }
            
            // 기본 자원 세팅
            string cid = Shared_Manager_Session.IsVisiting ? Shared_Manager_Session.VisitTargetId : Shared_Manager_Session.CurrentUserId;
            if (string.IsNullOrEmpty(cid)) cid = "guest";
            StartCoroutine(LoadFromServerCoroutine(cid));
            return;
        }

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

    private string GetMapCodeSnapshot(GameSaveRequest data) {
        GameSaveRequest temp = data;
        temp.res1 = 0; temp.res2 = 0; temp.res3 = 0; temp.res4 = 0; temp.res5 = 0; temp.play_time = 0;
        return JsonUtility.ToJson(temp);
    }

    public int GetDirtyStatus() {
        string currentId = Shared_Manager_Session.IsVisiting ? Shared_Manager_Session.VisitTargetId : Shared_Manager_Session.CurrentUserId;
        if (string.IsNullOrEmpty(currentId)) currentId = "guest";

        GameSaveRequest currentData = GatherAllData(currentId);
        
        string currentSnapshot = GetMapCodeSnapshot(currentData);
        if (lastSavedSnapshot != currentSnapshot) return 1;

        if (Time.time - lastSaveTime >= 5f) return 2;

        return 0;
    }

    public float GetSecondsSinceLastSave() {
        return Time.time - lastSaveTime;
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

    /// <summary>
    /// 일반 [저장하기] 버튼과 동일한 전체 시퀀스.
    /// SaveCurrentInput → GatherAllData → 서버 POST → snapshot/플래그 갱신 → ClearSessionLists
    /// </summary>
    public void PerformGameSave(bool showProgressUi = true) {
        StartCoroutine(PerformGameSaveCoroutine(showProgressUi));
    }

    public IEnumerator PerformGameSaveCoroutine(bool showProgressUi = true) {
        if (Shared_Manager_Session.IsVisiting) yield break;

        if (Ingame_Manager_Build.Instance != null && Ingame_Manager_Build.Instance.codingManager != null) {
            Ingame_Manager_Build.Instance.codingManager.SaveCurrentInput();
        }

        string currentId = Shared_Manager_Session.CurrentUserId;
        if (string.IsNullOrEmpty(currentId)) currentId = "guest";

        yield return SaveToServerCoroutine(GatherAllData(currentId), showProgressUi);
    }

    public void OnClick_Save() {
        PerformGameSave(showProgressUi: true);
    }

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
            data.conveyor_level = Ingame_Manager_Quest.Instance.conveyorUpgradeLevel;
        }

        if (Ingame_UI_Tutorial.Instance != null) {
            if (Ingame_UI_Tutorial.Instance.skipPanel != null && Ingame_UI_Tutorial.Instance.skipPanel.activeSelf) {
                data.tutorial_step = 0;
            } else {
                data.tutorial_step = Ingame_UI_Tutorial.Instance.isTutorialActive ? Ingame_UI_Tutorial.Instance.currentStep : 77;
            }
        }

        if (Ingame_Manager_Build.Instance != null) {
            var buildMgr = Ingame_Manager_Build.Instance;
            data.expand_count = buildMgr.expandCount;
            var codingMgr = buildMgr.codingManager;

            HashSet<GameObject> savedMachines = new HashSet<GameObject>();
            HashSet<int> savedMachineTypes = new HashSet<int>(); 

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
                
                savedMachineTypes.Add(mId); 

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

            if (codingMgr != null && codingMgr.globalCodes != null) {
                foreach (var kvp in codingMgr.globalCodes) {
                    int mId = kvp.Key;
                    string code = kvp.Value;

                    if (savedMachineTypes.Contains(mId) || string.IsNullOrEmpty(code)) continue;

                    MachineData dummyData = new MachineData {
                        machine_type = mId,
                        pos_x = -9999f, 
                        pos_y = -9999f,
                        pos_z = -9999f,
                        rotation_y = 0f,
                        source_code = code
                    };
                    data.machines.Add(dummyData);
                }
            }
        }
        return data;
    }

    IEnumerator SaveToServerCoroutine(GameSaveRequest requestData, bool showProgressUi = true) {
        if (showProgressUi && Ingame_Manager_Menu.Instance != null) {
            Ingame_Manager_Menu.Instance.ShowInfoWindow("저장 중...", false);
        }

        // 게임이 저장될 때, 유저의 도움말 레드닷 상태를 기기에 확정 저장!
        if (Ingame_UI_Help.Instance != null) {
            Ingame_UI_Help.Instance.SaveReadStatusToDevice();
        }

        string json = JsonUtility.ToJson(requestData);
        UnityWebRequest www = new UnityWebRequest($"{serverUrl}/save/game", "POST");
        www.uploadHandler = new UploadHandlerRaw(Encoding.UTF8.GetBytes(json));
        www.downloadHandler = new DownloadHandlerBuffer();
        www.SetRequestHeader("Content-Type", "application/json");
        
        yield return www.SendWebRequest();
        
        if (www.result == UnityWebRequest.Result.Success) {
            lastSavedSnapshot = GetMapCodeSnapshot(requestData);
            lastSaveTime = Time.time;

            if (Ingame_Manager_Menu.Instance != null) {
                Ingame_Manager_Menu.Instance.isSaved = true;
                if (showProgressUi) {
                    Ingame_Manager_Menu.Instance.ShowInfoWindow("저장 완료!", true);
                }
            }

            if (Ingame_Manager_Build.Instance != null) {
                Ingame_Manager_Build.Instance.ClearSessionLists();
            }
        } else {
            Debug.LogError("저장 실패: " + www.error);
            if (showProgressUi && Ingame_Manager_Menu.Instance != null) {
                Ingame_Manager_Menu.Instance.ShowInfoWindow("저장 실패\n다시 시도해주세요.", true);
            }
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
            if (response.status == "SUCCESS") {
                ApplyGameData(response);

                string currentId = Shared_Manager_Session.IsVisiting ? Shared_Manager_Session.VisitTargetId : Shared_Manager_Session.CurrentUserId;
                if (string.IsNullOrEmpty(currentId)) currentId = "guest";
                lastSavedSnapshot = GetMapCodeSnapshot(GatherAllData(currentId));
                lastSaveTime = Time.time;
            }
        } else {
            Debug.LogError("로드 실패: " + www.error);
        }
    }
    
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

        // [핵심 예외 차단] 새로하기(NewGame) 실행 도중일 때의 처리 ──
        if (isNewGameRequested) {
            isNewGameRequested = false;

            if (Ingame_Manager_Build.Instance != null) {
                Ingame_Manager_Build.Instance.ClearAllBuildingsForLoad();
                Ingame_Manager_Build.Instance.UpdateQuestMachineCounts();
            }
            if (Ingame_Manager_Quest.Instance != null) {
                Ingame_Manager_Quest.Instance.currentQuestId = 0;
                Ingame_Manager_Quest.Instance.conveyorUpgradeLevel = 0;
                Ingame_Manager_Quest.Instance.RefreshButtonStates();
                Ingame_Manager_Quest.Instance.SendMessage("UpdateQuestUI", SendMessageOptions.DontRequireReceiver);
            }
            if (Ingame_UI_SystemControl.Instance != null) {
                Ingame_UI_SystemControl.Instance.UpdateAllUI();
            }
            return;
        }

        if (Ingame_Manager_Quest.Instance != null && data.resources != null) {
            Ingame_Manager_Quest.Instance.currentQuestId = data.resources.quest_id;
            Ingame_Manager_Quest.Instance.conveyorUpgradeLevel = data.resources.conveyor_level;
            Ingame_Manager_Quest.Instance.RefreshButtonStates(); 
            Ingame_Manager_Quest.Instance.SendMessage("UpdateQuestUI", SendMessageOptions.DontRequireReceiver);
        }

        if (!Shared_Manager_Session.IsVisiting && Ingame_UI_Tutorial.Instance != null && data.resources != null) {
            int savedStep = data.resources.tutorial_step;
            
            if (savedStep == -1 || savedStep == 77 || (savedStep == 0 && data.machines != null && data.machines.Count > 0)) {
                Ingame_UI_Tutorial.Instance.currentStep = 77;
                Ingame_UI_Tutorial.Instance.EndTutorial();
                
                if (Ingame_UI_Help.Instance != null) {
                    Ingame_UI_Help.Instance.RefreshHelpList();
                }
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

                List<MachineData> sortedMachines = new List<MachineData>(data.machines);sortedMachines.Sort((a, b) => {
                    int priorityA = (a.machine_type >= 10) ? 0 : 1;
                    int priorityB = (b.machine_type >= 10) ? 0 : 1;
                    return priorityA.CompareTo(priorityB);
                    });

                foreach (var mData in data.machines) {
                    if (mData.pos_y <= -9000f) {
                        if (buildMgr.codingManager != null && !string.IsNullOrEmpty(mData.source_code)) {
                            buildMgr.codingManager.SetSavedCode(mData.machine_type, mData.source_code);
                        }
                        continue; 
                    }

                    Vector3Int checkPos = new Vector3Int( (int)Mathf.Round(mData.pos_x), (int)Mathf.Round(mData.pos_y), (int)Mathf.Round(mData.pos_z));
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
                    if (isOccupied) {
                        // 💡 무조건 스킵하지 말고, 로그를 남겨서 어디서 겹치는지 알아야 합니다!    
                        Debug.LogError($"[위치 충돌] {mData.machine_type}번 기계가 {checkPos}에서 겹쳐서 로드 실패!");
                        continue;
                    }

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

    public void OnClick_ExportPresetToJSON() 
    {
        if (Ingame_Manager_Build.Instance != null && Ingame_Manager_Build.Instance.codingManager != null) {
            Ingame_Manager_Build.Instance.codingManager.SaveCurrentInput();
        }

        GameSaveRequest data = GatherAllData("tutorial_preset");
        string jsonText = JsonUtility.ToJson(data, true);
        GUIUtility.systemCopyBuffer = jsonText;
        Debug.Log("[프리셋 복사 완료]\n\n" + jsonText);
        
        if (Ingame_Manager_Build.Instance != null) {
            Ingame_Manager_Build.Instance.ShowFloatingText("JSON 복사 완료!", Camera.main.transform.position);
        }
    }

    public void LoadLocalPreset(TextAsset presetJsonFile) 
    {
        if (presetJsonFile == null) return;

        Vector3? openCodingPos = null;
        if (Ingame_Manager_Build.Instance != null && Ingame_Manager_Build.Instance.codingManager != null) {
            if (Ingame_Manager_Build.Instance.codingManager.codingPanel.activeSelf) {
                var target = Ingame_Manager_Build.Instance.codingManager.GetCurrentTargetLogic();
                if (target != null) {
                    openCodingPos = target.transform.position;
                }
            }
        }

        GameSaveRequest savedData = JsonUtility.FromJson<GameSaveRequest>(presetJsonFile.text);

        GameLoadResponse response = new GameLoadResponse();
        response.status = "SUCCESS";
        response.resources = new LoadResources {
            resource_1 = savedData.res1, resource_2 = savedData.res2,
            resource_3 = savedData.res3, resource_4 = savedData.res4,
            resource_5 = savedData.res5, 
            total_play_time = (Ingame_Manager_Time.Instance != null) ? (int)Ingame_Manager_Time.Instance.gameTime : savedData.play_time,
            expand_count = savedData.expand_count, quest_id = savedData.quest_id,
            tutorial_step = savedData.tutorial_step, conveyor_level = savedData.conveyor_level
        };
        response.machines = savedData.machines;

        ApplyGameData(response);

        if (openCodingPos.HasValue) {
            StartCoroutine(RestoreCodingPanelCoroutine(openCodingPos.Value));
        }
    }

    IEnumerator RestoreCodingPanelCoroutine(Vector3 savedPos) {
        yield return new WaitForSeconds(0.2f); 

        bool isTutorial = (Ingame_UI_Tutorial.Instance != null && Ingame_UI_Tutorial.Instance.isTutorialActive);

        if (isTutorial) {
            int step = Ingame_UI_Tutorial.Instance.currentStep;
            if (step <= 26 && Ingame_UI_Tutorial.Instance.btnTutorialMiner != null) {
                Ingame_UI_Tutorial.Instance.btnTutorialMiner.onClick.Invoke();
            }
            else if (step > 26 && step < 58 && Ingame_UI_Tutorial.Instance.btnTutorialProductor != null) {
                Ingame_UI_Tutorial.Instance.btnTutorialProductor.onClick.Invoke(); 
            }
            else if (step >= 58 && Ingame_UI_Tutorial.Instance.btnTutorialConveyor != null) {
                Ingame_UI_Tutorial.Instance.btnTutorialConveyor.onClick.Invoke();
            }
        }
        else if (Ingame_Manager_Build.Instance != null) {
            GameObject targetMachine = null;
            float minDistance = 100f;

            foreach (var obj in Ingame_Manager_Build.Instance.GetInstalledObjects().Values) {
                if (obj == null) continue;
                float dist = Vector3.Distance(obj.transform.position, savedPos);
                if (dist < minDistance) {
                    minDistance = dist;
                    targetMachine = obj;
                }
            }

            if (targetMachine != null && minDistance < 2f) {
                targetMachine.SendMessage("OnMouseDown", SendMessageOptions.DontRequireReceiver);
            } 
        }
    }

    IEnumerator WaitAndOpenCodingWindow(GameObject machine) {
        yield return new WaitForSeconds(0.1f);
        if (machine != null) {
            machine.SendMessage("OnMouseDown", SendMessageOptions.DontRequireReceiver);
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