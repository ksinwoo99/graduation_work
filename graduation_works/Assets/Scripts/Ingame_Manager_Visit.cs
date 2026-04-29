using UnityEngine;
using System.Collections.Generic;

public class Ingame_Manager_Visit : MonoBehaviour {
    public static Ingame_Manager_Visit Instance;

    [Header("추천 기능 UI")]
    public GameObject recommendButton; 
    public GameObject recommendCountTextObj; // ✨ [추가] 추천수 텍스트 오브젝트
    public UI_Leaderboard leaderboardManager; 

    [Header("관전 모드 시 숨길 UI 목록 (저장 버튼, 하단 메뉴 등)")]
    public List<GameObject> disableUIsWhenVisiting = new List<GameObject>();

    void Awake() {
        if (Instance == null) Instance = this;
    }

    void Start() {
        if (Shared_Manager_Session.IsVisiting) {
            foreach (var ui in disableUIsWhenVisiting) {
                if (ui != null) ui.SetActive(false);
            }
            
            if (recommendButton != null) recommendButton.SetActive(true);
            if (recommendCountTextObj != null) recommendCountTextObj.SetActive(true);

            if (leaderboardManager != null) {
                leaderboardManager.targetUserId = Shared_Manager_Session.VisitTargetId; 
                StartCoroutine(leaderboardManager.GetRecommendCount(Shared_Manager_Session.VisitTargetId)); 
                StartCoroutine(leaderboardManager.GetUserRankings(Shared_Manager_Session.VisitTargetId));
            }
        } else {
            // 추천하기 버튼만 숨깁니다.
            if (recommendButton != null) recommendButton.SetActive(true);
            
            // 내 아이디의 추천 수를 불러옵니다.
            if (leaderboardManager != null) {
                string myId = Shared_Manager_Session.CurrentUserId;
                leaderboardManager.targetUserId = myId;
                StartCoroutine(leaderboardManager.GetRecommendCount(myId));
                StartCoroutine(leaderboardManager.GetUserRankings(myId));
            }
        }
    }
}