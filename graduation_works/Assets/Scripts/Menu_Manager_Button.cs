using UnityEngine;

public class Menu_Manager_Button : MonoBehaviour {
    public Menu_Manager_UI uiManager; 

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

    public void OnClick_Continue() {
        // 이어하기: 서버 로드 신호를 켠 뒤 인게임으로 진입!
        Ingame_System_Save.isLoadRequested = true;
        Shared_Manager_Session.IsReadOnlyMode = false;
        uiManager.StartGameTransition();
    }

    public void OnClick_PlayAround() {
        uiManager.ShowError("준비중입니다.");
    }
}