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
public class GameSaveRequest {
    public string user_id;
    public int res1, res2, res3, res4, res5, play_time, expand_count; 
    public List<MachineData> machines = new List<MachineData>();
}

[System.Serializable]
public class LoadResources {
    public int resource_1, resource_2, resource_3, resource_4, resource_5, total_play_time, expand_count;
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

    void Awake() { if (Instance == null) Instance = this; }

    void Start() {
        if (isLoadRequested) {
            Debug.Log("타이틀 화면에서 불러오기 요청됨! 서버에서 데이터를 가져옵니다...");
            isLoadRequested = false; 
            OnClick_Load();          
        } else {
            Debug.Log("새 게임 시작. (서버 로드 안 함)");
            if (Ingame_Manager_Time.Instance != null) 
                Ingame_Manager_Time.Instance.gameTime = 0f;
        }
    }

    public void OnClick_Save() {
        string currentId = string.IsNullOrEmpty(Shared_Manager_Session.CurrentUserId) ? "guest" : Shared_Manager_Session.CurrentUserId;
        GameSaveRequest requestData = GatherAllData(currentId);
        StartCoroutine(SaveToServerCoroutine(requestData));
    }

    private GameSaveRequest GatherAllData(string userId) {
        GameSaveRequest data = new GameSaveRequest();
        data.user_id = userId;

        if (Ingame_Manager_Time.Instance != null) data.play_time = (int)Ingame_Manager_Time.Instance.gameTime;

        if (Ingame_Manager_Resource.Instance != null) {
            var mgr = Ingame_Manager_Resource.Instance;
            data.res1 = mgr.resCommon; 
            data.res2 = mgr.resRare;      
            data.res3 = mgr.resSpecial;   
            data.res5 = mgr.resExotic;    
            data.res4 = mgr.currentGold;  
        }

        if (Ingame_Manager_Build.Instance != null) {
            data.expand_count = Ingame_Manager_Build.Instance.expandCount;
            
            // ✨ 매니저 가져오기
            var codingMgr = Ingame_Manager_Build.Instance.codingManager;
            
            foreach (var kvp in Ingame_Manager_Build.Instance.GetInstalledObjects()) {
                GameObject obj = kvp.Value;
                if (obj == null) continue;

                string rawName = obj.name.Replace("(Clone)", "").Trim();
                
                MachineData mData = new MachineData {
                    machine_type = GetMachineTypeInt(rawName),
                    tile_index = 0, 
                    pos_x = kvp.Key.x, pos_y = kvp.Key.y, pos_z = kvp.Key.z
                };
                
                if (Ingame_Manager_Build.Instance.installedDirections.ContainsKey(kvp.Key)) 
                    mData.rotation_y = -(int)Ingame_Manager_Build.Instance.installedDirections[kvp.Key] * 90f; 

                // ✨ [핵심 수정 부분] 괄호 위치 조정 완료!
                if (codingMgr != null) {
                    string codeToSave = "";
                    Iteminfo_Base info = obj.GetComponent<Iteminfo_Base>();

                    if (info != null && !string.IsNullOrEmpty(info.machineName)) {
                        codeToSave = codingMgr.GetSavedCode(info.machineName);
                    }

                    if (string.IsNullOrEmpty(codeToSave)) {
                        codeToSave = codingMgr.GetSavedCode(rawName);
                    }

                    mData.source_code = codeToSave; 
                }

                // 위 블록 바깥에서 Add를 해줘야 합니다.
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
        if (www.result == UnityWebRequest.Result.Success) Debug.Log("서버 저장 성공!");
        else Debug.LogError($"저장 실패: {www.error}");
    }

    public void OnClick_Load() {
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
            else Debug.LogError("불러오기 실패: " + response.msg);
        } else {
            Debug.LogError($"불러오기 에러: {www.error}");
        }
    }

    private void ApplyGameData(GameLoadResponse data) {
        if (Ingame_Manager_Resource.Instance != null && data.resources != null) {
            var mgr = Ingame_Manager_Resource.Instance;
            mgr.resCommon = data.resources.resource_1;
            mgr.resRare = data.resources.resource_2;       
            mgr.resSpecial = data.resources.resource_3;    
            mgr.resExotic = data.resources.resource_5;     
            mgr.currentGold = data.resources.resource_4;
            mgr.EarnGold(0); 
        }

        if (Ingame_Manager_Time.Instance != null && data.resources != null)
            Ingame_Manager_Time.Instance.gameTime = data.resources.total_play_time;

        if (Ingame_Manager_Build.Instance != null && data.resources != null) {
            var buildMgr = Ingame_Manager_Build.Instance;
            
            buildMgr.expandCount = data.resources.expand_count;
            buildMgr.currentMapSize = 4 + (buildMgr.expandCount * buildMgr.expandSizeStep); 
            buildMgr.GenerateFloor(); 
        }

        if (Ingame_Manager_Build.Instance != null && data.machines != null) {
            Ingame_Manager_Build.Instance.ClearAllBuildingsForLoad();
            foreach (var mData in data.machines) {
                GameObject prefabToInstantiate = GetPrefabFromInt(mData.machine_type);
                if (prefabToInstantiate != null) {
                    Ingame_Manager_Build.Instance.LoadBuildingFromServer(mData, prefabToInstantiate);
                }
            }
            Ingame_Manager_Build.Instance.UpdateQuestMachineCounts();
        }
        Debug.Log("맵 데이터 불러오기 완료!");
    }

    private int GetMachineTypeInt(string prefabName) {
        string lower = prefabName.ToLower();
        if (lower.Contains("miner")) return 1;
        if (lower.Contains("conveyor")) return 2;
        if (lower.Contains("productor")) return 3;
        if (lower.Contains("storage")) return 4;
        if (lower.Contains("market")) return 5;
        return 0; 
    }

    private GameObject GetPrefabFromInt(int typeId) {
        var buildMgr = Ingame_Manager_Build.Instance;
        if (buildMgr == null || buildMgr.loadablePrefabs == null) return null;

        if (typeId >= 0 && typeId < buildMgr.loadablePrefabs.Length) {
            return buildMgr.loadablePrefabs[typeId];
        }
        return null;
    }
}