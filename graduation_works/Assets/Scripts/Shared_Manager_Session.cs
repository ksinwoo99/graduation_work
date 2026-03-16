using UnityEngine;

public static class Shared_Manager_Session {
    private static string _currentUserId;
    public static string CurrentUserId {
        get { return string.IsNullOrEmpty(_currentUserId) ? "Guest" : _currentUserId; }
        set { _currentUserId = value; }
    }

    public static bool IsReadOnlyMode = false;
    public static bool IsVisiting = false; 
    public static string VisitTargetId = "";

    private static string GetSaveKey(string userId) => $"User_{userId}_SaveData";

    public static bool HasSaveData(string targetUserId) {
        return PlayerPrefs.HasKey(GetSaveKey(targetUserId));
    }

    public static void SaveData(string jsonString) {
        if (IsReadOnlyMode) {
            Debug.LogWarning("ReadOnly Mode: Save blocked.");
            return;
        }
        string key = GetSaveKey(CurrentUserId);
        PlayerPrefs.SetString(key, jsonString);
        PlayerPrefs.Save();
        Debug.Log($"Data Saved for {CurrentUserId}");
    }

    public static Shared_Data_Save LoadData(string targetId) {
        string key = GetSaveKey(targetId);
        if (!PlayerPrefs.HasKey(key)) return null;

        string json = PlayerPrefs.GetString(key);
        return JsonUtility.FromJson<Shared_Data_Save>(json);
    }

    public static void DeleteData() {
        string key = GetSaveKey(CurrentUserId);
        PlayerPrefs.DeleteKey(key);
    }
}