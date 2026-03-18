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
    public int resource_1, resource_2, resource_3, resource_4, resource_5, total_play_time, expand_count;
}

[System.Serializable]
public class GameSaveRequest {
    public string user_id;
    public int res1, res2, res3, res4, res5, play_time, expand_count; 
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

    // ✨ [에러 해결 핵심] Menu 씬에서 Ingame 씬으로 넘어올 때 로딩 여부를 판단하는 플래그입니다.
    public static bool isLoadRequested = false; 

    private string serverUrl = "http://13.237.51.219:8000";

    void Awake() { if (Instance == null) Instance = this; }

    // ✨ [에러 해결 핵심] 씬이 시작될 때 로딩이 예약되어 있다면 실행합니다.
    void Start() {
        if (isLoadRequested) {
            isLoadRequested = false; // 플래그 리셋
            OnClick_Load();          // 실제 로딩 시작
        }
    }

    // 이름 -> 숫자 매핑 규칙
    // ✨ 인스펙터 리스트 순서(Element 0~10)와 완벽하게 일치시킨 매핑 함수
    public int GetMachineTypeInt(string name) {
        // Miner 시리즈 (ID 1 ~ 4)
        if (name.Contains("Miner_Common")) return 1;   // Element 0
        if (name.Contains("Miner_Advanced")) return 2; // Element 1
        if (name.Contains("Miner_Hightech")) return 3; // Element 2
        if (name.Contains("Miner_Superior")) return 4; // Element 3

        // Productor 시리즈 (ID 5 ~ 8)
        if (name.Contains("Productor_Common")) return 5;   // Element 4
        if (name.Contains("Productor_Advanced")) return 6; // Element 5
        if (name.Contains("Productor_Hightech")) return 7; // Element 6
        if (name.Contains("Productor_Superior")) return 8; // Element 7

        // 기타 기계들 (ID 9 ~ 11)
        if (name.Contains("Conveyor")) return 9; // Element 8
        if (name.Contains("Storage")) return 10; // Element 9
        if (name.Contains("Market")) return 11;  // Element 10

        return 0; // 알 수 없는 기계
    }

    public void OnClick_Save() {
        if (Ingame_Manager_Build.Instance != null && Ingame_Manager_Build.Instance.codingManager != null) {
            Ingame_Manager_Build.Instance.codingManager.SaveCurrentInput();
        }
        string currentId = string.IsNullOrEmpty(Shared_Manager_Session.CurrentUserId) ? "guest" : Shared_Manager_Session.CurrentUserId;
        StartCoroutine(SaveToServerCoroutine(GatherAllData(currentId)));
    }

    private GameSaveRequest GatherAllData(string userId) {
        GameSaveRequest data = new GameSaveRequest { user_id = userId };
        if (Ingame_Manager_Resource.Instance != null) {
            var mgr = Ingame_Manager_Resource.Instance;
            data.res1 = mgr.resCommon; data.res2 = mgr.resRare; data.res3 = mgr.resSpecial;   
            data.res5 = mgr.resExotic; data.res4 = mgr.currentGold;  
        }
        if (Ingame_Manager_Time.Instance != null) data.play_time = (int)Ingame_Manager_Time.Instance.gameTime;

        if (Ingame_Manager_Build.Instance != null) {
            data.expand_count = Ingame_Manager_Build.Instance.expandCount;
            var codingMgr = Ingame_Manager_Build.Instance.codingManager;
            foreach (var kvp in Ingame_Manager_Build.Instance.GetInstalledObjects()) {
                if (kvp.Value == null) continue;
                string engName = kvp.Value.name.Replace("(Clone)", "").Trim();
                int mId = GetMachineTypeInt(engName);
                MachineData mData = new MachineData {
                    machine_type = mId,
                    pos_x = kvp.Key.x, pos_y = kvp.Key.y, pos_z = kvp.Key.z,
                    rotation_y = Ingame_Manager_Build.Instance.installedDirections.ContainsKey(kvp.Key) ?
                                 -(int)Ingame_Manager_Build.Instance.installedDirections[kvp.Key] * 90f : 0f,
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
        if (www.result == UnityWebRequest.Result.Success) Debug.Log("저장 성공!");
    }

    public void OnClick_Load() {
        // 방문 모드인지 본인 모드인지 판단하여 아이디 설정
        string currentId = Shared_Manager_Session.IsVisiting ? Shared_Manager_Session.VisitTargetId : Shared_Manager_Session.CurrentUserId;
        if (string.IsNullOrEmpty(currentId)) currentId = "guest";
        StartCoroutine(LoadFromServerCoroutine(currentId));
    }

    IEnumerator LoadFromServerCoroutine(string userId) {
        UnityWebRequest www = UnityWebRequest.Get($"{serverUrl}/load/game?user_id={userId}");
        yield return www.SendWebRequest();
        if (www.result == UnityWebRequest.Result.Success) {
            GameLoadResponse response = JsonUtility.FromJson<GameLoadResponse>(www.downloadHandler.text);
            if (response.status == "SUCCESS") ApplyGameData(response);
        }
    }

    private void ApplyGameData(GameLoadResponse data) {
        if (Ingame_Manager_Resource.Instance != null && data.resources != null) {
            var mgr = Ingame_Manager_Resource.Instance;
            mgr.resCommon = data.resources.resource_1; mgr.resRare = data.resources.resource_2;
            mgr.resSpecial = data.resources.resource_3; mgr.currentGold = data.resources.resource_4;
            mgr.resExotic = data.resources.resource_5;
        }
        if (Ingame_Manager_Time.Instance != null && data.resources != null) Ingame_Manager_Time.Instance.gameTime = data.resources.total_play_time;

        if (Ingame_Manager_Build.Instance != null) {
            if (data.resources != null) Ingame_Manager_Build.Instance.expandCount = data.resources.expand_count;
            if (data.machines != null) {
                Ingame_Manager_Build.Instance.ClearAllBuildingsForLoad();
                foreach (var mData in data.machines) {
                    GameObject prefab = GetPrefabFromInt(mData.machine_type);
                    if (prefab != null) Ingame_Manager_Build.Instance.LoadBuildingFromServer(mData, prefab);
                }
            }
        }
    }

    private GameObject GetPrefabFromInt(int type) {
        var buildMgr = Ingame_Manager_Build.Instance;
        if (type > 0 && type <= buildMgr.loadablePrefabs.Length) return buildMgr.loadablePrefabs[type - 1];
        return null;
    }
}