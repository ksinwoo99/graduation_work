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

[System.Serializable]
public class UserRankingResponse {
    public string status;
    public int recommend_rank;
    public int gold_rank;
    public int my_recommend;
    public int my_gold;
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

    [Header("UI 연결 - 순위 표시")]
    public TMP_Text textRecommendRank;
    public TMP_Text textGoldRank;

    [Header("추천 결과 팝업 UI")]
    public GameObject recommendPopupPanel;
    public TMP_Text recommendPopupText;

    private Coroutine autoCloseCoroutine;

    void Start()
    {
        if (!string.IsNullOrEmpty(targetUserId))
        {
            StartCoroutine(GetRecommendCount(targetUserId));
        }
    }

    public void RefreshLeaderboard()
    {
        StartCoroutine(GetLeaderboardData()); // Top 5 가져오기
        StartCoroutine(GetUserRankings(Shared_Manager_Session.CurrentUserId)); // 내 순위 가져오기
    }

    private IEnumerator GetLeaderboardData()
    {
        UnityWebRequest www = UnityWebRequest.Get($"{serverUrl}/get_leaderboard");
        yield return www.SendWebRequest();

        if (www.result == UnityWebRequest.Result.Success)
        {
            LeaderboardResponse res = JsonUtility.FromJson<LeaderboardResponse>(www.downloadHandler.text);
            if (res.status == "SUCCESS")
            {
                // 추천 리스트 조립
                string recText = "순위\t아이디\t추천\n──────────────\n";
                for (int i = 0; i < res.top_recommends.Count; i++) {
                    recText += $"{i + 1}위\t{res.top_recommends[i].id}\t{res.top_recommends[i].recommend_count}\n";
                }
                if (textTopRecommends != null) textTopRecommends.text = recText;
                
                // 골드 리스트 조립
                string goldText = "순위\t아이디\t골드\n──────────────\n";
                for (int i = 0; i < res.top_golds.Count; i++) {
                    goldText += $"{i + 1}위\t{res.top_golds[i].id}\t{res.top_golds[i].total_gold}\n";
                }
                if (textTopGolds != null) textTopGolds.text = goldText;
            }
        }
    }

    public IEnumerator GetUserRankings(string userId)
    {
        UnityWebRequest www = UnityWebRequest.Get($"{serverUrl}/get_user_rankings?user_id={userId}");
        yield return www.SendWebRequest();

        if (www.result == UnityWebRequest.Result.Success)
        {
            var res = JsonUtility.FromJson<UserRankingResponse>(www.downloadHandler.text);
            if (res.status == "SUCCESS")
            {
                // 원하는 형식: 순위 \t 내아이디 \t 점수
                if (textRecommendRank != null) 
                    textRecommendRank.text = $"{res.recommend_rank}위\t{userId}\t{res.my_recommend}";
                
                if (textGoldRank != null) 
                    textGoldRank.text = $"{res.gold_rank}위\t{userId}\t{res.my_gold}";
            }
        }
    }

    // ──────────────────────────
    // 개별 유저 추천 로직
    // ──────────────────────────

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
                
                if (recommendPopupPanel != null && recommendPopupText != null) {
                    recommendPopupText.text = res.msg;
                    recommendPopupPanel.SetActive(true);

                    if (autoCloseCoroutine != null) StopCoroutine(autoCloseCoroutine);
                    autoCloseCoroutine = StartCoroutine(AutoCloseTimer(3f));
                }

                // ✨ 2. 성공했을 때만 숫자 새로고침
                if (res.status == "SUCCESS")
                {
                    StartCoroutine(GetRecommendCount(toUser));
                }
            }
        }
    }

    private IEnumerator AutoCloseTimer(float delay)
    {
        yield return new WaitForSecondsRealtime(delay);
        OnClick_ClosePopup(); // 3초 뒤에 닫기 함수 실행!
    }
    public void OnClick_ClosePopup()
    {
        if (recommendPopupPanel != null) {
            recommendPopupPanel.SetActive(false);
        }
        
        // 유저가 직접 닫았는데 타이머가 계속 돌아가면 안 되니까 꺼줍니다.
        if (autoCloseCoroutine != null) {
            StopCoroutine(autoCloseCoroutine);
            autoCloseCoroutine = null;
        }
    }
}