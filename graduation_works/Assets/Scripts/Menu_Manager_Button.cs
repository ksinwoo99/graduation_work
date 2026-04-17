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

    public void OnClick_NewGame() {
        string userId = Shared_Manager_Session.CurrentUserId;
        
        if (Shared_Manager_Session.HasSaveData(userId)) {
            uiManager.ShowError("기존 데이터가 있습니다.");
        } else {
            Ingame_System_Save.isLoadRequested = false;
            Shared_Manager_Session.IsReadOnlyMode = false;
            uiManager.StartGameTransition();
        }
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