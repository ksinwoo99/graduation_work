using UnityEngine;
using TMPro;
using UnityEngine.UI;
using System.Collections.Generic;

public class UI_ButtonManager : MonoBehaviour
{
    [Header("1️⃣ 로그인 패널")]
    public TMP_InputField loginIdField;
    public TMP_InputField loginPwField;

    [Header("2️⃣ 비밀번호 찾기 패널")]
    public TMP_InputField pwFindIdField;
    public TMP_Text pwFindResultText;

    [Header("3️⃣ 회원가입 패널")]
    public TMP_InputField registerIdField;
    public TMP_InputField registerPwField;
    public TMP_InputField registerPwCheckField;
    public Button registerButton; // 회원가입 버튼 (상태 제어에 사용)

    public GameObject signupSuccessPopup;
    public login_UIManager uiManager;


    private Dictionary<string, string> fakeDB = new Dictionary<string, string>()
    {
        { "user01", "pass1234" },
        { "admin", "adminPW" },
        { "gpt", "airocks" }
    };

    private bool isIdChecked = false;

    // 로그인 버튼 클릭 시
    public void OnLoginButtonClicked()
    {
        string id = loginIdField.text.Trim();
        string pw = loginPwField.text.Trim();

        if (fakeDB.ContainsKey(id) && fakeDB[id] == pw)
        {
            Debug.Log("<color=green>로그인 성공!</color>");
            // TODO: 추후 팝업 또는 다음 씬 전환 구현
        }
        else
        {
            Debug.Log("<color=red>아이디 또는 비밀번호가 틀렸습니다.</color>");
            // TODO: 팝업 오류 메시지로 대체 가능
        }
    }

    // 비밀번호 찾기 버튼 클릭 시
    public void OnPwFindButtonClicked()
    {
        string id = pwFindIdField.text.Trim();

        if (fakeDB.ContainsKey(id))
        {
            string pw = fakeDB[id];
            pwFindResultText.text = $"비밀번호: {pw}";
        }
        else
        {
            pwFindResultText.text = "<color=red>존재하지 않는 ID입니다.</color>";
        }
    }

    // 중복확인 버튼 클릭 시
    public void OnIdCheckButtonClicked()
    {
        string id = registerIdField.text.Trim();

        if (string.IsNullOrEmpty(id))
        {
            Debug.Log("<color=red>ID를 입력해주세요.</color>");
            isIdChecked = false;
            return;
        }

        if (fakeDB.ContainsKey(id))
        {
            Debug.Log("<color=red>이미 존재하는 ID입니다.</color>");
            isIdChecked = false;
        }
        else
        {
            Debug.Log("<color=green>사용 가능한 ID입니다.</color>");
            isIdChecked = true;
        }
    }

    // 회원가입 버튼 클릭 시
    public void OnRegisterButtonClicked()
    {
        string id = registerIdField.text.Trim();
        string pw = registerPwField.text.Trim();
        string pwCheck = registerPwCheckField.text.Trim();

        if (!isIdChecked)
        {
            Debug.Log("<color=red>ID 중복확인을 먼저 해주세요.</color>");
            return;
        }

        if (string.IsNullOrEmpty(pw) || string.IsNullOrEmpty(pwCheck))
        {
            Debug.Log("<color=red>비밀번호를 입력해주세요.</color>");
            return;
        }

        if (pw != pwCheck)
        {
            Debug.Log("<color=red>비밀번호가 일치하지 않습니다.</color>");
            return;
        }

        if (fakeDB.ContainsKey(id))
        {
            Debug.Log("<color=red>이미 등록된 ID입니다. 다시 확인해주세요.</color>");
            return;
        }

        // 회원가입 성공
        fakeDB.Add(id, pw);
        isIdChecked = false;
        // ✅ 팝업 띄우기
        signupSuccessPopup.SetActive(true);
    }

    public void OnConfirmSignupPopup()
    {
        signupSuccessPopup.SetActive(false);
        uiManager.ShowLoginPanel(); // 애니메이션 포함 로그인 화면 복귀
    }
}
