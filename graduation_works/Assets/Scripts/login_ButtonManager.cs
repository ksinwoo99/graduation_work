using UnityEngine;
using TMPro;
using UnityEngine.UI;
using UnityEngine.SceneManagement;
using System.Collections.Generic;


public class UI_ButtonManager : MonoBehaviour
{
    public login_UIManager uiManager;

    [Header("로그인")]
    public TMP_InputField loginIdField;
    public TMP_InputField loginPwField;

    [Header("비밀번호 찾기")]
    public TMP_InputField pwFindIdField;
    public TMP_Text pwFindResultText;

    [Header("회원가입")]
    public TMP_InputField registerIdField;
    public TMP_InputField registerPwField;
    public TMP_InputField registerPwCheckField;
    public Button registerButton;

    private Dictionary<string, string> fakeDB = new()
    {
        { "user01", "pass1234" },
        { "admin", "adminPW" },
        { "gpt", "airocks" }
    };

    private bool isIdChecked = false;
    private string lastCheckedId = "";

    // ================= 로그인 =================
    public void OnLoginButtonClicked()
    {
        string id = loginIdField.text.Trim();
        string pw = loginPwField.text.Trim();

        if (fakeDB.ContainsKey(id) && fakeDB[id] == pw) {
            UserSession.UserId = id;   // ✅ 여기서 저장
            SceneManager.LoadScene("Menu_Scene");
        }
        else
            uiManager.ShowLoginError();
    }

    // ================= 비밀번호 찾기 =================
    public void OnPwFindButtonClicked()
    {
        string id = pwFindIdField.text.Trim();

        if (fakeDB.ContainsKey(id))
            pwFindResultText.text = $"비밀번호: {fakeDB[id]}";
        else
            pwFindResultText.text = "<color=#FF5A5A>존재하지 않는 ID입니다.</color>";
    }

    // ================= 회원가입 =================
    public void OnClickCheckDuplicateId()
    {
        string id = registerIdField.text.Trim();

        if (id.Length < 4 || id.Length > 16) {
            uiManager.ShowRegisterIdCheckResult(false);
            uiManager.ShowRegisterError("4자 이상, 16자 이하만\n가능합니다.");
            isIdChecked = false;
            return;
        }

        if (string.IsNullOrEmpty(id)) {
            uiManager.ShowRegisterIdCheckResult(false);
            isIdChecked = false;
            return;
        }

        if (fakeDB.ContainsKey(id)) {
            uiManager.ShowRegisterIdCheckResult(false);
            isIdChecked = false;
        }
        else {
            uiManager.ShowRegisterIdCheckResult(true);
            isIdChecked = true;
            lastCheckedId = id;
        }
    }

    public void OnRegisterIdChanged(string value)
    {
        isIdChecked = false;
        uiManager.HideRegisterIdCheckPanel();
    }

    public void OnRegisterButtonClicked()
    {
        string id = registerIdField.text.Trim();
        string pw = registerPwField.text.Trim();
        string pwCheck = registerPwCheckField.text.Trim();

        if (!isIdChecked || lastCheckedId != id) {
            uiManager.ShowRegisterError("ID 중복확인이 필요합니다.");
            return;
        }

        if (pw.Length < 4 || pw.Length > 32) {
            uiManager.ShowRegisterError("비밀번호는 4자 이상 32자 이하로 입력해주세요.");
            return;
        }

        if (pw != pwCheck) {
            uiManager.ShowRegisterError("비밀번호가 서로 다릅니다.");
            return;
        }

        if (fakeDB.ContainsKey(id)) {
            uiManager.ShowRegisterError("이미 사용 중인 ID입니다.");
            return;
        }

        fakeDB.Add(id, pw);
        isIdChecked = false;

        uiManager.ShowRegisterSuccess();
    }
}
