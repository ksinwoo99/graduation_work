using UnityEngine;
using UnityEngine.UI;
using DG.Tweening; 

public class Ingame_ItemPanelController : MonoBehaviour
{
    [Header("기계 선택 UI")]
    public RectTransform ItemPanel;     // 움직일 패널
    public GameObject ItemPanel_On;     // 켜는 버튼 (하얀색, 패널이 숨겨졌을 때 보임)
    public GameObject ItemPanel_Off;    // 끄는 버튼 (붉은 삼각형, 패널이 보일 때 보임)

    [Header("애니메이션 설정")]
    public float slideDuration = 0.5f; 
    public Ease slideEase = Ease.OutQuart; 

    // 👇 핵심 추가: 패널을 얼마나 '더' 내릴지 결정하는 변수
    [Tooltip("패널이 내려갔는데도 흰 버튼이 보이면 이 값을 20, 50 등으로 늘려보세요.")]
    public float extraDownDistance = 0f; 

    private Vector2 showPosition; // 원래 보일 때 위치
    private Vector2 hidePosition; // 숨겨졌을 때 위치
    private bool isPanelShown = true; // 현재 상태 확인용

    void Start()
    {
        showPosition = ItemPanel.anchoredPosition;
        float panelHeight = ItemPanel.rect.height;
        hidePosition = new Vector2(showPosition.x, showPosition.y - panelHeight - extraDownDistance);

        // 버튼 상태 초기화
        UpdateButtons(true);
    }

    // 붉은 삼각형(Button_Off)을 눌렀을 때 호출 -> 패널 숨기기
    public void OnClick_HidePanel()
    {
        if (!isPanelShown) return; 

        // 아래로 이동 (DOTween)
        ItemPanel.DOAnchorPos(hidePosition, slideDuration).SetEase(slideEase);
        
        isPanelShown = false;
        UpdateButtons(false);
    }

    // 켜는 버튼(Button_On)을 눌렀을 때 호출 -> 패널 보이기
    public void OnClick_ShowPanel()
    {
        if (isPanelShown) return; 

        // 원래 위치로 복귀 (DOTween)
        ItemPanel.DOAnchorPos(showPosition, slideDuration).SetEase(slideEase);

        isPanelShown = true;
        UpdateButtons(true);
    }

    //시작 시 on/off 활성화 상태 초기화
    private void UpdateButtons(bool isShown)
    {
        if(ItemPanel_Off != null) ItemPanel_Off.SetActive(isShown);
        if(ItemPanel_On != null) ItemPanel_On.SetActive(!isShown);
    }
}