using UnityEngine;

public class Menu_ButtonManager : MonoBehaviour
{
    public Menu_UIManager uiManager;

    // ================= 새로하기 =================
    public void OnClickNewGame() {
        if (UserSession.HasSaveData()) {
            uiManager.ShowError(
                "기존 플레이 데이터가 있습니다.\n새로 시작하면 삭제됩니다."
            );
        }
        else {
            GameData.IsNewGame = true;
            UserSession.DeleteSaveData();
            uiManager.StartGameTransition();
        }
    }

    // ================= 이어하기 =================
    public void OnClickContinue() {
        if (UserSession.HasSaveData()) {
            GameData.IsNewGame = false;
            UserSession.LoadLocal();
            uiManager.StartGameTransition();
        }
        else {
            uiManager.ShowError("기존에 저장된 데이터가 없습니다!");
        }
    }

    // ================= 놀러가기 =================
    public void OnClickPlayAround() {
        uiManager.ShowError("준비중입니다.");
    }
}
