using UnityEngine;
using UnityEngine.UI;
using UnityEngine.EventSystems;
using TMPro;
using System.Collections.Generic;

public class Ingame_UI_Tutorial : MonoBehaviour
{
    public static Ingame_UI_Tutorial Instance;

    [Header("1. 튜토리얼 스킵 팝업")]
    public GameObject skipPanel;       
    public Button btnSkipYes;          
    public Button btnSkipNo;           

    [Header("2. 튜토리얼 메인 UI")]
    public GameObject dimBackground;   
    public GameObject bubblePanel;     
    public TextMeshProUGUI txtMessage; 
    public Button btnNext;             

    [Header("3. 하이라이트 대상 패널")]
    public GameObject panelResource;            
    public GameObject panelInstallation;        
    public GameObject panelSideGroup;           
    public GameObject panelCoding;              
    public GameObject panelInstallationInfo;    

    [Header("4. 튜토리얼 상호작용 버튼 및 패널")]
    public Button btnTutorialMiner;              
    public TextMeshProUGUI txtTutorialMinerName; 
    public GameObject resizeHandle;              
    public GameObject inputMinerCodeObj;        

    private int currentStep = 0;
    private bool isTutorialActive = false;
    private int startQuestIdForInstall = 0;  
    
    private string initialMinerName = ""; 

    private class HighlightData
    {
        public GameObject panel;
        public bool wasCanvasAdded;
        public bool wasRaycasterAdded;
        public bool origOverrideSorting;
        public string origSortingLayerName;
        public int origSortingOrder;
    }
    private List<HighlightData> activeHighlights = new List<HighlightData>();

    private bool hasScrolledUp = false;
    private bool hasScrolledDown = false;

    void Awake() { if (Instance == null) Instance = this; }

    void Start()
    {
        bubblePanel.SetActive(false);
        dimBackground.SetActive(false);
        
        if (resizeHandle != null)
        {
            EventTrigger trigger = resizeHandle.GetComponent<EventTrigger>();
            if (trigger == null) trigger = resizeHandle.AddComponent<EventTrigger>();

            EventTrigger.Entry entry = new EventTrigger.Entry();
            entry.eventID = EventTriggerType.EndDrag; 
            entry.callback.AddListener((data) => { OnResizeHandleDragged(); });
            trigger.triggers.Add(entry);
        }

        ShowSkipPrompt();
    }

    void Update()
    {
        if (!isTutorialActive) return;

        if (currentStep == 2 && Input.GetMouseButtonUp(1)) 
        {
            currentStep++; PlayStep(currentStep);
        }
        else if (currentStep == 3)
        {
            float scroll = Input.mouseScrollDelta.y;
            if (scroll > 0) hasScrolledUp = true;
            if (scroll < 0) hasScrolledDown = true;

            if (hasScrolledUp && hasScrolledDown)
            {
                currentStep++; PlayStep(currentStep);
            }
        }
        else if (currentStep == 8)
        {
            bool isCtrlPressed = Input.GetKey(KeyCode.LeftControl) || Input.GetKey(KeyCode.RightControl);
            if (isCtrlPressed && Input.mouseScrollDelta.y != 0)
            {
                currentStep++; PlayStep(currentStep);
            }
        }
        else if (currentStep == 10)
        {
            if (txtTutorialMinerName != null && txtTutorialMinerName.text != initialMinerName)
            {
                currentStep++; PlayStep(currentStep);
            }
        }
        // ✨ [핵심 수정] Step 11은 순수 텍스트(DialogMode)이므로 Update에서 검사할 필요가 없습니다. (다음 버튼으로 넘어감)
        
        // ✨ [핵심 수정] Step 12가 실제 코딩 입력 감지입니다! (번호 싱크 맞춤)
        else if (currentStep == 12)
        {
            string currentCode = "";
            if (inputMinerCodeObj != null)
            {
                TMP_InputField tmpInput = inputMinerCodeObj.GetComponent<TMP_InputField>();
                if (tmpInput != null) currentCode = tmpInput.text;
                else
                {
                    InputField legacyInput = inputMinerCodeObj.GetComponent<InputField>();
                    if (legacyInput != null) currentCode = legacyInput.text;
                }
            }

            if (!string.IsNullOrEmpty(currentCode) && currentCode.Replace(" ", "").ToLower().Contains("mining()"))
            {
                currentStep++; PlayStep(currentStep);
            }
        }
        // ✨ [핵심 수정] Step 14가 실제 퀘스트 완료 검사입니다! (번호 싱크 맞춤)
        else if (currentStep == 14)
        {
            var questMgr = Ingame_Manager_Quest.Instance;
            if (questMgr != null && questMgr.currentQuestId > startQuestIdForInstall)
            {
                currentStep++; PlayStep(currentStep);
            }
        }
    }

