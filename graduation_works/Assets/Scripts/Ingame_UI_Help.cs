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

        // 새로하기면 레드닷 초기화, 아니면 기기에서 불러오기
        if (Ingame_System_Save.isNewGameRequested) {
            ResetReadStatus();
        } else {
            LoadReadStatusFromDevice();
        }

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

        // 방문 모드인지 확인하는 변수 추가
        bool isVisiting = Shared_Manager_Session.IsVisiting;

        currentUnlockedCount = 0;
        bool hasUnreadItem = false;

        foreach(var item in allHelpItems)
        {
            GameObject prefabToUse = item.categoryPrefab != null ? item.categoryPrefab : helpButtonPrefab;
            GameObject newBtnObj = Instantiate(prefabToUse, contentParent);
            spawnedButtons.Add(newBtnObj);

            TextMeshProUGUI btnText = newBtnObj.GetComponentInChildren<TextMeshProUGUI>();
            Button btn = newBtnObj.GetComponent<Button>();

            Transform subRedDotTransform = newBtnObj.transform.Find("RedDot");
            GameObject subRedDot = subRedDotTransform != null ? subRedDotTransform.gameObject : null;

            // 튜토리얼 단계를 만족했거나, '방문 모드'라면 해금!
            if (currentTutorialStep >= item.unlockTutorialStep || isVisiting)
            {
                currentUnlockedCount++;
                if (btnText != null) btnText.text = item.title;
                if (btn != null) {
                    btn.interactable = true; 
                    btn.onClick.AddListener(() => ShowDetailView(item));
                }

                if (!readHelpItems.Contains(item) && !isVisiting)
                {
                    hasUnreadItem = true;
                    if (subRedDot != null) subRedDot.SetActive(true); // 프리팹 레드도트 ON
                }
                else
                {
                    if (subRedDot != null) subRedDot.SetActive(false); // 읽었거나 방문모드면 OFF
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

    // ==========================================
    // 💡 레드닷(읽음 상태) 기기 저장 및 초기화 로직
    // ==========================================

    // [기능 A] 저장하기 (Ingame_System_Save에서 호출됨)
    public void SaveReadStatusToDevice() {
        string userId = Shared_Manager_Session.CurrentUserId;
        if (string.IsNullOrEmpty(userId)) return;

        foreach (var item in readHelpItems) {
            PlayerPrefs.SetInt("HelpRead_" + userId + "_" + item.title, 1);
        }
        PlayerPrefs.Save();
        Debug.Log("도움말 레드닷 상태가 기기에 저장되었습니다.");
    }

    // [기능 B] 불러오기 (Start에서 이어하기 시 호출됨)
    public void LoadReadStatusFromDevice() {
        string userId = Shared_Manager_Session.CurrentUserId;
        if (string.IsNullOrEmpty(userId)) return;

        readHelpItems.Clear(); 
        foreach (var item in allHelpItems) {
            // 기기에 1(읽음)로 저장되어 있으면 메모리 리스트에 추가
            if (PlayerPrefs.GetInt("HelpRead_" + userId + "_" + item.title, 0) == 1) {
                readHelpItems.Add(item); 
            }
        }
    }

    // [기능 C] 초기화 (Start에서 새로하기 시 호출됨)
    public void ResetReadStatus() {
        string userId = Shared_Manager_Session.CurrentUserId;
        if (string.IsNullOrEmpty(userId)) return;

        foreach (var item in allHelpItems) {
            PlayerPrefs.DeleteKey("HelpRead_" + userId + "_" + item.title);
        }
        readHelpItems.Clear();
        PlayerPrefs.Save();
        Debug.Log("도움말 레드닷 상태가 초기화되었습니다.");
    }
}