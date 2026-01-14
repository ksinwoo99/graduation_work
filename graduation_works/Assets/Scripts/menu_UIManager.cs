using UnityEngine;
using DG.Tweening;
using TMPro;
using UnityEngine.SceneManagement;
using System.Collections;
using System.Collections.Generic;

public class Menu_UIManager : MonoBehaviour
{
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
    public CanvasGroup fadeCanvasGroup; // 전체 화면 검정 이미지

    private Vector2 originalSize = new Vector2(500, 380);
    private Vector2 expandedSize = new Vector2(500, 980);

    private Dictionary<GameObject, Coroutine> hideRoutines = new();

    void Start()
    {
        loginSuccessPanel.SetActive(true);
        errorPanel.SetActive(false);
        menuSelectPanel.SetActive(false);


        loginSuccessPanelRect.sizeDelta = originalSize;
        loginSuccessPanelRect.pivot = new Vector2(0.5f, 0f);

        fadeCanvasGroup.alpha = 0f;

        welcomeText.text = $"{UserSession.UserId}님 환영합니다!";

        DOVirtual.DelayedCall(2f, ExpandPanelWithTitleOut);
    }

    void ExpandPanelWithTitleOut()
    {
        loginSuccessPanelRect.DOKill();
        titleLogo.DOKill();

        titleLogo.DOAnchorPosY(800, 0.2f).SetEase(Ease.InOutSine);

        loginSuccessPanelRect
            .DOSizeDelta(expandedSize, 0.5f)
            .SetEase(Ease.OutCubic)
            .OnComplete(() =>
            {
                loginSuccessPanel.SetActive(false);
                menuSelectPanel.SetActive(true);
                menuSelectPanel_UserId.text = $"접속중: {UserSession.UserId}님";
            });
    }

    // ================= 게임 시작 연출 =================
    public void StartGameTransition()
    {
        Sequence seq = DOTween.Sequence();

        // 1️⃣ 메뉴 선택 패널 ↓ 아래로
        seq.Append(
            menuSelectPanelRect
                .DOAnchorPosY(-1200f, 0.4f)
                .SetEase(Ease.InBack)
        );

        seq.AppendInterval(0.5f);

        // 2️⃣ 타이틀 로고 ↓ 중앙으로
        seq.Append(
            titleLogo
                .DOAnchorPos(Vector2.zero, 0.4f)
                .SetEase(Ease.InOutCubic)
        );

        // 3️⃣ 로고 확대 + 암전
        seq.Append(
            titleLogo
                .DOScale(10f, 0.6f)
                .SetEase(Ease.InCubic)
        );

        seq.Join(
            fadeCanvasGroup
                .DOFade(1f, 0.6f)
        );

        // 4️⃣ 씬 전환
        seq.OnComplete(() =>
        {
            SceneManager.LoadScene("InGame_Scene");
        });
    }

    // ================= 에러 패널 =================
    public void ShowError(string message)
    {
        errorText.text = message;
        ShowTempPanel(errorPanel);
    }

    void ShowTempPanel(GameObject panel, float seconds = 1.5f)
    {
        if (panel == null) return;

        panel.SetActive(true);

        if (hideRoutines.TryGetValue(panel, out var running) && running != null)
            StopCoroutine(running);

        hideRoutines[panel] = StartCoroutine(AutoHide(panel, seconds));
    }

    IEnumerator AutoHide(GameObject panel, float seconds)
    {
        yield return new WaitForSeconds(seconds);
        if (panel != null) panel.SetActive(false);
        hideRoutines[panel] = null;
    }
}
