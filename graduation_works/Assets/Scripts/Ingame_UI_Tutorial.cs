using UnityEngine;
using UnityEngine.UI;
using UnityEngine.EventSystems;
using TMPro;
using System.Collections.Generic;
using System.Text.RegularExpressions; 

public class Ingame_UI_Tutorial : MonoBehaviour
{
    public static Ingame_UI_Tutorial Instance;

    [Header("0. 테스트/디버그 설정")]
    public bool forceStartTutorial = false; 

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
    public Button btnDebug; 

    public Button btnTutorialProductor; 
    public Button btnTutorialExpand;    
    public Button btnTutorialDemolish;  

    private int currentStep = 0;
    
    public bool isTutorialActive = false;
    public bool isActionMode = false; 

    private int startQuestIdForInstall = 0;  
    private int startQuestIdForLoop = 0; // ✨ [신규] 반복문 퀘스트 추적용

    private bool shouldSkipTutorialOnStart = false;

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

    void Awake() 
    { 
        if (Instance == null) Instance = this; 

        if (Ingame_System_Save.isLoadRequested || Shared_Manager_Session.IsVisiting)
        {
            shouldSkipTutorialOnStart = true;
        }
    }

    void Start()
    {
        bubblePanel.SetActive(false);
        dimBackground.SetActive(false);
        skipPanel.SetActive(false); 
        
        if (resizeHandle != null)
        {
            EventTrigger trigger = resizeHandle.GetComponent<EventTrigger>();
            if (trigger == null) trigger = resizeHandle.AddComponent<EventTrigger>();

            EventTrigger.Entry entry = new EventTrigger.Entry();
            entry.eventID = EventTriggerType.EndDrag; 
            entry.callback.AddListener((data) => { OnResizeHandleDragged(); });
            trigger.triggers.Add(entry);
        }

        if (forceStartTutorial)
        {
            StartTutorial(); 
        }
        else if (shouldSkipTutorialOnStart)
        {
            EndTutorial();
        }
        else
        {
            ShowSkipPrompt();
        }
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
            if (hasScrolledUp && hasScrolledDown) { currentStep++; PlayStep(currentStep); }
        }
        else if (currentStep == 8)
        {
            bool isCtrlPressed = Input.GetKey(KeyCode.LeftControl) || Input.GetKey(KeyCode.RightControl);
            if (isCtrlPressed && Input.mouseScrollDelta.y != 0) { currentStep++; PlayStep(currentStep); }
        }
        // ✨ [수정] 17단계(채굴기 설치 롤백), 37단계(가공기 설치 롤백)
        else if (currentStep == 17 || currentStep == 37)
        {
            if (Ingame_Manager_Build.Instance != null && !Ingame_Manager_Build.Instance.isBuildMode)
            {
                if (Ingame_Manager_Build.Instance.GetInstalledObjects().Count > 0) { 
                    currentStep++; PlayStep(currentStep); 
                }
                else { 
                    currentStep = (currentStep == 17) ? 15 : 35; 
                    PlayStep(currentStep); 
                }
            }
        }
        // ✨ [핵심 추가] 46단계: 반복문 퀘스트를 완료하여 다음 퀘스트로 넘어갔는지 감지!
        else if (currentStep == 46)
        {
            if (Ingame_Manager_Quest.Instance != null && Ingame_Manager_Quest.Instance.currentQuestId > startQuestIdForLoop)
            {
                currentStep++; PlayStep(currentStep);
            }
        }
    }

    private string GetCleanInputText()
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
                else
                {
                    TextMeshProUGUI tmpText = inputMinerCodeObj.GetComponent<TextMeshProUGUI>();
                    if (tmpText != null) currentCode = tmpText.text;
                }
            }
        }
        return Regex.Replace(currentCode, "<.*?>", string.Empty).Replace(" ", "").ToLower();
    }

    public void CheckNameCodeAndProceed()
    {
        if (currentStep == 10 && GetCleanInputText().Contains("name="))
        {
            currentStep++; PlayStep(currentStep);
        }
    }

    public void CheckMiningCodeAndProceed()
    {
        if (currentStep == 13 && GetCleanInputText().Contains("mining()"))
        {
            currentStep++; PlayStep(currentStep);
        }
    }

    public void CheckProductorCodeAndProceed()
    {
        if (currentStep == 33) // ✨ 32에서 33으로 변경됨
        {
            string code = GetCleanInputText();
            if (code.Contains("if") && code.Contains("elif") && code.Contains("producting("))
            {
                currentStep++; PlayStep(currentStep);
            }
        }
    }

    public void ShowSkipPrompt()
    {
        skipPanel.SetActive(true); dimBackground.SetActive(true); 
        btnSkipYes.onClick.RemoveAllListeners(); btnSkipYes.onClick.AddListener(EndTutorial);
        btnSkipNo.onClick.RemoveAllListeners(); btnSkipNo.onClick.AddListener(() => { skipPanel.SetActive(false); StartTutorial(); });
    }

    public void StartTutorial() { isTutorialActive = true; currentStep = 0; PlayStep(currentStep); }

    public void PlayStep(int stepIndex)
    {
        bubblePanel.SetActive(true);
        ClearHighlight(); 

        switch (stepIndex)
        {
            case 0: SetDialogMode("안녕하세요, 당신의 py.Factory\n발전을 도와줄 어시스트입니다!"); break;
            case 1: SetDialogMode("기본 목표는,\n'설치물의 코딩과 공장의 자동화'\n입니다!"); break;
            case 2: SetActionMode("우클릭을 누른 후 드래그 하면,\n공장 부지를 옮겨 볼 수 있습니다."); break;
            case 3: hasScrolledUp = false; hasScrolledDown = false; SetActionMode("또한 스크롤을 통하여 공장의\n줌 인/아웃도 가능합니다!"); break;
            case 4: HighlightPanel(panelResource); SetDialogMode("왼쪽 위에는 현재 보유한 자원,\n그리고 퀘스트 라인을 볼 수 있어요."); break;
            case 5: HighlightPanel(panelSideGroup); SetDialogMode("오른쪽 패널들에선 현재 공장의 상태,\n업그레이드 등을 할 수 있습니다!"); break;
            case 6: HighlightPanel(panelInstallation); SetPilotMode("아래쪽 패널에서는 맵에 지을 수 있는\n다양한 설치물들을 선택할 수 있어요.\n\n한번 채굴기를 클릭해보시겠어요?");
                if (btnTutorialMiner != null) btnTutorialMiner.onClick.AddListener(OnMinerButtonClicked); break;
            case 7: HighlightPanel(panelInstallation); HighlightPanel(panelCoding); HighlightPanel(panelInstallationInfo); SetDialogMode("이렇게, 코딩을 위한 개발환경과\n해당 설치물에 대한 설명이 뜹니다!"); break;
            case 8: HighlightPanel(panelInstallation); HighlightPanel(panelCoding); HighlightPanel(panelInstallationInfo); SetPilotMode("코딩 창의 위를 누르고 드래그하여\n위치를 바꿀 수도 있어요.\n\n코딩 창의 글씨가 너무 작거나 크다면,\n'Ctrl + 마우스 휠'을 이용해 글자 크기를 조절해 보세요!"); break;
            case 9: HighlightPanel(panelInstallation); HighlightPanel(panelCoding); HighlightPanel(panelInstallationInfo); SetPilotMode("패널 우측 하단의 손잡이를 드래그해서\n창의 크기도 마음대로 조절할 수 있습니다!"); break;
            case 10: SetActionMode("자, 이제 첫 번째 퀘스트를\n진행해 볼까요?\n\n코딩 창에 name = \"원하는 이름\" 을 입력하고 저장 및 디버깅(F5)을 하여,\n채굴기의 이름을 지어주세요!"); break;
            case 11: HighlightPanel(panelInstallation); HighlightPanel(panelCoding); HighlightPanel(panelInstallationInfo); SetDialogMode("설치물 버튼의 이름을 보시면,\nname 변수에 저장한 내용으로\n변경되었습니다."); break;
            case 12: HighlightPanel(panelInstallation); HighlightPanel(panelCoding); HighlightPanel(panelInstallationInfo); SetDialogMode("이렇게 python에서는\n'변수명 = 숫자 or 문자열' 을 입력하여,\n데이터를 저장하는 공간을\n만들 수 있습니다.\n\n다음에는 실제 기능을 적용해보죠!"); break;
            case 13: SetActionMode("채굴기의 코드에,\n'이 설치물이 필요로 하는 함수'를\n넣어줘야 합니다.\n\nmining() 이라고 적혀있는데,\n적어넣고 디버깅을 해볼까요?"); break;
            case 14: if (Ingame_Manager_Quest.Instance != null) { startQuestIdForInstall = Ingame_Manager_Quest.Instance.currentQuestId; } HighlightPanel(panelInstallation); HighlightPanel(panelCoding); HighlightPanel(panelInstallationInfo); SetDialogMode("완벽합니다!\n이제 이 채굴기의 설치가\n가능해졌습니다."); break;
            case 15: SetActionMode("채굴기같은 설치물 선택 중 R키를 누르면\n'생성될 요소의 위치 조절'이 가능해요.\n한번 맵에 클릭하여 설치해볼까요?"); break;
            case 16: SetDialogMode("훌륭합니다!\n맵에 채굴기가\n성공적으로 배치되었습니다."); break;
            case 17: SetActionMode("이제 우클릭을 누르거나\n선택된 설치물 버튼을 다시 눌러서,\n설치 모드에서 나가보세요."); break;
            case 18: SetDialogMode("성공적으로 상태가 저장되고,\n설치 모드에서 빠져나왔습니다!"); break;
            case 19: SetActionMode("코딩대로 자원이 잘 채굴되는지\n조금만 기다려 볼까요?"); break;
            case 20: SetDialogMode("채굴기가 첫 자원 채집을\n성공하였습니다!"); break;
            case 21: SetActionMode("생성된 자원을 마우스로 직접 클릭해서\n획득해 보세요!"); break;
            case 22: SetDialogMode("처음으로 얻은 자원이네요, 축하합니다!\n\n지금은 기본자원만 얻었지만,\n채굴기마다 채취할 수 있는 자원이\n다양하게 있습니다."); break;
            case 23: SetDialogMode("그런데 자원을 획득하고 나니\n채굴기가 멈춰버렸네요?"); break;
            case 24: SetDialogMode("이 채굴기는 반복문이 없는\n'단순 코드형'이라서\n1회 채굴 후 정지되었기 때문입니다!"); break;
            case 25: HighlightPanel(panelSideGroup); SetActionMode("채굴기를 직접 클릭하거나,\n우측 패널의 '전체 (재)가동'을 눌러\n다시 작동시킬 수 있습니다.\n\n자원을 한 번 더 획득해 보세요!"); break;
            case 26: SetDialogMode("좋습니다, 잘 따라오고 계시네요!\n\n다만 지금같은 방법은 너무 불편하죠?\n진행하다 보면, 기계가 자동 반복하도록\n코딩하는 방법을 알려드리겠습니다!"); break;
            
            // =========================================================================
            // ✨ 가공기 구간 (분할 적용)
            // =========================================================================
            case 27: SetDialogMode("지금은 우선 획득한 자원을 바탕으로,\n더 복잡한 로직이 필요한\n'가공기'를 알려드릴게요."); break; 
            case 28: SetDialogMode("가공기는 자원을 소모하여\n판매 가능한 상품을 만들어냅니다.\n\n모든 가공기는 A타입과 B타입,\n두 가지 상품을 만들 수 있어요."); break;
            case 29: HighlightPanel(panelInstallation); SetPilotMode("아래쪽 패널에서 '가공기'를\n한번 클릭해 보시겠어요?");
                if (btnTutorialProductor != null) btnTutorialProductor.onClick.AddListener(OnProductorButtonClicked); break;
            case 30: HighlightPanel(panelInstallation); HighlightPanel(panelCoding); HighlightPanel(panelInstallationInfo); SetDialogMode("가공기는 꼭 복잡하게 짤 필요 없이,\n단순히 producting(Common, 'A')\n한 줄만 적어도 A상품을 만들어냅니다.\n\n(물론 이름 설정은 필수에요!)"); break;
            case 31: HighlightPanel(panelInstallation); HighlightPanel(panelCoding); HighlightPanel(panelInstallationInfo); SetDialogMode("하지만 if와 elif 문을 사용하면,\n현재 자원 상태에 따라 똑똑하게\n만들 상품을 나눌 수 있습니다!"); break;
            
            // ✨ [수정] 32단계 대사 분리
            case 32: HighlightPanel(panelInstallation); HighlightPanel(panelCoding); HighlightPanel(panelInstallationInfo); 
                SetDialogMode("자원이 100개 이상일 땐 A를,\n50개 이상일 땐 B를 만들게 해볼까요?"); break;
            
            case 33: HighlightPanel(panelInstallation); HighlightPanel(panelCoding); HighlightPanel(panelInstallationInfo); 
                SetActionMode("if resCommon >= 100:\n    producting(Common, 'A')\nelif resCommon >= 50:\n    producting(Common, 'B')\n\n입력 후 디버깅(F5) 해보세요!");
                break;
            
            case 34: HighlightPanel(panelInstallation); HighlightPanel(panelCoding); HighlightPanel(panelInstallationInfo); SetDialogMode("완벽합니다!\n이제 이 가공기의 설치가\n가능해졌습니다."); break;
            case 35: SetActionMode("채굴기 때처럼 맵에 가공기를\n클릭하여 설치해볼까요?\n(R키로 배출 방향을 조절할 수 있습니다)"); break;
            case 36: SetDialogMode("훌륭합니다!\n가공기가 성공적으로 배치되었습니다."); break;
            case 37: SetActionMode("이제 우클릭을 누르거나 취소 버튼을 눌러\n설치 모드에서 나가보세요."); break;
            case 38: SetDialogMode("설치 상태가 정상적으로 저장되고\n설치 모드에서 빠져나왔습니다!"); break;
            case 39: SetActionMode("가공기가 자원을 가져가서 코딩된 대로\n상품을 만들어낼 때까지 기다려 볼까요?\n(자원이 부족하다면 채굴기를 켜주세요!)"); break;
            case 40: SetDialogMode("가공기에서 판매 가능한\n첫 상품을 만들어냈습니다!"); break;
            case 41: SetActionMode("생성된 상품을 마우스로 직접 클릭해서\n판매해 보세요."); break;
            case 42: SetDialogMode("첫 수익입니다, 축하드려요!\n이렇게 가공된 상품을 팔아 번 골드로\n공장을 업그레이드할 수 있습니다."); break;

            // =========================================================================
            // ✨ 반복문 퀘스트 연동 구간
            // =========================================================================
            case 43: SetDialogMode("완벽합니다!\n이제 '반복문(Loop)'을\n배워볼 시간입니다."); break;
            case 44: SetDialogMode("매번 기계를 켜주는 건 번거롭죠.\nfor문이나 while문을 사용하면\n알아서 반복 작동합니다!"); break;
            case 45: SetDialogMode("다만!!!\n\n현재 공장 시스템의 과부하를 막기 위해\n반복문은 최대 10회까지만 허용됩니다."); break;
            
            case 46: 
                // ✨ 퀘스트 ID를 저장하고 다음 퀘스트로 넘어갈 때까지 기다립니다.
                if (Ingame_Manager_Quest.Instance != null) startQuestIdForLoop = Ingame_Manager_Quest.Instance.currentQuestId;
                HighlightPanel(panelInstallation); HighlightPanel(panelCoding); HighlightPanel(panelInstallationInfo); 
                SetActionMode("채굴기와 가공기 각각의 코드에\n반복문을 추가하여\n퀘스트를 완료해 보세요!\n(예: for i in range(10):)");
                break;
                
            case 47: SetDialogMode("정말 대단합니다! 이제 기계들이\n스스로 10번씩 척척 일할 겁니다."); break;

            // =========================================================================
            // ✨ 물류, 확장, 철거 구간 (분할 적용)
            // =========================================================================
            case 48: HighlightPanel(panelInstallation); SetDialogMode("생산량이 늘어나면 물류가 중요해집니다."); break;
            case 49: HighlightPanel(panelInstallation); SetDialogMode("'컨베이어'는 코드에 move() 하나만\n적으면 자동으로 작동합니다."); break;
            case 50: HighlightPanel(panelInstallation); SetDialogMode("'창고'와 '판매소'는 코딩 창이 없습니다.\n대신 설치만 하면 자원 보유 최대치가 늘어나죠!"); break;

            case 51: HighlightPanel(panelSideGroup); SetDialogMode("공장이 좁아진다면 언제든\n우측 상단의 '공장 확장' 버튼을 눌러\n부지를 넓힐 수 있습니다."); break;
            case 52: HighlightPanel(panelSideGroup); SetPilotMode("첫 확장은 무료이니,\n직접 '확장' 버튼을 클릭해 볼까요?");
                if (btnTutorialExpand != null) btnTutorialExpand.onClick.AddListener(OnExpandButtonClicked); break;
            
            case 53: HighlightPanel(panelInstallation); SetDialogMode("부지가 한결 넓어졌네요!"); break;
            case 54: HighlightPanel(panelInstallation); SetDialogMode("마지막으로, 잘못 설치한 기계는 하단의\n'철거' 버튼을 눌러 지울 수 있습니다."); break;
            case 55: SetPilotMode("하단의 '철거' 아이콘을 클릭하여\n기계들을 부술 준비를 해보세요!");
                if (btnTutorialDemolish != null) btnTutorialDemolish.onClick.AddListener(OnDemolishButtonClicked); break;
            
            case 56: SetDialogMode("이것으로 파견 AI 어시스턴트의\n모든 기초 안내가 끝났습니다!"); break;
            case 57: SetDialogMode("이제 퀘스트 라인을 따라\n최고의 공장을 만들어 보세요!");
                btnNext.onClick.RemoveAllListeners(); btnNext.onClick.AddListener(EndTutorial); break;
                
            default: EndTutorial(); break;
        }
    }

    private void OnMinerButtonClicked()
    {
        if (currentStep == 6)
        {
            if (btnTutorialMiner != null) btnTutorialMiner.onClick.RemoveListener(OnMinerButtonClicked);
            currentStep++; PlayStep(currentStep);
        }
    }

    private void OnProductorButtonClicked()
    {
        if (currentStep == 29)
        {
            if (btnTutorialProductor != null) btnTutorialProductor.onClick.RemoveListener(OnProductorButtonClicked);
            currentStep++; PlayStep(currentStep);
        }
    }

    private void OnExpandButtonClicked()
    {
        if (currentStep == 52) 
        {
            if (btnTutorialExpand != null) btnTutorialExpand.onClick.RemoveListener(OnExpandButtonClicked);
            currentStep++; PlayStep(currentStep);
        }
    }

    private void OnDemolishButtonClicked()
    {
        if (currentStep == 55) 
        {
            if (btnTutorialDemolish != null) btnTutorialDemolish.onClick.RemoveListener(OnDemolishButtonClicked);
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
        if (canvas == null) { canvas = targetUI.AddComponent<Canvas>(); data.wasCanvasAdded = true; } 
        else { data.wasCanvasAdded = false; data.origOverrideSorting = canvas.overrideSorting; data.origSortingLayerName = canvas.sortingLayerName; data.origSortingOrder = canvas.sortingOrder; }

        GraphicRaycaster raycaster = targetUI.GetComponent<GraphicRaycaster>();
        if (raycaster == null) { raycaster = targetUI.AddComponent<GraphicRaycaster>(); data.wasRaycasterAdded = true; } 
        else { data.wasRaycasterAdded = false; }

        canvas.overrideSorting = true; canvas.sortingLayerName = "UI"; canvas.sortingOrder = 9; 
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
            else if (canvas != null) { canvas.overrideSorting = data.origOverrideSorting; canvas.sortingLayerName = data.origSortingLayerName; canvas.sortingOrder = data.origSortingOrder; }
        }
        activeHighlights.Clear();
    }

    private void SetDialogMode(string msg)
    {
        isActionMode = false; dimBackground.SetActive(true); txtMessage.text = msg;
        btnNext.gameObject.SetActive(true);
        btnNext.onClick.RemoveAllListeners(); btnNext.onClick.AddListener(() => { currentStep++; PlayStep(currentStep); });
    }

    private void SetPilotMode(string msg)
    {
        isActionMode = false; dimBackground.SetActive(true); txtMessage.text = msg;
        btnNext.gameObject.SetActive(false); 
    }

    private void SetActionMode(string msg)
    {
        isActionMode = true; dimBackground.SetActive(false); txtMessage.text = msg;
        btnNext.gameObject.SetActive(false); 
    }

    public void EndTutorial()
    {
        isActionMode = false; ClearHighlight(); isTutorialActive = false;
        skipPanel.SetActive(false); bubblePanel.SetActive(false); dimBackground.SetActive(false);
    }

    // =========================================================
    // ✨ 외부 연동 트리거 (인덱스 수정됨)
    // =========================================================
    public void TriggerMachineInstalled() 
    { 
        if (currentStep == 15 || currentStep == 35) { currentStep++; PlayStep(currentStep); } 
    }
    
    public void TriggerResourceSpawned() 
    { 
        if (currentStep == 19 || currentStep == 39) { currentStep++; PlayStep(currentStep); } 
    }
    
    public void TriggerResourceCollected() 
    { 
        if (currentStep == 21 || currentStep == 25 || currentStep == 41) { currentStep++; PlayStep(currentStep); } 
    }
    
    public void TriggerMinerRestarted() { }

    public void TriggerCompileResult(bool isCompileError)
    {
        if (!isTutorialActive) return;
        if (isCompileError) return;

        if (currentStep == 10) CheckNameCodeAndProceed();
        else if (currentStep == 13) CheckMiningCodeAndProceed();
        else if (currentStep == 33) CheckProductorCodeAndProceed(); 
        // ✨ 반복문 코딩 검사는 이제 퀘스트 완료 연동으로 대체되었으므로 여기서 검사하지 않습니다.
    }
}