    public void ShowSkipPrompt()
    {
        skipPanel.SetActive(true);
        dimBackground.SetActive(true); 

        btnSkipYes.onClick.RemoveAllListeners();
        btnSkipYes.onClick.AddListener(EndTutorial);

        btnSkipNo.onClick.RemoveAllListeners();
        btnSkipNo.onClick.AddListener(() => {
            skipPanel.SetActive(false);
            StartTutorial(); 
        });
    }

    public void StartTutorial()
    {
        isTutorialActive = true;
        currentStep = 0;
        PlayStep(currentStep);
    }

    public void PlayStep(int stepIndex)
    {
        bubblePanel.SetActive(true);
        ClearHighlight(); 

        switch (stepIndex)
        {
            case 0:
                SetDialogMode("안녕하세요, 당신의 py.Factory\n발전을 도와줄 어시스트입니다!");
                break;
            case 1:
                SetDialogMode("기본 목표는,\n'설치물의 코딩과 공장의 자동화'\n입니다!");
                break;
            case 2:
                SetActionMode("우클릭을 누른 후 드래그 하면,\n공장 부지를 옮겨 볼 수 있습니다.");
                break;
            case 3:
                hasScrolledUp = false;
                hasScrolledDown = false;
                SetActionMode("또한 스크롤을 통하여 공장의\n줌 인/아웃도 가능합니다!");
                break;
            case 4:
                HighlightPanel(panelResource); 
                SetDialogMode("왼쪽 위에는 현재 보유한 자원,\n그리고 퀘스트 라인을 볼 수 있어요.");
                break;
            case 5:
                HighlightPanel(panelSideGroup); 
                SetDialogMode("오른쪽 패널들에선 현재 공장의 상태,\n업그레이드 등을 할 수 있습니다!");
                break;
            case 6: 
                HighlightPanel(panelInstallation); 
                SetPilotMode("아래쪽 패널에서는 맵에 지을 수 있는\n다양한 설치물들을 선택할 수 있어요.\n\n한번 채굴기를 클릭해보시겠어요?");
                
                if (btnTutorialMiner != null)
                {
                    btnTutorialMiner.onClick.AddListener(OnMinerButtonClicked);
                }
                break;
            case 7:
                HighlightPanel(panelInstallation); 
                HighlightPanel(panelCoding);
                HighlightPanel(panelInstallationInfo); 
                SetDialogMode("이렇게, 코딩을 위한 개발환경과\n해당 설치물에 대한 설명이 뜹니다!");
                break;
            case 8: 
                HighlightPanel(panelInstallation); 
                HighlightPanel(panelCoding);
                HighlightPanel(panelInstallationInfo); 
                SetPilotMode("코딩 창의 위를 누르고 드래그하여\n위치를 바꿀 수도 있어요.\n\n코딩 창의 글씨가 너무 작거나 크다면,\n'Ctrl + 마우스 휠'을 이용해 글자 크기를 조절해 보세요!");
                break;
            case 9: 
                HighlightPanel(panelInstallation); 
                HighlightPanel(panelCoding);
                HighlightPanel(panelInstallationInfo); 
                SetPilotMode("패널 우측 하단의 손잡이를 드래그해서\n창의 크기도 마음대로 조절할 수 있습니다!");
                break;
            case 10: 
                if (txtTutorialMinerName != null) initialMinerName = txtTutorialMinerName.text;

                HighlightPanel(panelInstallation); 
                HighlightPanel(panelCoding);
                HighlightPanel(panelInstallationInfo);
                SetActionMode("자, 이제 첫 번째 퀘스트를\n진행해 볼까요?\n\n코딩 창에 name = \"이름\" 을 입력하고 저장 및 디버깅(F5)을 하여,\n채굴기의 이름을 지어주세요!");
                break;
            case 11:
                SetDialogMode("멋진 이름이네요!\n이렇게 python에서는\n'변수명 = 숫자 or 문자열' 을 입력하여,\n데이터를 저장하는 공간을\n만들 수 있습니다.");
                break;
            case 12:
                HighlightPanel(panelInstallation); 
                HighlightPanel(panelCoding);
                HighlightPanel(panelInstallationInfo);
                SetActionMode("이번에는 실제 기능을\n적용해보겠습니다.\n\n채굴기의 코드에,\n'이 설치물이 필요로 하는 함수'를\n넣어줘야 합니다.\nmining() 이라고 적혀있는데,\n적어넣고 디버깅을 해볼까요?");
                break;
            case 13: 
                if (Ingame_Manager_Quest.Instance != null) {
                    startQuestIdForInstall = Ingame_Manager_Quest.Instance.currentQuestId;
                }
                SetDialogMode("완벽합니다!\n이제 이 채굴기의 설치가 가능해졌습니다.");
                break;
            case 14: 
                SetActionMode("채굴기같은 설치물을 클릭 한 상태로 R키를 누르면\n'생성될 자원 및 상품의 위치 조절'이 가능해요.\n한번 설치해볼까요?");
                btnNext.onClick.RemoveAllListeners();
                btnNext.onClick.AddListener(EndTutorial);
                break;
            default:
                EndTutorial();
                break;
        }
    }

