using UnityEngine;
using TMPro;
using UnityEngine.UI; // Button 컴포넌트 사용을 위해 추가
using UnityEngine.Networking;
using UnityEngine.EventSystems; // 포커스 제어를 위해 추가
using System.Collections;

public class Menu_Manager_Visit : MonoBehaviour {
    [Header("연결할 UI")]
    public Menu_Manager_UI uiManager;          
    public GameObject visitPanel;              
    public TMP_InputField targetIdInput;       
    public Button btnGoVisit; // '방문하기' 버튼을 여기에 연결해주세요.
    public GameObject leaderboardPanel;

    private string serverUrl = "http://13.237.51.219:8000"; 

    void Start() {
        // 입력창의 값이 바뀔 때마다 감지하도록 리스너 등록
        if (targetIdInput != null) {
            targetIdInput.onValueChanged.AddListener(OnInputFieldValueChanged);
        }
        
        // 처음에 버튼 상태 초기화
        UpdateButtonState("");
    }

    void Update() {
        // ✨ 패널이 켜져 있을 때만 키보드 입력 체크
        if (visitPanel != null && visitPanel.activeSelf) {
            
            // 1. 엔터 키: 아이디가 입력된 상태라면 방문 시도
            if (Input.GetKeyDown(KeyCode.Return) || Input.GetKeyDown(KeyCode.KeypadEnter)) {
                if (btnGoVisit != null && btnGoVisit.interactable) {
                    OnClick_GoToVisit();
                }
            }

            // 2. ✨ [신규] ESC 키: 패널 닫기 (돌아가기)
            if (Input.GetKeyDown(KeyCode.Escape)) {
                OnClick_CloseVisitPanel();
            }
        }
    }

    public void OnClick_OpenVisitPanel() {
        if (visitPanel != null) {
            visitPanel.SetActive(true);
            
            // 패널이 열릴 때 포커스를 입력창으로 이동
            if (targetIdInput != null) {
                targetIdInput.text = "";
                targetIdInput.ActivateInputField(); 
                EventSystem.current.SetSelectedGameObject(targetIdInput.gameObject);
            }
            
            UpdateButtonState(""); 
        }
    }

    public void OnClick_CloseVisitPanel() {
        if (visitPanel != null) {
            visitPanel.SetActive(false);
            // 패널 닫을 때 포커스 해제하여 다른 버튼 오작동 방지
            EventSystem.current.SetSelectedGameObject(null);
        }
    }

    private void OnInputFieldValueChanged(string value) {
        UpdateButtonState(value);
    }

    private void UpdateButtonState(string value) {
        if (btnGoVisit != null) {
            // 텍스트 유무에 따라 버튼 활성화 제어
            btnGoVisit.interactable = !string.IsNullOrWhiteSpace(value);
        }
    }

    public void OnClick_GoToVisit() {
        string targetId = targetIdInput.text.Trim();
        
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

                if (leaderboardPanel != null) leaderboardPanel.SetActive(false);
                
                uiManager.StartGameTransition(); 
            } else {
                uiManager.ShowError("해당 유저의 저장 데이터가 없습니다.");
            }
        } else {
            uiManager.ShowError("서버 통신 오류가 발생했습니다.");
        }
    }
}