using UnityEngine;
using DG.Tweening;
using TMPro;
using System.Collections;
using System.Collections.Generic;

public class Login_Manager_UI : MonoBehaviour {
    [Header("로그인")]
    public GameObject loginPanel;

    [Header("비밀번호 찾기")]
    public GameObject pwFindPanel;

    [Header("회원 가입")]
    public GameObject registerPanel;
    public GameObject registerSuccessPanel;

    [Header("통합 알림창 (에러 및 안내)")]
    public GameObject errorPanel;
    public TMP_Text errorText;

    [Header("DOTween용 RectTransform")]
    public RectTransform loginTitle;
    public RectTransform registerPanelRect;
    public RectTransform registerSuccessPanelRect;

    private Vector2 originalRegisterSize = new Vector2(500, 380);
    private Vector2 expandedRegisterSize = new Vector2(500, 820);
    private Dictionary<GameObject, Coroutine> hideRoutines = new();

    void Start() {
        loginPanel.SetActive(true);
        pwFindPanel.SetActive(false);
        registerPanel.SetActive(false);
        registerSuccessPanel.SetActive(false);
        
        if (errorPanel != null) errorPanel.SetActive(false);

        registerPanelRect.sizeDelta = originalRegisterSize;
        registerPanelRect.pivot = new Vector2(0.5f, 0f);
    }

    public void ShowTempPanel(GameObject panel) {
        if (panel == null) return;
        panel.SetActive(true);
        if (hideRoutines.TryGetValue(panel, out var running) && running != null)
            StopCoroutine(running);
            
        // 🔥 [수정 1] 팝업 유지 시간을 1초에서 5초로 변경!
        hideRoutines[panel] = StartCoroutine(AutoHide(panel, 5f));
    }

    // 🔥 [수정 2] 5초를 대기하되, 도중에 입력이 들어오면 바로 꺼지는 로직으로 변경
    IEnumerator AutoHide(GameObject panel, float seconds) {
        float timer = 0f;
        
        // 버튼을 클릭해서 창을 띄운 그 찰나의 순간(같은 프레임)에 바로 꺼지는 것을 방지하기 위해 아주 잠깐 대기합니다.
        yield return null; 

        while (timer < seconds) {
            timer += Time.deltaTime;

            // 마우스 좌클릭(0), 엔터 키, 넘패드 엔터 키, ESC 키 중 하나라도 누르면 즉시 종료
            if (Input.GetMouseButtonDown(0) || 
                Input.GetKeyDown(KeyCode.Return) || 
                Input.GetKeyDown(KeyCode.KeypadEnter) || 
                Input.GetKeyDown(KeyCode.Escape)) {
                break; // 5초가 다 안 지났어도 루프를 탈출하여 바로 아래의 SetActive(false)로 넘어감
            }

            yield return null;
        }

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
        pwFindPanel.SetActive(false);
        registerPanel.SetActive(false);
        registerSuccessPanel.SetActive(false);
        
        if (errorPanel != null) errorPanel.SetActive(false);

        loginTitle.DOKill();
        loginTitle.DOAnchorPosY(90f, 1.2f).SetEase(Ease.OutBounce);
    }

    public void ShowPwFindPanel() {
        loginPanel.SetActive(false);
        pwFindPanel.SetActive(true);
        registerPanel.SetActive(false);
    }

    public void ShowRegisterPanel() {
        loginTitle.DOKill();
        loginTitle.DOAnchorPosY(540, 0.2f).SetEase(Ease.InOutSine);

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

    public void ShowLoginError() {
        if (errorText != null) errorText.text = "<color=#FF5A5A>아이디 또는 비밀번호를 확인해주세요.</color>";
        ShowTempPanel(errorPanel);
    }

    public void ShowRegisterIdCheckResult(bool available) {
        if (errorText != null) {
            errorText.text = available
                ? "<color=#4CAF50>사용 가능한 ID입니다.</color>"
                : "<color=#FF5A5A>사용 불가능한 ID입니다.</color>";
        }
        ShowTempPanel(errorPanel);
    }

    public void HideRegisterIdCheckPanel() {
        if (errorPanel != null) errorPanel.SetActive(false);
    }

    public void ShowAlertMessage(string message) {
        if (errorText != null) errorText.text = message; 
        ShowTempPanel(errorPanel);
    }

    public void ShowRegisterSuccess() {
        registerPanel.SetActive(false);
        registerSuccessPanelRect.sizeDelta = expandedRegisterSize; 
        registerSuccessPanel.SetActive(true);
    }
}