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

    // ✨ [신규 추가] 꺼져 있는 튜토리얼 패널을 켤 수 있도록 연결할 변수입니다.
    [Header("튜토리얼 UI 연결")]
    public GameObject tutorialPanel; 

    void Awake() { 
        if (Instance == null) Instance = this; 
    }

    void Start() {
        // ✨ [핵심 로직] 게임 시작 시, 상태를 보고 튜토리얼 패널을 조작합니다.
        if (isLoadRequested) {
            // 1. 불러오기로 들어왔다면 튜토리얼을 끈 상태로 유지합니다.
            isLoadRequested = false;
            if (tutorialPanel != null) tutorialPanel.SetActive(false);
            OnClick_Load();
        } 
        else if (Shared_Manager_Session.IsVisiting) {
            // 2. 남의 공장에 놀러왔다면 튜토리얼을 끕니다.
            if (tutorialPanel != null) tutorialPanel.SetActive(false);
        } 
        else {
            // 3. 둘 다 아니라면 새 게임(혹은 에디터 바로 실행)이므로 튜토리얼을 강제로 켭니다!
            if (tutorialPanel != null) tutorialPanel.SetActive(true);
        }
    }

    // 인스펙터 리스트 순서와 일치하는 매핑 함수
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
        // 대형 추가 / 긴 이름(Large)을 무조건 먼저 검사해야 혼동하지 않습니다!
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

    private GameSaveRequest GatherAllData(string userId) {
        GameSaveRequest data = new GameSaveRequest { user_id = userId };
        
        // 1. 자원 데이터
        if (Ingame_Manager_Resource.Instance != null) {
            var mgr = Ingame_Manager_Resource.Instance;
            data.res1 = mgr.resCommon; 
            data.res2 = mgr.resRare; 
            data.res3 = mgr.resSpecial;   
            data.res5 = mgr.resExotic; 
            data.res4 = mgr.currentGold;  
        }

        // 2. 플레이 시간
        if (Ingame_Manager_Time.Instance != null) {
            data.play_time = (int)Ingame_Manager_Time.Instance.gameTime;
        }

        if (Ingame_Manager_Quest.Instance != null) {
            data.quest_id = Ingame_Manager_Quest.Instance.currentQuestId;
        }

        if (Ingame_UI_Tutorial.Instance != null) {
            data.tutorial_step = Ingame_UI_Tutorial.Instance.isTutorialActive ? Ingame_UI_Tutorial.Instance.currentStep : -1;
        }

        // 3. 빌드 및 맵 확장 데이터
        if (Ingame_Manager_Build.Instance != null) {
            var buildMgr = Ingame_Manager_Build.Instance;
            data.expand_count = buildMgr.expandCount;
            var codingMgr = buildMgr.codingManager;

            // ✨ [핵심 수정] 4칸 중복 저장을 막기 위해, '기준점'이 담긴 installedDirections만 순회합니다!
            foreach (var kvp in buildMgr.installedDirections) {
                Vector3Int originPos = kvp.Key;
                BuildDirection dir = kvp.Value;

                // 기준점에 기계가 실제로 존재하는지 확인
                if (!buildMgr.GetInstalledObjects().ContainsKey(originPos)) continue;
                GameObject machineObj = buildMgr.GetInstalledObjects()[originPos];
                if (machineObj == null) continue;

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
    
    private void ApplyGameData(GameLoadResponse data) {
    if (data == null) return;

    // 🔥 [추가] 건축 모드 강제 종료 (이게 없으면 기계가 멈춘 채로 로드됩니다)
    if (Ingame_Manager_Build.Instance != null) {
        Ingame_Manager_Build.Instance.CancelBuildMode(); 
    }

    // 1. 자원 및 시간 복구 (기존 로직 동일)
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

    // 2. 퀘스트 및 레벨 데이터 선행 복구
    // 🚀 [중요] 기계를 생성하기 전에 호출해야 기계들이 생성 즉시 반복문 권한을 확인합니다.
    if (Ingame_Manager_Quest.Instance != null && data.resources != null) {
        Ingame_Manager_Quest.Instance.currentQuestId = data.resources.quest_id;
        Ingame_Manager_Quest.Instance.RefreshButtonStates(); // 여기서 반복문 레벨 복구 완료!
        Ingame_Manager_Quest.Instance.SendMessage("UpdateQuestUI", SendMessageOptions.DontRequireReceiver);
    }

    // 3. 튜토리얼 데이터 복구
    if (Ingame_UI_Tutorial.Instance != null && data.resources != null) {
        int savedStep = data.resources.tutorial_step;
        
        if (savedStep == -1 || (savedStep == 0 && data.machines.Count > 0)) {
            // 이미 스킵했거나(-1), 옛날 세이브 파일이라 기계가 있는데 튜토리얼이 0인 경우
            Ingame_UI_Tutorial.Instance.EndTutorial();
        } else {
            // ✨ [핵심 수정 1] 로딩 중 강제로 꺼졌던 튜토리얼 전체 패널을 다시 켜줍니다!
            if (tutorialPanel != null) tutorialPanel.SetActive(true);

            if (savedStep == 0) {
                // ✨ [핵심 수정 2] 스텝이 0이라면 묻지도 않고 시작하지 말고, 스킵 팝업을 띄워줍니다.
                Ingame_UI_Tutorial.Instance.ShowSkipPrompt();
            } else {
                // 진행 중이던 스텝(1 이상)이 있다면 그대로 이어서 진행합니다.
                Ingame_UI_Tutorial.Instance.isTutorialActive = true;
                Ingame_UI_Tutorial.Instance.currentStep = savedStep;
                Ingame_UI_Tutorial.Instance.PlayStep(savedStep);
            }
        }
    }

    // 4. 맵 확장 및 기계 복구 (엔진 가동)
    if (Ingame_Manager_Build.Instance != null && data.resources != null) {
        var buildMgr = Ingame_Manager_Build.Instance;
        buildMgr.expandCount = data.resources.expand_count;
        buildMgr.currentMapSize = 4 + (buildMgr.expandCount * 2);
        buildMgr.GenerateFloor(); 

        if (data.machines != null) {
            buildMgr.ClearAllBuildingsForLoad();
            foreach (var mData in data.machines) {
                // ✨ [핵심 추가: 유령 건물 방어막] 
                // 소환할 위치에 이미 누군가 자리를 잡고 있다면(중복 데이터라면) 과감히 스킵!
                Vector3Int checkPos = new Vector3Int(Mathf.RoundToInt(mData.pos_x), Mathf.RoundToInt(mData.pos_y), Mathf.RoundToInt(mData.pos_z));
                if (buildMgr.GetInstalledObjects().ContainsKey(checkPos)) {
                    continue; 
                }

                GameObject prefab = GetPrefabFromInt(mData.machine_type);
                if (prefab != null) {
                    buildMgr.LoadBuildingFromServer(mData, prefab);
                }
            }
            buildMgr.UpdateQuestMachineCounts(); 
            if (buildMgr.codingManager != null) buildMgr.codingManager.SyncAllButtonNames();
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