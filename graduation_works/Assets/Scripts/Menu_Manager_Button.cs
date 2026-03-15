using UnityEngine;
using UnityEngine.Networking; // 🔥 서버 통신을 위해 추가
using System.Collections;     // 🔥 코루틴을 위해 추가

// 서버에서 O/X 대답을 받을 그릇
[System.Serializable]
public class CheckSaveResponse 
{
    public string status;
}

public class Menu_Manager_Button : MonoBehaviour {
    public Menu_Manager_UI uiManager; 

    private string serverUrl = "http://13.237.51.219:8000"; 

    public void OnClick_NewGame() {
        string userId = Shared_Manager_Session.CurrentUserId;
        
        if (Shared_Manager_Session.HasSaveData(userId)) {
            uiManager.ShowError("기존 데이터가 있습니다.");
        } else {
            // 새 게임은 서버 로드 신호를 끄고 시작
            Ingame_System_Save.isLoadRequested = false;
            Shared_Manager_Session.IsReadOnlyMode = false;
            uiManager.StartGameTransition();
        }
    }

    // ==========================================
    // ✨ 이어하기 버튼 업그레이드 (서버 확인 후 진행)
    // ==========================================
    public void OnClick_Continue() {
        // 바로 씬을 넘기지 않고 서버에 검사 요청 시작!
        StartCoroutine(CheckSaveAndContinue());
    }

    IEnumerator CheckSaveAndContinue() {
        string userId = Shared_Manager_Session.CurrentUserId;
        
        if (string.IsNullOrEmpty(userId) || userId == "guest") {
            uiManager.ShowError("로그인이 필요합니다.");
            yield break; // 함수 종료
        }

        uiManager.ShowError("데이터 확인 중..."); // 확인 중일 때 살짝 안내 띄우기

        // 1. 서버에 데이터 있는지 물어보기
        string url = $"{serverUrl}/check_save?user_id={userId}";
        UnityWebRequest www = UnityWebRequest.Get(url);
        yield return www.SendWebRequest();

        // 2. 대답 확인하기
        if (www.result == UnityWebRequest.Result.Success) {
            CheckSaveResponse res = JsonUtility.FromJson<CheckSaveResponse>(www.downloadHandler.text);
            
            if (res.status == "EXIST") {
                // 데이터가 있으면 원래대로 씬 전환!
                Ingame_System_Save.isLoadRequested = true;
                Shared_Manager_Session.IsReadOnlyMode = false;
                uiManager.StartGameTransition();
            } else {
                // 데이터가 없으면 기존 로그인 에러 패널 재활용해서 안내!
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