    private void OnMinerButtonClicked()
    {
        if (currentStep == 6)
        {
            if (btnTutorialMiner != null) 
                btnTutorialMiner.onClick.RemoveListener(OnMinerButtonClicked);
                
            currentStep++; PlayStep(currentStep);
        }
    }

    private void OnResizeHandleDragged()
    {
        if (currentStep == 9)
        {
            currentStep++; PlayStep(currentStep);
        }
    }

    private void HighlightPanel(GameObject targetUI)
    {
        if (targetUI == null) return;
        HighlightData data = new HighlightData { panel = targetUI };

        Canvas canvas = targetUI.GetComponent<Canvas>();
        if (canvas == null) {
            canvas = targetUI.AddComponent<Canvas>();
            data.wasCanvasAdded = true;
        } else {
            data.wasCanvasAdded = false;
            data.origOverrideSorting = canvas.overrideSorting;
            data.origSortingLayerName = canvas.sortingLayerName;
            data.origSortingOrder = canvas.sortingOrder;
        }

        GraphicRaycaster raycaster = targetUI.GetComponent<GraphicRaycaster>();
        if (raycaster == null) {
            raycaster = targetUI.AddComponent<GraphicRaycaster>();
            data.wasRaycasterAdded = true;
        } else {
            data.wasRaycasterAdded = false;
        }

        canvas.overrideSorting = true;
        canvas.sortingLayerName = "UI";
        canvas.sortingOrder = 9; 

        activeHighlights.Add(data);
    }

    private void ClearHighlight()
    {
        foreach (var data in activeHighlights)
        {
            if (data.panel == null) continue;

            Canvas canvas = data.panel.GetComponent<Canvas>();
            GraphicRaycaster raycaster = data.panel.GetComponent<GraphicRaycaster>();

            if (data.wasRaycasterAdded && raycaster != null) Destroy(raycaster);
            if (data.wasCanvasAdded && canvas != null) Destroy(canvas);
            else if (canvas != null) {
                canvas.overrideSorting = data.origOverrideSorting;
                canvas.sortingLayerName = data.origSortingLayerName;
                canvas.sortingOrder = data.origSortingOrder;
            }
        }
        activeHighlights.Clear();
    }

    private void SetDialogMode(string msg)
    {
        dimBackground.SetActive(true);
        txtMessage.text = msg;
        btnNext.gameObject.SetActive(true);

        btnNext.onClick.RemoveAllListeners();
        btnNext.onClick.AddListener(() => {
            currentStep++; PlayStep(currentStep);
        });
    }

    private void SetPilotMode(string msg)
    {
        dimBackground.SetActive(true); 
        txtMessage.text = msg;
        btnNext.gameObject.SetActive(false); 
    }

    private void SetActionMode(string msg)
    {
        dimBackground.SetActive(false); 
        txtMessage.text = msg;
        btnNext.gameObject.SetActive(false); 
    }

    public void EndTutorial()
    {
        ClearHighlight(); 
        isTutorialActive = false;
        skipPanel.SetActive(false);
        bubblePanel.SetActive(false);
        dimBackground.SetActive(false);
    }
}