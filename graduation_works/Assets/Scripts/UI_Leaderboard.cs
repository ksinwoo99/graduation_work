using UnityEngine;
using UnityEngine.Networking;
using System.Collections;
using TMPro;

public class UI_Leaderboard : MonoBehaviour
{
    private string serverUrl = "http://13.237.51.219:8000"; // 서버 주소 맞춤 설정

    [Header("UI 연결")]
    public TMP_Text myRecommendCountText; // 내 공장 또는 방문한 공장의 추천수 텍스트
    public string targetUserId; // 현재 보고 있는 공장의 주인이름

    void Start()
    {
        // 씬 시작 시 대상의 추천수를 불러옵니다.
        // 인게임이면 내 아이디, 놀러가기면 상대 아이디를 targetUserId에 넣으세요.
        if (!string.IsNullOrEmpty(targetUserId))
        {
            StartCoroutine(GetRecommendCount(targetUserId));
        }
    }

    // 1. 추천수 가져오기
    public IEnumerator GetRecommendCount(string userId)
    {
        UnityWebRequest www = UnityWebRequest.Get($"{serverUrl}/get_recommend_count?user_id={userId}");
        yield return www.SendWebRequest();

        if (www.result == UnityWebRequest.Result.Success)
        {
            // JsonUtility 파싱 로직 (클래스 정의 필요)
            // 성공 시 myRecommendCountText.text = $"추천받은 횟수: {count}";
        }
    }

    // 2. 놀러가기 화면에서 '추천하기' 버튼 클릭 시 호출
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
                // 응답 확인 후 텍스트 즉시 갱신 또는 "이미 추천했습니다" 알림 띄우기
                Debug.Log("추천 응답: " + www.downloadHandler.text);
            }
        }
    }
}