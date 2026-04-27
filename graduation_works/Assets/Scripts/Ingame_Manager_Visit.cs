using UnityEngine;
using UnityEngine.SceneManagement;
using System.Collections.Generic;

public class Ingame_Manager_Visit : MonoBehaviour {
    public static Ingame_Manager_Visit Instance;

    [Header("추천 기능 UI")]
    public GameObject recommendButton; 
    public UI_Leaderboard leaderboardManager;
    
    [Header("관전 모드 시 숨길 UI 목록 (저장 버튼, 하단 메뉴 등)")]
    public List<GameObject> disableUIsWhenVisiting = new List<GameObject>();

    void Awake() {
        if (Instance == null) Instance = this;
    }

    void Start() {
        if (Shared_Manager_Session.IsVisiting) {
            // [방문 모드] 건설 UI 등 숨기기
            foreach (var ui in disableUIsWhenVisiting) {
                if (ui != null) ui.SetActive(false);
            }
            
            // 추천 버튼 활성화
            if (recommendButton != null && leaderboardManager != null) {
                recommendButton.SetActive(true);
                leaderboardManager.targetUserId = Shared_Manager_Session.VisitTargetId; 
                StartCoroutine(leaderboardManager.GetRecommendCount(Shared_Manager_Session.VisitTargetId)); 
            }

            if (Ingame_Manager_Build.Instance != null)
                Ingame_Manager_Build.Instance.ShowFloatingText($"{Shared_Manager_Session.VisitTargetId}님의 공장에 놀러왔습니다!", Vector3.zero);
        } else {
            // [내 공장] 추천 버튼 숨기기
            if (recommendButton != null) recommendButton.SetActive(false);
        }
    }
}