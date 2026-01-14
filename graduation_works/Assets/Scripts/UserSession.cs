using UnityEngine;

public static class UserSession
{
    // ================= 로그인 세션 =================
    private static string _userId;

    public static string UserId
    {
        get
        {
            if (string.IsNullOrEmpty(_userId))
                _userId = "tester";
            return _userId;
        }
        set
        {
            _userId = value;
        }
    }

    // ================= 플레이 데이터 =================
    public static PlayerSaveData CurrentSaveData;

    private static string SaveKey => $"SAVE_{UserId}";

    // 저장 데이터 존재 여부
    public static bool HasSaveData()
    {
        return PlayerPrefs.HasKey(SaveKey);
    }

    // 로컬 저장
    public static void SaveLocal(PlayerSaveData data)
    {
        CurrentSaveData = data;
        string json = JsonUtility.ToJson(data);
        PlayerPrefs.SetString(SaveKey, json);
        PlayerPrefs.Save();
    }

    // 로컬 불러오기
    public static PlayerSaveData LoadLocal()
    {
        if (!HasSaveData())
            return null;

        string json = PlayerPrefs.GetString(SaveKey);
        CurrentSaveData = JsonUtility.FromJson<PlayerSaveData>(json);
        return CurrentSaveData;
    }

    // 새로하기용 삭제
    public static void DeleteSaveData()
    {
        PlayerPrefs.DeleteKey(SaveKey);
        CurrentSaveData = null;
    }
}

// ================= 세이브 데이터 구조 =================
[System.Serializable]
public class PlayerSaveData
{
    public int level;
    public Vector2 position;
    public int gold;
}