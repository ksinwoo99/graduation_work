using UnityEngine;
using TMPro;
using UnityEngine.UI;
using UnityEngine.SceneManagement;
using System.Collections.Generic;
using System.Collections;
using System;

public class Login_Manager_Button : MonoBehaviour 
{
    // 유니티 인스펙터 연결 유지를 위해 기존(HEAD) 이름 사용
    public Login_Manager_UI uiManager;

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

    [Serializable]
    public class UserAuthData
    {
        public string user_id;
        public string password;
    }

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

        UserAuthData data = new UserAuthData();
        data.user_id = id;
        data.password = pw;

        StartCoroutine(login_DbManager.Instance.SendJsonRequest("/login", data, (response) =>
        {
            if (response != null && response.status == "LOGIN_SUCCESS")
            {
                // 🔥 [주의] 프로젝트 상황에 맞게 주석을 해제해서 사용하세요!
                // 1. 기존 main 브랜치 방식 유지 시:
                Shared_Manager_Session.CurrentUserId = id; 
                
                // 2. 새로운 Login_Db 브랜치 방식 사용 시:
                // UserSession.UserId = id; 
                // UserSession.UserPk = response.user_pk;

                SceneManager.LoadScene("Menu_Scene");
            }
            else
            {
                uiManager.ShowLoginError();
            }
        }));
    }

    public void OnPwFindButtonClicked() 
    {
        string id = pwFindIdField.text.Trim();
        if (string.IsNullOrEmpty(id)) return;
        
        UserAuthData data = new UserAuthData();
        data.user_id = id;

        StartCoroutine(login_DbManager.Instance.SendJsonRequest("/find_pw", data, (response) =>
        {
            if (response != null && response.status == "SUCCESS")
            {
                pwFindResultText.text = $"비밀번호: {response.password}";
            }
            else if (response != null && response.status == "USER_NOT_FOUND")
            {
                pwFindResultText.text = "<color=#FF5A5A>존재하지 않는 ID입니다.</color>";
            }
            else
            {
                pwFindResultText.text = "<color=#FF5A5A>오류가 발생했습니다.</color>";
            }
        }));
    }

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

        UserAuthData data = new UserAuthData();
        data.user_id = id;
        data.password = pw;

        StartCoroutine(login_DbManager.Instance.SendJsonRequest("/register", data, (response) =>
        {
            if (response != null && response.status == "REGISTER_SUCCESS")
            {
                uiManager.ShowRegisterSuccess();
                
                registerIdField.text = "";
                registerPwField.text = "";
                registerPwCheckField.text = "";
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