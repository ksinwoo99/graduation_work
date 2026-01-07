using UnityEngine;
using TMPro;

public class Menu_UIManager : MonoBehaviour
{
    public GameObject loginSuccessPanel;
    public TMP_Text welcomeText;

    void Start()
    {
        // 로그인 성공 패널 표시
        loginSuccessPanel.SetActive(true);

        // 환영 문구 세팅
        if (!string.IsNullOrEmpty(UserSession.UserId))
        {
            welcomeText.text = $"{UserSession.UserId}님 환영합니다!";
        }
        else
        {
            welcomeText.text = "환영합니다!";
        }
    }
}
