using UnityEngine;
using TMPro;
using UnityEngine.UI; // ✨ Button 컴포넌트 사용을 위해 추가
using UnityEngine.Networking;
using System.Collections;

public class Menu_Manager_Visit : MonoBehaviour {
    [Header("연결할 UI")]
    public Menu_Manager_UI uiManager;          
    public GameObject visitPanel;              
    public TMP_InputField targetIdInput;       
    public Button btnGoVisit; // ✨ [신규] '방문하기' 버튼을 여기에 연결해주세요.

    private string serverUrl = "http://13.237.51.219:8000"; 

    void Start() {
        // ✨ 입력창의 값이 바뀔 때마다 감지하도록 리스너 등록
        if (targetIdInput != null) {
            targetIdInput.onValueChanged.AddListener(OnInputFieldValueChanged);
        }
        
        // 처음에 버튼 상태 초기화
        UpdateButtonState("");
    }

    public void OnClick_OpenVisitPanel() {
        if (visitPanel != null) visitPanel.SetActive(true);
        if (targetIdInput != null) {
            targetIdInput.text = "";
            UpdateButtonState(""); // 패널 열 때 버튼 상태 다시 체크
        }
    }

    public void OnClick_CloseVisitPanel() {
        if (visitPanel != null) visitPanel.SetActive(false);
    }

    // ✨ [신규] 입력창 텍스트가 바뀔 때 실행될 함수
    private void OnInputFieldValueChanged(string value) {
        UpdateButtonState(value);
    }

    // ✨ [신규] 텍스트 유무에 따라 버튼의 클릭 가능 여부를 결정하는 함수
    private void UpdateButtonState(string value) {
        if (btnGoVisit != null) {
            // 텍스트를 앞뒤 공백 제거(Trim)했을 때 비어있지 않아야 버튼 활성화
            btnGoVisit.interactable = !string.IsNullOrWhiteSpace(value);
        }
    }

    public void OnClick_GoToVisit() {
        string targetId = targetIdInput.text.Trim();
        
        // 💡 이제 버튼 자체가 안 눌리므로 여기서 string.IsNullOrEmpty 체크와 에러창은 필요 없습니다.
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
                Shared_Manager_Session.IsVisiting = true;
                Shared_Manager_Session.VisitTargetId = targetId;
                Shared_Manager_Session.IsReadOnlyMode = true; 
                
                Ingame_System_Save.isLoadRequested = true; 
                
                if (visitPanel != null) visitPanel.SetActive(false);
                uiManager.StartGameTransition(); 
            } else {
                uiManager.ShowError("해당 유저의 저장 데이터가 없습니다.");
            }
        } else {
            uiManager.ShowError("서버 통신 오류가 발생했습니다.");
        }
    }
}