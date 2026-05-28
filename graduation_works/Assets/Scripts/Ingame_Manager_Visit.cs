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
            // [방문 모드] 내 공장이 아닐 때 -> 하단 UI 끄기
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
            // ✨ [수정 1] 남의 공장에 방문했을 때(IsVisiting = true) 숨겨두었던 필수 UI(저장, 하단메뉴 등)를 다시 모두 켜줍니다!
            foreach (var ui in disableUIsWhenVisiting) {
                if (ui != null) ui.SetActive(true);
            }

            // ✨ [수정 2] 내 공장에서는 나를 추천할 수 없으니 추천 버튼을 숨깁니다. 
            // (주석은 '숨깁니다' 였는데 기존 코드가 true로 되어 있었습니다)
            if (recommendButton != null) recommendButton.SetActive(false);
            if (recommendCountTextObj != null) recommendCountTextObj.SetActive(true); // 내 추천수는 보이게 유지
            
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