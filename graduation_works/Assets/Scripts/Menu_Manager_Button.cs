using UnityEngine;
using UnityEngine.Networking; 
using System.Collections;     
using UnityEngine.SceneManagement;

[System.Serializable]
public class CheckSaveResponse 
{
    public string status;
}

public class Menu_Manager_Button : MonoBehaviour {
    public Menu_Manager_UI uiManager; 

    private string serverUrl = "http://13.237.51.219:8000"; 

    public void OnClick_Logout() {
        Shared_Manager_Session.CurrentUserId = "";
        SceneManager.LoadScene("Login_Scene"); 
    }

    public void OnClick_QuitGame()
    {
        #if UNITY_EDITOR
            UnityEditor.EditorApplication.isPlaying = false;
        #else
            Application.Quit();
        #endif
    }

    private bool isPendingNewGame = false; // 새로하기 보류 상태 마커

    public void OnClick_NewGame() {
        StartCoroutine(CheckSaveAndNewGame());
    }
    
    IEnumerator CheckSaveAndNewGame() {
        string userId = Shared_Manager_Session.CurrentUserId;
        
        // 게스트면 즉시 새 게임 실행
        if (string.IsNullOrEmpty(userId) || userId == "guest") {
            ExecuteNewGame();
            yield break; 
        }

        uiManager.ShowError("데이터 확인 중..."); 

        // 서버에 저장 데이터 존재 여부 요청
        string url = $"{serverUrl}/check_save?user_id={userId}";
        UnityWebRequest www = UnityWebRequest.Get(url);
        yield return www.SendWebRequest();

        // 데이터 확인이 끝났으므로 "데이터 확인 중..." 알림창은 닫아줍니다.
        if (uiManager.errorPanel != null) uiManager.errorPanel.SetActive(false);

        if (www.result == UnityWebRequest.Result.Success) {
            CheckSaveResponse res = JsonUtility.FromJson<CheckSaveResponse>(www.downloadHandler.text);
            
            // 🔄 서버 응답이 EXIST(데이터 있음) 일 때만 예/아니요 팝업창을 띄웁니다.
            if (res.status == "EXIST") {
                isPendingNewGame = true;
                uiManager.ShowConfirm("저장 데이터가 있습니다.\n새로 시작하시겠습니까?");
            } else {
                // 데이터가 없다면 팝업 없이 즉시 시작
                ExecuteNewGame();
            }
        } else {
            // 서버 통신 장애 시 안전하게 팝업 없이 진행하거나 에러를 처리합니다.
            Debug.LogError("서버 연결 실패" + www.error);
            ExecuteNewGame();
        }
    }

    private void ExecuteNewGame() {
        Ingame_System_Save.isLoadRequested = false;
        Shared_Manager_Session.IsReadOnlyMode = false;
        uiManager.StartGameTransition();
    }

    public void OnClick_ConfirmYes() {
        if (isPendingNewGame) {
            isPendingNewGame = false;
            uiManager.HideConfirm();
            ExecuteNewGame(); 
        }
    }

    public void OnClick_ConfirmNo() {
        isPendingNewGame = false;
        uiManager.HideConfirm();
    }

    public void OnClick_Continue() {
        StartCoroutine(CheckSaveAndContinue());
    }

    IEnumerator CheckSaveAndContinue() {
        string userId = Shared_Manager_Session.CurrentUserId;
        
        if (string.IsNullOrEmpty(userId) || userId == "guest") {
            uiManager.ShowError("로그인이 필요합니다.");
            yield break; 
        }

        uiManager.ShowError("데이터 확인 중..."); 

        string url = $"{serverUrl}/check_save?user_id={userId}";
        UnityWebRequest www = UnityWebRequest.Get(url);
        yield return www.SendWebRequest();

        if (www.result == UnityWebRequest.Result.Success) {
            CheckSaveResponse res = JsonUtility.FromJson<CheckSaveResponse>(www.downloadHandler.text);
            
            if (res.status == "EXIST") {
                Ingame_System_Save.isLoadRequested = true;
                Shared_Manager_Session.IsReadOnlyMode = false;
                uiManager.StartGameTransition();
            } else {
                uiManager.ShowError("저장된 데이터가 없습니다.\n[새 게임]을 눌러주세요.");
            }
        } else {
            uiManager.ShowError("서버 통신 오류가 발생했습니다.");
        }
    }

    public void OnClick_PlayAround() {
        uiManager.ShowError("준비중입니다.");
    }
}