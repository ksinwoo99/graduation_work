using UnityEngine;
using TMPro;
using UnityEngine.UI;
using UnityEngine.SceneManagement;
using System.Collections.Generic;
using System.Collections;
using System;

public class Login_Manager_Button : MonoBehaviour 
{
    public Login_Manager_UI uiManager;

    [Header("로그인")]
    public TMP_InputField loginIdField;
    public TMP_InputField loginPwField;

    [Header("비밀번호 찾기")]
    public TMP_InputField pwFindIdField;
    public TMP_InputField pwFindEmailField;     // ✨ [추가] 이메일 입력칸
    public TMP_InputField pwFindAuthCodeField;  // ✨ [추가] 6자리 인증번호 입력칸
    public TMP_Text pwFindResultText;

    [Header("회원가입")]
    public TMP_InputField registerIdField;
    public TMP_InputField registerPwField;
    public TMP_InputField registerPwCheckField;
    public TMP_InputField registerEmailField;   // ✨ [추가] 이메일 가입 입력칸
    public Button registerButton;

    private bool isIdChecked = false;
    private string lastCheckedId = "";

    [Serializable]
    public class UserAuthData
    {
        public string user_id;
        public string password;
        public string email; // ✨ [추가] 이메일 전송용
        public string code;  // ✨ [추가] 인증번호 검증용
    }

    // ================= 로그인 =================
    public void OnLoginButtonClicked()
    {
        string id = loginIdField.text.Trim();
        string pw = loginPwField.text.Trim();

        if (string.IsNullOrEmpty(id) || string.IsNullOrEmpty(pw))
        {
            uiManager.ShowLoginError();
            return;
        }

        UserAuthData data = new UserAuthData();
        data.user_id = id;
        data.password = pw;

        StartCoroutine(login_DbManager.Instance.SendJsonRequest("/login", data, (response) =>
        {
            if (response != null && response.status == "LOGIN_SUCCESS")
            {
                Shared_Manager_Session.CurrentUserId = id; 
                SceneManager.LoadScene("Menu_Scene");
            }
            else
            {
                uiManager.ShowLoginError();
            }
        }));
    }

    // ================= 비밀번호 찾기: 1. 인증번호 메일 발송 =================
    public void OnClick_SendAuthCode() 
    {
        string id = pwFindIdField.text.Trim();
        string email = pwFindEmailField.text.Trim();

        if (string.IsNullOrEmpty(id) || string.IsNullOrEmpty(email)) {
            pwFindResultText.text = "<color=#FF5A5A>아이디와 이메일을 모두 입력하세요.</color>";
            return;
        }
        
        pwFindResultText.text = "메일 발송 중...";

        UserAuthData data = new UserAuthData { user_id = id, email = email };

        StartCoroutine(login_DbManager.Instance.SendJsonRequest("/send_auth_code", data, (response) =>
        {
            if (response != null && response.status == "SUCCESS")
            {
                pwFindResultText.text = "<color=#4CAF50>메일로 인증번호가 발송되었습니다.</color>";
            }
            else
            {
                string errorMsg = response != null ? response.msg : "오류가 발생했습니다.";
                pwFindResultText.text = $"<color=#FF5A5A>{errorMsg}</color>";
            }
        }));
    }

    // ================= 비밀번호 찾기: 2. 인증번호 검증 =================
    public void OnClick_VerifyAuthCode() 
    {
        string id = pwFindIdField.text.Trim();
        string email = pwFindEmailField.text.Trim();
        string code = pwFindAuthCodeField.text.Trim();

        if (string.IsNullOrEmpty(code)) {
            pwFindResultText.text = "<color=#FF5A5A>인증번호를 입력하세요.</color>";
            return;
        }

        UserAuthData data = new UserAuthData { user_id = id, email = email, code = code };

        StartCoroutine(login_DbManager.Instance.SendJsonRequest("/verify_auth_code", data, (response) =>
        {
            if (response != null && response.status == "SUCCESS")
            {
                pwFindResultText.text = $"<color=#00FFFF>찾은 비밀번호: {response.password}</color>";
            }
            else
            {
                pwFindResultText.text = "<color=#FF5A5A>인증번호가 틀렸습니다.</color>";
            }
        }));
    }

    // ================= 회원가입 아이디 중복확인 =================
    public void OnClickCheckDuplicateId() 
    {
        string id = registerIdField.text.Trim();

        if (id.Length < 4 || id.Length > 16)
        {
            uiManager.ShowRegisterIdCheckResult(false);
            uiManager.ShowRegisterError("4자 이상, 16자 이하만\n가능합니다.");
            isIdChecked = false;
            return;
        }

        UserAuthData data = new UserAuthData();
        data.user_id = id;

        StartCoroutine(login_DbManager.Instance.SendJsonRequest("/check_duplicate", data, (response) =>
        {
            if (response != null && response.status == "ID_SAFE")
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

    // ================= 회원가입 완료 버튼 =================
    public void OnRegisterButtonClicked() 
    {
        string id = registerIdField.text.Trim();
        string pw = registerPwField.text.Trim();
        string pwCheck = registerPwCheckField.text.Trim();
        string email = registerEmailField.text.Trim(); // ✨ 이메일 읽어오기

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

        if (string.IsNullOrEmpty(email)) 
        {
            uiManager.ShowRegisterError("이메일을 입력해주세요.");
            return;
        }

        UserAuthData data = new UserAuthData();
        data.user_id = id;
        data.password = pw;
        data.email = email; // ✨ 택배에 이메일 담아서 보내기

        StartCoroutine(login_DbManager.Instance.SendJsonRequest("/register", data, (response) =>
        {
            if (response != null && response.status == "REGISTER_SUCCESS")
            {
                uiManager.ShowRegisterSuccess();
                
                registerIdField.text = "";
                registerPwField.text = "";
                registerPwCheckField.text = "";
                registerEmailField.text = "";
                isIdChecked = false;
            }
            else
            {
                string msg = (response != null) ? response.msg : "알 수 없는 오류";
                uiManager.ShowRegisterError("회원가입 실패: " + msg);
            }
        }));
    }
}