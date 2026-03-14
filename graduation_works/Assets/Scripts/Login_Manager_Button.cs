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
    public TMP_InputField pwFindEmailField;     
    public TMP_InputField pwFindAuthCodeField;  

    [Header("회원가입")]
    public TMP_InputField registerIdField;
    public TMP_InputField registerPwField;
    public TMP_InputField registerPwCheckField;
    public TMP_InputField registerEmailField;   
    public TMP_InputField registerAuthCodeField; // 🔥 [추가] 회원가입용 인증번호 입력칸
    public Button registerButton;

    private bool isIdChecked = false;
    private string lastCheckedId = "";

    private bool isIdFoundByEmail = false; 
    private string foundIdForPwFind = "";  

    [Serializable]
    public class UserAuthData
    {
        public string user_id;
        public string password;
        public string email; 
        public string code;  
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

    // ================= 아이디/비밀번호 찾기 =================
    public void OnClick_CheckIdByEmail() 
    {
        string email = pwFindEmailField.text.Trim();

        if (string.IsNullOrEmpty(email)) {
            uiManager.ShowAlertMessage("<color=#FF5A5A>이메일을 입력해주세요.</color>");
            return;
        }

        uiManager.ShowAlertMessage("가입 내역 확인 중...");
        UserAuthData data = new UserAuthData { email = email };

        StartCoroutine(login_DbManager.Instance.SendJsonRequest("/find_id_by_email", data, (response) =>
        {
            if (response != null && response.status == "SUCCESS")
            {
                isIdFoundByEmail = true;
                foundIdForPwFind = response.user_id; 
                uiManager.ShowAlertMessage($"<color=#00FFFF>ID: {foundIdForPwFind}</color>");
            }
            else
            {
                isIdFoundByEmail = false;
                foundIdForPwFind = "";
                uiManager.ShowAlertMessage("<color=#FF5A5A>해당 Email로 가입된 내역이 없습니다.</color>");
            }
        }));
    }

    public void OnPwFindEmailChanged(string value) 
    {
        if (isIdFoundByEmail) {
            isIdFoundByEmail = false;
            foundIdForPwFind = "";
        }
    }

    public void OnClick_SendAuthCode() 
    {
        if (!isIdFoundByEmail || string.IsNullOrEmpty(foundIdForPwFind)) {
            uiManager.ShowAlertMessage("<color=#FF5A5A>ID 유무 확인이\n필요합니다.</color>");
            return;
        }

        string email = pwFindEmailField.text.Trim();
        uiManager.ShowAlertMessage("메일 발송 중...");

        UserAuthData data = new UserAuthData { user_id = foundIdForPwFind, email = email };

        StartCoroutine(login_DbManager.Instance.SendJsonRequest("/send_auth_code", data, (response) =>
        {
            if (response != null && response.status == "SUCCESS")
            {
                uiManager.ShowAlertMessage("<color=#4CAF50>메일로 인증번호가\n발송되었습니다.</color>");
            }
            else
            {
                string errorMsg = response != null ? response.msg : "오류가 발생했습니다.";
                uiManager.ShowAlertMessage($"<color=#FF5A5A>{errorMsg}</color>");
            }
        }));
    }

    public void OnClick_VerifyAuthCode() 
    {
        if (!isIdFoundByEmail || string.IsNullOrEmpty(foundIdForPwFind)) {
            uiManager.ShowAlertMessage("<color=#FF5A5A>ID 유무 확인이\n필요합니다.</color>");
            return;
        }

        string email = pwFindEmailField.text.Trim();
        string code = pwFindAuthCodeField.text.Trim();

        if (string.IsNullOrEmpty(code)) {
            uiManager.ShowAlertMessage("<color=#FF5A5A>인증번호를 입력하세요.</color>");
            return;
        }

        UserAuthData data = new UserAuthData { user_id = foundIdForPwFind, email = email, code = code };

        StartCoroutine(login_DbManager.Instance.SendJsonRequest("/verify_auth_code", data, (response) =>
        {
            if (response != null && response.status == "SUCCESS")
            {
                uiManager.ShowAlertMessage($"<color=#00FFFF>PW: {response.password}</color>");
            }
            else
            {
                uiManager.ShowAlertMessage("<color=#FF5A5A>인증번호가 틀렸습니다.</color>");
            }
        }));
    }

    // ================= 회원가입: ID 중복확인 =================
    public void OnClickCheckDuplicateId() 
    {
        string id = registerIdField.text.Trim();

        if (id.Length < 4 || id.Length > 16)
        {
            uiManager.ShowRegisterIdCheckResult(false);
            uiManager.ShowAlertMessage("4자 이상, 16자 이하만\n가능합니다.");
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

    public void OnClick_RegisterSendAuthCode()
    {
        string email = registerEmailField.text.Trim();

        if (string.IsNullOrEmpty(email)) {
            uiManager.ShowAlertMessage("<color=#FF5A5A>이메일을 먼저\n입력해주세요.</color>");
            return;
        }

        uiManager.ShowAlertMessage("인증코드 발송 중...");

        UserAuthData data = new UserAuthData { email = email };

        //회원가입용 발송 API 주소!
        StartCoroutine(login_DbManager.Instance.SendJsonRequest("/send_register_auth_code", data, (response) =>
        {
            if (response != null && response.status == "SUCCESS")
            {
                uiManager.ShowAlertMessage("<color=#4CAF50>입력하신 메일로 인증번호가\n발송되었습니다.</color>");
            }
            else
            {
                string errorMsg = response != null ? response.msg : "인증번호 발송 실패";
                uiManager.ShowAlertMessage($"<color=#FF5A5A>{errorMsg}</color>");
            }
        }));
    }

    // ================= 회원가입: 완료 버튼 (최종 검증) =================
    public void OnRegisterButtonClicked() 
    {
        string id = registerIdField.text.Trim();
        string pw = registerPwField.text.Trim();
        string pwCheck = registerPwCheckField.text.Trim();
        string email = registerEmailField.text.Trim(); 
        string authCode = registerAuthCodeField.text.Trim(); // 🔥 새로 추가된 인증코드

        // 1. 기존 조건 검증
        if (!isIdChecked || lastCheckedId != id)
        {
            uiManager.ShowAlertMessage("ID 중복확인이\n필요합니다.");
            return;
        }

        if (pw.Length < 4 || pw.Length > 32)
        {
            uiManager.ShowAlertMessage("비밀번호는 4자 이상\n32자 이하로 입력해주세요.");
            return;
        }

        if (pw != pwCheck)
        {
            uiManager.ShowAlertMessage("비밀번호가 서로\n다릅니다.");
            return;
        }

        if (string.IsNullOrEmpty(email)) 
        {
            uiManager.ShowAlertMessage("이메일을 입력해주세요.");
            return;
        }

        // 2. 신규 조건 검증 (인증코드 누락 방지)
        if (string.IsNullOrEmpty(authCode))
        {
            uiManager.ShowAlertMessage("메일로 발송된\n인증코드를 입력해주세요.");
            return;
        }

        // 3. 서버로 최종 가입 요청 (인증코드 동봉)
        UserAuthData data = new UserAuthData();
        data.user_id = id;
        data.password = pw;
        data.email = email; 
        data.code = authCode; 

        StartCoroutine(login_DbManager.Instance.SendJsonRequest("/register", data, (response) =>
        {
            if (response != null && response.status == "REGISTER_SUCCESS")
            {
                uiManager.ShowRegisterSuccess();
                
                // 입력 필드 초기화
                registerIdField.text = "";
                registerPwField.text = "";
                registerPwCheckField.text = "";
                registerEmailField.text = "";
                registerAuthCodeField.text = ""; 
                isIdChecked = false;
            }
            else
            {
                string msg = (response != null) ? response.msg : "알 수 없는 오류";
                uiManager.ShowAlertMessage("회원가입 실패:\n" + msg);
            }
        }));
    }
}