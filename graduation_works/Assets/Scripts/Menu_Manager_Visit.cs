using UnityEngine;
using TMPro;
using UnityEngine.Networking;
using System.Collections;

public class Menu_Manager_Visit : MonoBehaviour {
    [Header("연결할 UI")]
    public Menu_Manager_UI uiManager;          // 에러 띄우고 씬 전환할 기존 UI 매니저
    public GameObject visitPanel;              // 놀러가기 팝업창 
    public TMP_InputField targetIdInput;       // 아이디 적는 칸

    private string serverUrl = "http://13.237.51.219:8000"; // 본인 서버 IP로 변경!

    public void OnClick_OpenVisitPanel() {
        if (visitPanel != null) visitPanel.SetActive(true);
        if (targetIdInput != null) targetIdInput.text = "";
    }

    public void OnClick_CloseVisitPanel() {
        if (visitPanel != null) visitPanel.SetActive(false);
    }

    public void OnClick_GoToVisit() {
        string targetId = targetIdInput.text.Trim();
        if (string.IsNullOrEmpty(targetId)) return;

        if (targetId == Shared_Manager_Session.CurrentUserId) {
            uiManager.ShowError("자신의 공장입니다.");
            return;
        }

        StartCoroutine(CheckAndVisit(targetId));
    }

    IEnumerator CheckAndVisit(string targetId) {
        uiManager.ShowError("데이터 확인 중...");
        
        string url = $"{serverUrl}/check_save?user_id={targetId}";
        UnityWebRequest www = UnityWebRequest.Get(url);
        yield return www.SendWebRequest();

        if (www.result == UnityWebRequest.Result.Success) {
            CheckSaveResponse res = JsonUtility.FromJson<CheckSaveResponse>(www.downloadHandler.text);
            
            if (res.status == "EXIST") {
                // ✨ 데이터가 있으면 배낭에 정보 챙겨서 인게임 씬으로 넘어갑니다!
                Shared_Manager_Session.IsVisiting = true;
                Shared_Manager_Session.VisitTargetId = targetId;
                Shared_Manager_Session.IsReadOnlyMode = true; // 읽기 전용 모드 ON
                
                Ingame_System_Save.isLoadRequested = true; // 씬 열리면 로드해라!
                
                if (visitPanel != null) visitPanel.SetActive(false);
                uiManager.StartGameTransition(); // 씬 전환 애니메이션 실행
            } else {
                uiManager.ShowError("해당 유저의 저장 데이터가 없습니다.");
            }
        } else {
            uiManager.ShowError("서버 통신 오류가 발생했습니다.");
        }
    }
}