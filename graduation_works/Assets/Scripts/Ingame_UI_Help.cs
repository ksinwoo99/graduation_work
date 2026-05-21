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
    public ScrollRect ListView_Scroll;
    public ScrollRect DetailView_Scroll;
    public float scrollSensitivity = 25f;

    [Header("디테일 뷰 설정")]
    public TextMeshProUGUI txtDetailTitle;
    public TextMeshProUGUI txtDetailContent;

    [Header("도움말 데이터 목록")]
    public List<HelpData> allHelpItems; 
    
    [Header("알림(New) 설정")]
    public GameObject redDotIcon; 
    public int viewedUnlockedCount = 0; 
    public int currentUnlockedCount = 0;

    private List<GameObject> spawnedButtons = new List<GameObject>();

    // ✨ [추가] 유저가 실제로 클릭해서 '읽은' 도움말 데이터를 저장하는 세트
    private HashSet<HelpData> readHelpItems = new HashSet<HelpData>();

    public static Ingame_UI_Help Instance;

    void Awake() {
        if (Instance == null) Instance = this;
    }

    void Start()
    {
        showPosX = helpPanelRect.anchoredPosition.x;
        hidePosX = showPosX + helpPanelRect.rect.width;

        helpPanelRect.anchoredPosition = new Vector2(hidePosX, helpPanelRect.anchoredPosition.y);
        UpdateButtonState(false);

        if (ListView_Scroll != null) ListView_Scroll.scrollSensitivity = scrollSensitivity;
        if (DetailView_Scroll != null) DetailView_Scroll.scrollSensitivity = scrollSensitivity;

        ShowListView();
        RefreshHelpList();
    }

    public void OnClick_ShowPanel()
    {
        if (isPanelShown) return;
        helpPanelRect.DOAnchorPosX(showPosX, slideDuration).SetEase(slideEase).SetUpdate(true);
        isPanelShown = true;
        UpdateButtonState(true);
        
        ShowListView();     
        
        // ✨ [수정] 패널을 열었다고 해서 무조건 메인 레드도트를 끄지 않습니다.
        // 아래 RefreshHelpList()에서 읽지 않은 항목이 있는지 검사하여 결정합니다.
        RefreshHelpList();  
    }

    public void OnClick_HidePanel()
    {
        if (!isPanelShown) return;
        helpPanelRect.DOAnchorPosX(hidePosX, slideDuration).SetEase(slideEase).SetUpdate(true);
        isPanelShown = false;
        UpdateButtonState(false);
    }

    private void UpdateButtonState(bool isShown)
    {
        if (buttonOn != null) buttonOn.SetActive(!isShown);
        if (buttonOff != null) buttonOff.SetActive(isShown);
    }

    public void OnClick_BackToList()
    {
        ShowListView();
        // ✨ 디테일 뷰를 보고 목록으로 돌아왔을 때, 레드도트 상태들을 최신화합니다.
        RefreshHelpList(); 
    }

    private void ShowListView()
    {
        listView.SetActive(true);
        detailView.SetActive(false);
    }

    private void ShowDetailView(HelpData data)
    {
        listView.SetActive(false);
        detailView.SetActive(true);

        txtDetailTitle.text = data.title;
        txtDetailContent.text = data.content;

        // 1. 이 도움말 항목을 읽었음으로 기록합니다.
        if (!readHelpItems.Contains(data))
        {
            readHelpItems.Add(data);
        }

        // ✨ [추가] 이제 방금 항목을 읽었으니, 혹시 "아직 안 읽은 다른 항목"이 남아있는지 체크합니다.
        int currentTutorialStep = 0;
        if (Ingame_UI_Tutorial.Instance != null) {
            currentTutorialStep = Ingame_UI_Tutorial.Instance.currentStep;
        }

        bool hasUnreadItem = false;
        foreach (var item in allHelpItems)
        {
            // 해금은 되었는데 유저가 아직 안 읽은 아이템이 하나라도 있는지 확인
            if (currentTutorialStep >= item.unlockTutorialStep && !readHelpItems.Contains(item))
            {
                hasUnreadItem = true;
                break;
            }
        }

        // ✨ [추가] 만약 방금 마지막 남은 아이템을 읽은 것이라면, 목록으로 돌아가지 않고 
        // 이 상태에서 바로 패널을 닫더라도 메인 패널의 레드도트가 즉시 꺼지도록 처리합니다!
        if (!hasUnreadItem)
        {
            if (redDotIcon != null)
            {
                Image dotImage = redDotIcon.GetComponent<Image>();
                if (dotImage != null) dotImage.DOKill();
                redDotIcon.SetActive(false);
            }
        }
    }

    // --- 목록 자동 생성 로직 ---
    public void RefreshHelpList()
    {
        foreach(var btn in spawnedButtons) Destroy(btn);
        spawnedButtons.Clear();

        int currentTutorialStep = 0;
        if (Ingame_UI_Tutorial.Instance != null) {
            currentTutorialStep = Ingame_UI_Tutorial.Instance.currentStep;
        }

        currentUnlockedCount = 0;
        // ✨ [추가] 해금은 되었으나 아직 유저가 읽지 않은 아이템이 있는지 체크하는 변수
        bool hasUnreadItem = false;

        foreach(var item in allHelpItems)
        {
            GameObject newBtnObj = Instantiate(helpButtonPrefab, contentParent);
            spawnedButtons.Add(newBtnObj);

            TextMeshProUGUI btnText = newBtnObj.GetComponentInChildren<TextMeshProUGUI>();
            Button btn = newBtnObj.GetComponent<Button>();

            // 프리팹 내부에 들어있는 개별 레드도트 오브젝트 찾기
            // (주의: 프리팹 내 레드도트 오브젝트명이 "RedDot"이어야 합니다)
            Transform subRedDotTransform = newBtnObj.transform.Find("RedDot");
            GameObject subRedDot = subRedDotTransform != null ? subRedDotTransform.gameObject : null;

            if (currentTutorialStep >= item.unlockTutorialStep)
            {
                currentUnlockedCount++;
                if (btnText != null) btnText.text = item.title;
                if (btn != null) {
                    btn.interactable = true; 
                    btn.onClick.AddListener(() => ShowDetailView(item));
                }

                // ✨ [추가] 해금은 되었는데 유저가 아직 한 번도 클릭해서 읽지 않은 경우
                if (!readHelpItems.Contains(item))
                {
                    hasUnreadItem = true;
                    if (subRedDot != null) subRedDot.SetActive(true); // 프리팹 레드도트 ON
                }
                else
                {
                    if (subRedDot != null) subRedDot.SetActive(false); // 읽었으면 프리팹 레드도트 OFF
                }
            }
            else
            {
                if (btnText != null) btnText.text = "???";
                if (btn != null) {
                    btn.interactable = false; 
                }
                if (subRedDot != null) subRedDot.SetActive(false); // 미해금은 레드도트 가림
            }
        }

        // ✨ [최종 수정] 메인 도움말 패널 레드도트 알림 조건 제어
        // 1. 패널이 닫혀있는 상태에서 새로운 해금 항목이 있을 때 깜빡임 작동
        // 2. 혹은 패널을 열었거나 닫았더라도 유저가 안 읽은(개별 도트가 켜진) 항목이 하나라도 남아있다면 계속 깜빡임 유지!
        if (hasUnreadItem) {
            if (redDotIcon != null && !redDotIcon.activeSelf) {
                redDotIcon.SetActive(true);
                
                Image dotImage = redDotIcon.GetComponent<Image>();
                if (dotImage != null) {
                    dotImage.DOKill();
                    Color c = dotImage.color;
                    c.a = 1f;
                    dotImage.color = c;

                    // 원래 원하셨던 부드러운 서서히 깜빡이는 롤백 방식 연동
                    dotImage.DOFade(0f, 0.5f).SetLoops(-1, LoopType.Yoyo).SetEase(Ease.InOutSine).SetUpdate(true);
                }
            }
        }
        else {
            // ✨ 모든 해금 항목을 다 읽었다면 패널 메인 레드도트를 완전히 끕니다.
            if (redDotIcon != null) {
                Image dotImage = redDotIcon.GetComponent<Image>();
                if (dotImage != null) dotImage.DOKill();
                redDotIcon.SetActive(false);
            }
        }
    }
}