using UnityEngine;
using UnityEngine.UI;
using DG.Tweening; 

public class Ingame_Controller_ItemPanel : MonoBehaviour {
    [Header("기계 선택 UI")]
    public RectTransform ItemPanel;
    public GameObject ItemPanel_On;
    public GameObject ItemPanel_Off;

    [Header("애니메이션 설정")]
    public float slideDuration = 0.5f; 
    public Ease slideEase = Ease.OutQuart; 

    [Tooltip("패널이 내려갔는데도 흰 버튼이 보이면 이 값을 늘리세요.")]
    public float extraDownDistance = 10f; 

    private Vector2 showPosition;
    private Vector2 hidePosition;
    private bool isPanelShown = true;

    void Start() {
        showPosition = ItemPanel.anchoredPosition;
        float panelHeight = ItemPanel.rect.height;
        hidePosition = new Vector2(showPosition.x, showPosition.y - panelHeight - extraDownDistance);

        UpdateButtons(true);
    }

    public void OnClick_HidePanel() {
        if (!isPanelShown) return; 

        ItemPanel.DOAnchorPos(hidePosition, slideDuration).SetEase(slideEase);
        
        isPanelShown = false;
        UpdateButtons(false);
    }

    public void OnClick_ShowPanel() {
        if (isPanelShown) return; 

        ItemPanel.DOAnchorPos(showPosition, slideDuration).SetEase(slideEase);

        isPanelShown = true;
        UpdateButtons(true);
    }

    private void UpdateButtons(bool isShown) {
        if(ItemPanel_Off != null) ItemPanel_Off.SetActive(isShown);
        if(ItemPanel_On != null) ItemPanel_On.SetActive(!isShown);
    }
}