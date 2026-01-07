using UnityEngine;
using TMPro;
using UnityEngine.UI;
using UnityEngine.SceneManagement;
using System.Collections.Generic;
using System.Collections;


public class login_ButtonManager : MonoBehaviour
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
    private bool isIdChecked = false;
    private string lastCheckedId = "";

    // ================= 로그인 =================
    public void OnLoginButtonClicked()
    {
        string id = loginIdField.text.Trim();
        string pw = loginPwField.text.Trim();

        // 입력값 검증
        if (string.IsNullOrEmpty(id) || string.IsNullOrEmpty(pw))
        {
            uiManager.ShowLoginError(); 
            return;
        }
        WWWForm form = new WWWForm();
        form.AddField("id", id);
        form.AddField("password", pw);

        StartCoroutine(login_DbManager.Instance.SendPostRequest("/login", form, (response) =>
        {
            if (response.Trim() == "LOGIN_SUCCESS")
            {
                UserSession.UserId = id;   // ✅ 여기서 저장
                SceneManager.LoadScene("Menu_Scene");
            }
            else
            {
                uiManager.ShowLoginError();
            }
        }));
    }

    // ================= 비밀번호 찾기 =================
    public void OnPwFindButtonClicked()
    {
        string id = pwFindIdField.text.Trim();

        if (string.IsNullOrEmpty(id)) return;
        WWWForm form = new WWWForm();
        form.AddField("id", id);

        StartCoroutine(login_DbManager.Instance.SendPostRequest("/find_pw", form, (response) =>
        {
            if (response.Trim() == "USER_NOT_FOUND")
            {
                pwFindResultText.text = "<color=#FF5A5A>존재하지 않는 ID입니다.</color>";
            }
            else if (response.Trim() == "ERROR")
            {
                pwFindResultText.text = "<color=#FF5A5A>오류가 발생했습니다.</color>";
            }
            else
            {
                pwFindResultText.text = $"비밀번호: {response}";
            }
        }));
    }

    // ================= 회원가입 =================
    public void OnClickCheckDuplicateId()
    {
        string id = registerIdField.text.Trim();

        if (id.Length < 4 || id.Length > 16)
        {
            uiManager.ShowRegisterIdCheckResult(false);
            uiManager.ShowRegisterError("4자 이상, 16자 이하만 가능합니다.");
            isIdChecked = false;
            return;
        }

        if (string.IsNullOrEmpty(id))
        {
            uiManager.ShowRegisterIdCheckResult(false);
            isIdChecked = false;
            return;
        }

        WWWForm form = new WWWForm();
        form.AddField("id", id);
        StartCoroutine(login_DbManager.Instance.SendPostRequest("/check_duplicate", form, (response) =>
        {
            if (response.Trim() == "ID_SAFE")
            {
                uiManager.ShowRegisterIdCheckResult(true);
                isIdChecked = true;
                lastCheckedId = id;
            }
            else
            {
                uiManager.ShowRegisterIdCheckResult(false);
                isIdChecked = false;
            }
        }));
    }

    public void OnRegisterIdChanged(string value)
    {
        if (isIdChecked)
        {
            isIdChecked = false;
            uiManager.HideRegisterIdCheckPanel();
        }
    }

    public void OnRegisterButtonClicked()
    {
        string id = registerIdField.text.Trim();
        string pw = registerPwField.text.Trim();
        string pwCheck = registerPwCheckField.text.Trim();

        // 유효성 검사
        if (!isIdChecked || lastCheckedId != id)
        {
            uiManager.ShowRegisterError("ID 중복확인이 필요합니다.");
            return;
        }

        if (pw.Length < 4 || pw.Length > 32)
        {
            uiManager.ShowRegisterError("비밀번호는 4자 이상 32자 이하로 입력해주세요.");
            return;
        }

        if (pw != pwCheck)
        {
            uiManager.ShowRegisterError("비밀번호가 서로 다릅니다.");
            return;
        }

        // 서버 회원가입 요청
        WWWForm form = new WWWForm();
        form.AddField("id", id);
        form.AddField("password", pw);

        StartCoroutine(login_DbManager.Instance.SendPostRequest("/register", form, (response) =>
        {
            if (response.Trim() == "REGISTER_SUCCESS")
            {
                uiManager.ShowRegisterSuccess();
                
                registerIdField.text = "";
                registerPwField.text = "";
                registerPwCheckField.text = "";
                isIdChecked = false;
            }
            else
            {
                uiManager.ShowRegisterError("회원가입 실패: " + response);
            }
        }));
    }
}