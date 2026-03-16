using UnityEngine;
using UnityEngine.SceneManagement;
using System.Collections.Generic;

public class Ingame_Manager_Visit : MonoBehaviour {
    public static Ingame_Manager_Visit Instance;

    [Header("놀러가기 전용 UI")]
    public GameObject btnReturnHome; // 내 공장으로 돌아가기 버튼
    
    [Header("관전 모드 시 숨길 UI 목록 (저장 버튼, 하단 메뉴 등)")]
    public List<GameObject> disableUIsWhenVisiting = new List<GameObject>();

    void Awake() {
        if (Instance == null) Instance = this;
    }

    void Start() {
        // 씬이 시작될 때 배낭(Session)을 열어보고 놀러온 상태인지 확인합니다.
        if (Shared_Manager_Session.IsVisiting) {
            // 돌아가기 버튼 켜기 & 건설 관련 UI 모두 끄기
            if (btnReturnHome != null) btnReturnHome.SetActive(true);
            foreach (var ui in disableUIsWhenVisiting) {
                if (ui != null) ui.SetActive(false);
            }
            
            if (Ingame_Manager_Build.Instance != null)
                Ingame_Manager_Build.Instance.ShowFloatingText($"{Shared_Manager_Session.VisitTargetId}님의 공장에 놀러왔습니다!", Vector3.zero);
        } else {
            // 내 공장이면 돌아가기 버튼 숨기기
            if (btnReturnHome != null) btnReturnHome.SetActive(false);
        }
    }

    public void OnClick_ReturnHome() {
        // 놀러가기 모드 해제
        Shared_Manager_Session.IsVisiting = false;
        Shared_Manager_Session.VisitTargetId = "";
        Shared_Manager_Session.IsReadOnlyMode = false;

        // 인게임 씬을 처음부터 다시 로드해서 내 공장을 깔끔하게 띄움!
        SceneManager.LoadScene("InGame_Scene"); 
    }
}