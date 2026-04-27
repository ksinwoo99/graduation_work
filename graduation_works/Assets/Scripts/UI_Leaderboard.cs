using UnityEngine;
using UnityEngine.Networking;
using System.Collections;
using System.Collections.Generic;
using TMPro;

// ✨ [추가] 서버에서 보내주는 JSON 배열을 담을 그릇
[System.Serializable]
public class LeaderboardUserData {
    public string id;
    public int recommend_count;
    public int total_gold;
}

[System.Serializable]
public class LeaderboardResponse {
    public string status;
    public List<LeaderboardUserData> top_recommends;
    public List<LeaderboardUserData> top_golds;
}

[System.Serializable]
public class RecommendResultResponse {
    public string status;
    public string msg;
    public int new_count;
}

public class UI_Leaderboard : MonoBehaviour
{
    private string serverUrl = "http://13.237.51.219:8000";

    [Header("UI 연결 - 리더보드 (Menu Scene)")]
    public TMP_Text textTopRecommends; // 추천수 1~5등 텍스트를 보여줄 곳
    public TMP_Text textTopGolds;      // 골드 1~5등 텍스트를 보여줄 곳

    [Header("UI 연결 - 개별 추천 (Visit/InGame Scene)")]
    public TMP_Text myRecommendCountText;
    public string targetUserId;

    void Start()
    {
        if (!string.IsNullOrEmpty(targetUserId))
        {
            StartCoroutine(GetRecommendCount(targetUserId));
        }
    }

    // ✨ Menu_Manager_UI에서 팝업이 뜰 때 호출하는 함수
    public void RefreshLeaderboard()
    {
        StartCoroutine(GetLeaderboardData());
    }

    // ✨ 서버에서 Top 5 데이터를 가져와서 텍스트에 넣는 로직
    private IEnumerator GetLeaderboardData()
    {
        UnityWebRequest www = UnityWebRequest.Get($"{serverUrl}/get_leaderboard");
        yield return www.SendWebRequest();

        if (www.result == UnityWebRequest.Result.Success)
        {
            LeaderboardResponse res = JsonUtility.FromJson<LeaderboardResponse>(www.downloadHandler.text);
            
            if (res.status == "SUCCESS")
            {
                // 추천수 텍스트 조립 (예: 1. user123 (50회))
                string recText = "";
                for (int i = 0; i < res.top_recommends.Count; i++) {
                    recText += $"{i + 1}. {res.top_recommends[i].id} ({res.top_recommends[i].recommend_count}회)\n";
                }
                if (textTopRecommends != null) textTopRecommends.text = recText;

                // 골드 텍스트 조립 (예: 1. user456 (1500G))
                string goldText = "";
                for (int i = 0; i < res.top_golds.Count; i++) {
                    goldText += $"{i + 1}. {res.top_golds[i].id} ({res.top_golds[i].total_gold}G)\n";
                }
                if (textTopGolds != null) textTopGolds.text = goldText;
            }
        }
    }

    // ----------------------------------------------------
    // 개별 유저 추천 로직
    // ----------------------------------------------------

    public IEnumerator GetRecommendCount(string userId)
    {
        UnityWebRequest www = UnityWebRequest.Get($"{serverUrl}/get_recommend_count?user_id={userId}");
        yield return www.SendWebRequest();

        if (www.result == UnityWebRequest.Result.Success)
        {
            // 간단 파싱을 위해 임시 클래스 사용
            var res = JsonUtility.FromJson<LeaderboardUserData>(www.downloadHandler.text);
            if (myRecommendCountText != null) myRecommendCountText.text = $"추천 수: {res.recommend_count}";
        }
    }

    public void OnClick_RecommendButton()
    {
        StartCoroutine(SendRecommend(Shared_Manager_Session.CurrentUserId, targetUserId));
    }

    private IEnumerator SendRecommend(string fromUser, string toUser)
    {
        string jsonData = $"{{\"from_user_id\":\"{fromUser}\", \"to_user_id\":\"{toUser}\"}}";
        
        using (UnityWebRequest www = new UnityWebRequest($"{serverUrl}/recommend_user", "POST"))
        {
            byte[] bodyRaw = System.Text.Encoding.UTF8.GetBytes(jsonData);
            www.uploadHandler = new UploadHandlerRaw(bodyRaw);
            www.downloadHandler = new DownloadHandlerBuffer();
            www.SetRequestHeader("Content-Type", "application/json");

            yield return www.SendWebRequest();

            if (www.result == UnityWebRequest.Result.Success)
            {
                RecommendResultResponse res = JsonUtility.FromJson<RecommendResultResponse>(www.downloadHandler.text);
                if (Ingame_Manager_Build.Instance != null) {
                    Ingame_Manager_Build.Instance.ShowFloatingText(res.msg, Vector3.zero);
                }

                if (res.status == "SUCCESS")
                {
                    StartCoroutine(GetRecommendCount(toUser));
                }
            }
        }
    }
}