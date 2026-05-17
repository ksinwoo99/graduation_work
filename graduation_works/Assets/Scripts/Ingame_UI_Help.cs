using UnityEngine;
using UnityEngine.UI;
using TMPro;
using DG.Tweening; 
using System.Collections.Generic;

public class Ingame_UI_Help : MonoBehaviour
{
    [Header("패널 슬라이드 설정")]
    public RectTransform helpPanelRect; // HelpPanel 자기 자신
    public float slideDuration = 0.5f;
    public Ease slideEase = Ease.OutQuart;
    public GameObject buttonOn;         // 열기 버튼
    public GameObject buttonOff;        // 닫기 버튼

    private float showPosX;
    private float hidePosX;
    private bool isPanelShown = false;  // 기본은 화면 밖에 숨겨진 상태

    [Header("뷰 전환")]
    public GameObject listView;
    public GameObject detailView;

    [Header("리스트 뷰 설정")]
    public Transform contentParent;      // List_View 안의 ScrollView -> Content
    public GameObject helpButtonPrefab;  // 아까 만든 버튼 프리팹

    [Header("디테일 뷰 설정")]
    public TextMeshProUGUI txtDetailTitle;
    public TextMeshProUGUI txtDetailContent;

    [Header("도움말 데이터 목록")]
    public List<HelpData> allHelpItems; // 인스펙터에서 등록할 모든 도움말 데이터
    
    private List<GameObject> spawnedButtons = new List<GameObject>();

    void Start()
    {
        // 1. 위치 초기화 (현재 에디터에 배치된 위치를 '보이는 위치'로 간주)
        showPosX = helpPanelRect.anchoredPosition.x;
        // 패널 너비만큼 오른쪽(+)으로 밀어서 숨깁니다.
        hidePosX = showPosX + helpPanelRect.rect.width;

        // 2. 시작할 땐 숨겨두기
        helpPanelRect.anchoredPosition = new Vector2(hidePosX, helpPanelRect.anchoredPosition.y);
        UpdateButtonState(false);
        
        // 3. 뷰 초기화
        ShowListView();
        RefreshHelpList();
    }

    // --- 패널 열고 닫기 (Button_On, Button_Off 에 연결하세요!) ---
    public void OnClick_ShowPanel()
    {
        if (isPanelShown) return;
        helpPanelRect.DOAnchorPosX(showPosX, slideDuration).SetEase(slideEase);
        isPanelShown = true;
        UpdateButtonState(true);
        
        ShowListView();     // 열 때마다 무조건 목록부터 보이게
        RefreshHelpList();  // 열 때마다 새로 해금된게 있는지 목록 갱신!
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

    // --- 리스트 / 디테일 뷰 전환 ---
    public void ShowListView()
    {
        listView.SetActive(true);
        detailView.SetActive(false);
    }

    // 뒤로가기 버튼에 연결하세요!
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

    // --- 목록 자동 생성 로직 ---
    public void RefreshHelpList()
    {
        // 1. 기존에 생성된 버튼 싹 지우기 (초기화)
        foreach(var btn in spawnedButtons) Destroy(btn);
        spawnedButtons.Clear();

        // 2. 현재 튜토리얼 진행도 가져오기
        int currentTutorialStep = 0;
        if (Ingame_UI_Tutorial.Instance != null) {
            currentTutorialStep = Ingame_UI_Tutorial.Instance.currentStep;
        }

        // 3. 조건에 맞는 도움말만 버튼으로 생성
        foreach(var item in allHelpItems)
        {
            // 이 도움말의 요구 단계보다 현재 튜토리얼 단계가 같거나 높다면 해금!
            if (currentTutorialStep >= item.unlockTutorialStep)
            {
                GameObject newBtnObj = Instantiate(helpButtonPrefab, contentParent);
                spawnedButtons.Add(newBtnObj);

                // 버튼 텍스트 변경
                TextMeshProUGUI btnText = newBtnObj.GetComponentInChildren<TextMeshProUGUI>();
                if (btnText != null) btnText.text = item.title;

                // 클릭 시 해당 데이터의 디테일 뷰를 열도록 이벤트 추가
                Button btn = newBtnObj.GetComponent<Button>();
                if (btn != null) {
                    btn.onClick.AddListener(() => ShowDetailView(item));
                }
            }
        }
    }
}