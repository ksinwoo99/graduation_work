using UnityEngine;
using System.Collections.Generic;

public class Ingame_System_Save : MonoBehaviour { // 🔥 이름 변경 완료
    public static Ingame_System_Save Instance;
    public static Shared_Data_Save TempLoadData = null; 

    void Awake() {
        if (Instance == null) Instance = this;
    }

    void Start() {
        if (TempLoadData != null) {
            Debug.Log("Loading Data...");
            ApplyGameData(TempLoadData);
            TempLoadData = null;
        } else {
            Debug.Log("New Game Started.");
            // 이름 변경 규칙 적용: Ingame_Manager_Time
            if (Ingame_Manager_Time.Instance != null) 
                Ingame_Manager_Time.Instance.gameTime = 0f;
        }
    }

    public void OnClick_Save() {
        if (Shared_Manager_Session.IsReadOnlyMode) return;
        
        Shared_Data_Save data = GatherAllData();
        string json = JsonUtility.ToJson(data, true);
        Shared_Manager_Session.SaveData(json);
    }

    private Shared_Data_Save GatherAllData() {
        Shared_Data_Save data = new Shared_Data_Save();

        if (Ingame_Manager_Time.Instance != null)
            data.totalPlayTime = Ingame_Manager_Time.Instance.gameTime;

        if (Ingame_Manager_Resource.Instance != null) {
            var mgr = Ingame_Manager_Resource.Instance;
            data.gold = mgr.currentGold;
            data.resCommon = mgr.resCommon;
            data.resUncommon = mgr.resUncommon;
            data.resRare = mgr.resRare;
        }

        // 이름 변경 규칙 적용: Ingame_Manager_Build
        if (Ingame_Manager_Build.Instance != null) {
            foreach (var kvp in Ingame_Manager_Build.Instance.GetInstalledObjects()) {
                GameObject obj = kvp.Value;
                if (obj == null) continue;

                // 🔥 [수정] logic_Miner -> logic_Miner_Common
                logic_Miner_Common miner = obj.GetComponent<logic_Miner_Common>();
                if (miner != null) {
                    BuildingSaveData b = new BuildingSaveData();
                    b.prefabName = obj.name.Replace("(Clone)", "").Trim();
                    b.position = kvp.Key;
                    b.remainingCount = miner.miningCount;
                    data.buildings.Add(b);
                }
            }
        }
        return data;
    }

    private void ApplyGameData(Shared_Data_Save data) {
        if (Ingame_Manager_Time.Instance != null)
            Ingame_Manager_Time.Instance.gameTime = data.totalPlayTime;

        if (Ingame_Manager_Resource.Instance != null) {
            var mgr = Ingame_Manager_Resource.Instance;
            mgr.currentGold = data.gold;
            mgr.resCommon = data.resCommon;
            mgr.resUncommon = data.resUncommon;
            mgr.resRare = data.resRare;
            mgr.EarnGold(0); // UI Refresh
        }

        if (Ingame_Manager_Build.Instance != null) {
            foreach (var b in data.buildings) {
                Ingame_Manager_Build.Instance.LoadBuilding(b.prefabName, b.position, b.remainingCount);
            }
        }
    }
}