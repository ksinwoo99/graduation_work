using UnityEngine;
using UnityEngine.UI;
using TMPro;
using DG.Tweening; 
using System.Collections.Generic;

public class Ingame_UI_Help : MonoBehaviour
{
    [Header("패널 슬라이드 설정")]
    public RectTransform helpPanelRect; 
    public float slideDuration = 0.5f;
    public Ease slideEase = Ease.OutQuart;
    public GameObject buttonOn;         
    public GameObject buttonOff;        

    private float showPosX;
    private float hidePosX;
    private bool isPanelShown = false;  

    [Header("뷰 전환")]
    public GameObject listView;
    public GameObject detailView;

    [Header("리스트 뷰 설정")]
    public Transform contentParent;      
    public GameObject helpButtonPrefab;  

    [Header("디테일 뷰 설정")]
    public TextMeshProUGUI txtDetailTitle;
    public TextMeshProUGUI txtDetailContent;

    [Header("도움말 데이터 목록")]
    public List<HelpData> allHelpItems; 
    
    [Header("알림(New) 설정")]
    // ✨ [추가] 메인 도움말 버튼에 띄울 빨간점이나 New 아이콘 오브젝트
    public GameObject redDotIcon; 
    // ✨ [추가] 유저가 패널을 열어서 마지막으로 확인한 해금 항목 개수
    private int viewedUnlockedCount = 0; 
    
    private List<GameObject> spawnedButtons = new List<GameObject>();

    public static Ingame_UI_Help Instance;

    void Awake() {
        if (Instance == null) Instance = this;
    }

    void Start()
    {
        showPosX = helpPanelRect.anchoredPosition.x;
        // ✨ 불필요한 여백 연산 제거 완료
        hidePosX = showPosX + helpPanelRect.rect.width;

        helpPanelRect.anchoredPosition = new Vector2(hidePosX, helpPanelRect.anchoredPosition.y);
        UpdateButtonState(false);
        
        ShowListView();
        RefreshHelpList();
    }

    public void OnClick_ShowPanel()
    {
        if (isPanelShown) return;
        helpPanelRect.DOAnchorPosX(showPosX, slideDuration).SetEase(slideEase);
        isPanelShown = true;
        UpdateButtonState(true);
        
        ShowListView();     
        
        // ✨ [추가] 패널을 열었으므로 알림을 끄고, '확인한 개수'를 갱신합니다.
        if (redDotIcon != null) redDotIcon.SetActive(false);
        UpdateViewedCount();
        
        RefreshHelpList();  
    }

    public void OnClick_HidePanel()
    {
        if (!isPanelShown) return;
        helpPanelRect.DOAnchorPosX(hidePosX, slideDuration).SetEase(slideEase);
        isPanelShown = false;
        UpdateButtonState(false);
    }

    private void UpdateButtonState(bool isShown)
    {
        if(buttonOff != null) buttonOff.SetActive(isShown);
        if(buttonOn != null) buttonOn.SetActive(!isShown);
    }

    public void ShowListView()
    {
        listView.SetActive(true);
        detailView.SetActive(false);
    }

    // 뒤로가기 버튼 연동용 (유니티 인스펙터에서 버튼의 OnClick에 연결하세요!)
    public void OnClick_BackToList() 
    {
        ShowListView();
    }

    private void ShowDetailView(HelpData data)
    {
        listView.SetActive(false);
        detailView.SetActive(true);

        txtDetailTitle.text = data.title;
        txtDetailContent.text = data.content;
    }
    
    // ✨ [추가] 현재 해금된 항목이 몇 개인지 세어서 '확인한 개수'로 저장하는 함수
    private void UpdateViewedCount() 
    {
        int currentTutorialStep = 0;
        if (Ingame_UI_Tutorial.Instance != null) currentTutorialStep = Ingame_UI_Tutorial.Instance.currentStep;

        viewedUnlockedCount = 0;
        foreach(var item in allHelpItems) {
            if (currentTutorialStep >= item.unlockTutorialStep) viewedUnlockedCount++;
        }
    }

    public void RefreshHelpList()
    {
        foreach(var btn in spawnedButtons) Destroy(btn);
        spawnedButtons.Clear();

        int currentTutorialStep = 0;
        if (Ingame_UI_Tutorial.Instance != null) {
            currentTutorialStep = Ingame_UI_Tutorial.Instance.currentStep;
        }

        int currentUnlockedCount = 0; // ✨ [추가] 이번 턴에 해금되어 있는 총 개수

        foreach(var item in allHelpItems)
        {
            GameObject newBtnObj = Instantiate(helpButtonPrefab, contentParent);
            spawnedButtons.Add(newBtnObj);

            TextMeshProUGUI btnText = newBtnObj.GetComponentInChildren<TextMeshProUGUI>();
            Button btn = newBtnObj.GetComponent<Button>();

            // ✨ [수정] 조건 분기 처리 (해금 완료 vs 미해금 ??? 처리)
            if (currentTutorialStep >= item.unlockTutorialStep)
            {
                // 해금됨
                currentUnlockedCount++;
                if (btnText != null) btnText.text = item.title;
                if (btn != null) {
                    btn.interactable = true; 
                    btn.onClick.AddListener(() => ShowDetailView(item));
                }
            }
            else
            {
                // 미해금
                if (btnText != null) btnText.text = "???";
                if (btn != null) {
                    btn.interactable = false; 
                }
            }
        }

        // ✨ [추가] 새 도움말 알림(Red Dot) 켜기 로직
        if (!isPanelShown && currentUnlockedCount > viewedUnlockedCount) {
            if (redDotIcon != null) redDotIcon.SetActive(true);
        }
    }
}