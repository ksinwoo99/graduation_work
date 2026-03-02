using UnityEngine;
using DG.Tweening;
using TMPro;
using System.Collections;
using System.Collections.Generic;

public class Login_Manager_UI : MonoBehaviour {
    [Header("로그인")]
    public GameObject loginPanel;
    public GameObject loginErrorPanel;

    [Header("비밀번호 찾기")]
    public GameObject pwFindPanel;

    [Header("ID 중복확인")]
    public GameObject registerIDCheckPanel;
    public TMP_Text registerIDCheckText;

    [Header("회원 가입")]
    public GameObject registerPanel;
    public GameObject registerSuccessPanel;
    public GameObject registerErrorPanel;
    public TMP_Text registerErrorText;

    [Header("DOTween용 RectTransform")]
    public RectTransform loginTitle;
    public RectTransform registerPanelRect;
    public RectTransform registerSuccessPanelRect;

    private Vector2 originalRegisterSize = new Vector2(500, 380);
    private Vector2 expandedRegisterSize = new Vector2(500, 600);
    private Dictionary<GameObject, Coroutine> hideRoutines = new();

    void Start() {
        loginPanel.SetActive(true);
        loginErrorPanel.SetActive(false);
        pwFindPanel.SetActive(false);
        registerPanel.SetActive(false);
        registerSuccessPanel.SetActive(false);
        registerIDCheckPanel.SetActive(false);
        registerErrorPanel.SetActive(false);

        registerPanelRect.sizeDelta = originalRegisterSize;
        registerPanelRect.pivot = new Vector2(0.5f, 0f);
    }

    public void ShowTempPanel(GameObject panel) {
        if (panel == null) return;
        panel.SetActive(true);
        if (hideRoutines.TryGetValue(panel, out var running) && running != null)
            StopCoroutine(running);
        hideRoutines[panel] = StartCoroutine(AutoHide(panel, 1f));
    }

    IEnumerator AutoHide(GameObject panel, float seconds) {
        yield return new WaitForSeconds(seconds);
        if (panel != null) panel.SetActive(false);
        hideRoutines[panel] = null;
    }

    public void ShowLoginPanel() {
        RectTransform closingRect = null;

        if (registerPanel.activeSelf) closingRect = registerPanelRect;
        else if (registerSuccessPanel.activeSelf) closingRect = registerSuccessPanelRect;

        if (closingRect != null) {
            closingRect.DOKill();
            closingRect.DOSizeDelta(originalRegisterSize, 0.3f)
                .SetEase(Ease.InOutCubic)
                .OnComplete(ActivateLoginPanel);
        } else {
            ActivateLoginPanel();
        }
    }

    private void ActivateLoginPanel() {
        loginPanel.SetActive(true);
        loginErrorPanel.SetActive(false);
        pwFindPanel.SetActive(false);
        registerPanel.SetActive(false);
        registerIDCheckPanel.SetActive(false);
        registerSuccessPanel.SetActive(false);
        registerErrorPanel.SetActive(false);

        loginTitle.DOKill();
        loginTitle.DOAnchorPosY(90f, 1.2f).SetEase(Ease.OutBounce);
    }

    public void ShowLoginError() {
        ShowTempPanel(loginErrorPanel);
    }

    public void ShowPwFindPanel() {
        loginPanel.SetActive(false);
        pwFindPanel.SetActive(true);
        registerPanel.SetActive(false);
    }

    public void ShowRegisterPanel() {
        loginTitle.DOKill();
        loginTitle.DOAnchorPosY(320, 0.2f).SetEase(Ease.InOutSine);

        loginPanel.SetActive(false);
        pwFindPanel.SetActive(false);
        registerPanel.SetActive(true);

        registerPanelRect.sizeDelta = originalRegisterSize;
        DOVirtual.DelayedCall(0.2f, () => {
            registerPanelRect
                .DOSizeDelta(expandedRegisterSize, 0.5f)
                .SetEase(Ease.OutCubic);
        });
    }

    public void ShowRegisterIdCheckResult(bool available) {
        registerIDCheckText.text = available
            ? "<color=#4CAF50>사용 가능한 ID입니다.</color>"
            : "<color=#FF5A5A>사용 불가능한 ID입니다.</color>";
        ShowTempPanel(registerIDCheckPanel);
    }

    public void HideRegisterIdCheckPanel() {
        registerIDCheckPanel.SetActive(false);
    }

    public void ShowRegisterError(string message) {
        registerErrorText.text = message;
        ShowTempPanel(registerErrorPanel);
    }
    public void ShowRegisterSuccess() {
        registerPanel.SetActive(false);
        registerSuccessPanelRect.sizeDelta = expandedRegisterSize; // 두번째 가입시 글자 안뜬거 수정 반영
        registerSuccessPanel.SetActive(true);
    }
}