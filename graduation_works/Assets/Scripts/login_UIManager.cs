using UnityEngine;
using DG.Tweening;

public class login_UIManager : MonoBehaviour
{
    // 로그인 / 비밀번호 찾기 / 회원가입 패널 
    public GameObject loginPanel;
    public GameObject pwFindPanel;
    public GameObject registerPanel;
    public GameObject registerPopup;

    public RectTransform loginTitle;
    public RectTransform registerPanelRect;

    // 회원가입 패널 높이 조절용 벡터값 저장
    private Vector2 originalRegisterSize = new Vector2(500, 380); // 기본 높이
    private Vector2 expandedRegisterSize = new Vector2(500, 600); // 확장 높이

    // 시작 시 로그인 패널 제외 나머지 숨김 처리
    void Start()
    {
        loginPanel.SetActive(true);
        pwFindPanel.SetActive(false);
        registerPanel.SetActive(false);
        registerPopup.SetActive(false);
        
        registerPanelRect.sizeDelta = originalRegisterSize;

        // pivot을 아래 고정
        registerPanelRect.pivot = new Vector2(0.5f, 0f);
    }

    public void ShowRegisterPanel()
    {
        // 로고 올라감
        loginTitle.DOKill();
        DOVirtual.DelayedCall(0, () => {
            loginTitle.DOAnchorPosY(320, 0.2f).SetEase(Ease.InOutSine);
        });

        // 로그인 패널 끄고 회원가입 패널 활성화
        loginPanel.SetActive(false);
        pwFindPanel.SetActive(false);
        registerPanel.SetActive(true);

        // 패널 크기 리셋 후 0.3초 뒤 확장
        registerPanelRect.sizeDelta = originalRegisterSize;
        DOVirtual.DelayedCall(0.2f, () => {
            registerPanelRect.DOSizeDelta(expandedRegisterSize, 0.5f).SetEase(Ease.OutCubic);
        });
    }

    public void ShowLoginPanel()
    {
        // 회원가입 패널 크기 줄이기
        registerPanelRect.DOKill();
        registerPanelRect.DOSizeDelta(originalRegisterSize, 0.3f).SetEase(Ease.InOutCubic)
        .OnComplete(() => {
            loginPanel.SetActive(true);
            pwFindPanel.SetActive(false);
            registerPanel.SetActive(false);

            // ✅ 현재 위치에서 자연스럽게 떨어지게
            loginTitle.DOKill();

            float currentY = loginTitle.anchoredPosition.y; // 현재 위치
            loginTitle.anchoredPosition = new Vector2(0, currentY); // 굳이 다시 설정해도 되고

            loginTitle.DOAnchorPosY(90f, 1.2f).SetEase(Ease.OutBounce);
        });
    }

    public void ShowPwFindPanel()
    {
        loginPanel.SetActive(false);
        pwFindPanel.SetActive(true);
        registerPanel.SetActive(false);
    }
}
