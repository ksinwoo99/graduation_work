using UnityEngine;
using DG.Tweening;
using TMPro;
using UnityEngine.SceneManagement;
using System.Collections;
using System.Collections.Generic;

public class Menu_Manager_UI : MonoBehaviour {
    [Header("로그인 성공 패널")]
    public GameObject loginSuccessPanel;
    public RectTransform loginSuccessPanelRect;

    [Header("메뉴 선택 패널")]
    public GameObject menuSelectPanel;
    public RectTransform menuSelectPanelRect;
    public TMP_Text menuSelectPanel_UserId;

    [Header("타이틀 로고")]
    public RectTransform titleLogo;

    [Header("에러 패널")]
    public GameObject errorPanel;
    public TMP_Text errorText;

    [Header("환영 텍스트")]
    public TMP_Text welcomeText;

    [Header("전환용 암전 이미지")]
    public CanvasGroup fadeCanvasGroup;

    [Header("리더보드 패널")]
    public GameObject leaderboardPanel;

    [Header("확인 패널 (Yes/No)")]
    public GameObject confirmPanel;
    public TMP_Text confirmText;
    public static string pendingErrorMessage = ""; 

    private Vector2 originalSize = new Vector2(500, 380);
    private Vector2 expandedSize = new Vector2(500, 980);

    private Dictionary<GameObject, Coroutine> hideRoutines = new();

    void Start() {
        loginSuccessPanel.SetActive(true);
        errorPanel.SetActive(false);
        menuSelectPanel.SetActive(false);

        if (!string.IsNullOrEmpty(pendingErrorMessage)) {
            ShowError(pendingErrorMessage);  // 에러창 띄우기
            pendingErrorMessage = "";        // 띄운 후에는 다음 번을 위해 비워줍니다.
        }
        
        loginSuccessPanelRect.sizeDelta = originalSize;
        loginSuccessPanelRect.pivot = new Vector2(0.5f, 0f);

        fadeCanvasGroup.alpha = 0f;

        // UserSession -> Shared_Manager_Session 변경
        welcomeText.text = $"{Shared_Manager_Session.CurrentUserId}님,\n 환영합니다!";

        DOVirtual.DelayedCall(2f, ExpandPanelWithTitleOut);
    }

    void ExpandPanelWithTitleOut() {
        loginSuccessPanelRect.DOKill();
        titleLogo.DOKill();

        titleLogo.DOAnchorPosY(1000, 0.2f).SetEase(Ease.InOutSine);

        loginSuccessPanelRect
            .DOSizeDelta(expandedSize, 0.5f)
            .SetEase(Ease.OutCubic)
            .OnComplete(() => {
                loginSuccessPanel.SetActive(false);
                menuSelectPanel.SetActive(true);
                menuSelectPanel_UserId.text = $"접속중: {Shared_Manager_Session.CurrentUserId}님";

                if (leaderboardPanel != null) {
                leaderboardPanel.SetActive(true);
                // 여기에 리더보드 데이터 새로고침 함수 호출 (아래 스크립트 참고)
                leaderboardPanel.GetComponent<UI_Leaderboard>().RefreshLeaderboard();
                }
            });
    }

    public void StartGameTransition() {
        if (leaderboardPanel != null) leaderboardPanel.SetActive(false);
        Sequence seq = DOTween.Sequence();

        seq.Append(menuSelectPanelRect.DOAnchorPosY(-1200f, 0.4f).SetEase(Ease.InBack));
        seq.AppendInterval(0.5f);
        seq.Append(titleLogo.DOAnchorPos(Vector2.zero, 0.4f).SetEase(Ease.InOutCubic));
        seq.Append(titleLogo.DOScale(10f, 0.6f).SetEase(Ease.InCubic));
        seq.Join(fadeCanvasGroup.DOFade(1f, 0.6f));

        seq.OnComplete(() => {
            SceneManager.LoadScene("InGame_Scene");
        });
    }

    public void ShowError(string message) {
        errorText.text = message;
        ShowTempPanel(errorPanel);
    }

    void ShowTempPanel(GameObject panel, float seconds = 1.5f) {
        if (panel == null) return;

        panel.SetActive(true);

        if (hideRoutines.TryGetValue(panel, out var running) && running != null)
            StopCoroutine(running);

        hideRoutines[panel] = StartCoroutine(AutoHide(panel, seconds));
    }

    IEnumerator AutoHide(GameObject panel, float seconds) {
        yield return new WaitForSeconds(seconds);
        if (panel != null) panel.SetActive(false);
        hideRoutines[panel] = null;
    }

    public void ShowConfirm(string message) {
        if (confirmText != null) confirmText.text = message;
        if (confirmPanel != null) confirmPanel.SetActive(true);
    }

    public void HideConfirm() {
        if (confirmPanel != null) confirmPanel.SetActive(false);
    }
}