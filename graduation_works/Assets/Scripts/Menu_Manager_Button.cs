using UnityEngine;

public class Menu_Manager_Button : MonoBehaviour {
    public Menu_Manager_UI uiManager; // Menu_UIManager도 이름 바꿨다면 여기 수정

    public void OnClick_NewGame() {
        // 기존: UserSession.HasSaveData() -> 신규: Shared_Manager_Session 사용
        string userId = Shared_Manager_Session.CurrentUserId;
        
        if (Shared_Manager_Session.HasSaveData(userId)) {
            uiManager.ShowError("기존 데이터가 있습니다.\n삭제 후 시작됩니다.");
        } else {
            // 새 게임은 짐(TempLoadData) 없이 시작
            Ingame_System_Save.TempLoadData = null;
            Shared_Manager_Session.IsReadOnlyMode = false;
            uiManager.StartGameTransition();
        }
    }

    public void OnClick_Continue() {
        string userId = Shared_Manager_Session.CurrentUserId;

        if (Shared_Manager_Session.HasSaveData(userId)) {
            // 데이터 로드해서 인게임 시스템에 전달
            Ingame_System_Save.TempLoadData = Shared_Manager_Session.LoadData(userId);
            Shared_Manager_Session.IsReadOnlyMode = false;
            uiManager.StartGameTransition();
        } else {
            uiManager.ShowError("저장된 데이터가 없습니다!");
        }
    }

    public void OnClick_PlayAround() {
        uiManager.ShowError("준비중입니다.");
    }
}