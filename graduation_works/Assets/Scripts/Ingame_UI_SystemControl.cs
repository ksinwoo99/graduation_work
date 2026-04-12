using UnityEngine;
using UnityEngine.UI;
using TMPro;
using System.Collections;

public class Ingame_UI_SystemControl : MonoBehaviour
{
    public static Ingame_UI_SystemControl Instance;

    [Header("1. 시스템 현재 상태 표시 (맨 위 3줄)")]
    public TextMeshProUGUI txtStatusLoop;     
    public TextMeshProUGUI txtStatusMap;      
    public TextMeshProUGUI txtStatusConveyor; 

    [Header("2. 공통 팝업창 UI (확장/업그레이드)")]
    public GameObject popupPanel;             
    public TextMeshProUGUI txtPopupAlert;     
    public GameObject buttonGroup;            

    [Header("3. 메인 UI - 공장 확장")]
    public Button btnExpandMain;              
    public TextMeshProUGUI txtExpandNextSize; 
    public TextMeshProUGUI txtExpandCost;     

    [Header("4. 메인 UI - 컨베이어 벨트 속도 개선")]
    public GameObject panelConveyorUpgrade;   
    public Button btnUpgradeConveyor;         
    public TextMeshProUGUI txtConveyorNextSpeed; 
    public TextMeshProUGUI txtConveyorCost;      
    
    public int costConveyorNormal = 2000;
    public int costConveyorFast = 5000;

    private enum PopupType { None, ExpandMap, UpgradeConveyor }
    private PopupType currentPopupType = PopupType.None;

    private bool isErrorState = false;        
    private Coroutine autoCloseCoroutine;     

    void Awake() { if (Instance == null) Instance = this; }

    void Start() {
        if (popupPanel != null) popupPanel.SetActive(false);
        
        if (btnExpandMain != null) btnExpandMain.onClick.AddListener(OnClick_OpenExpandPopup);
        if (btnUpgradeConveyor != null) btnUpgradeConveyor.onClick.AddListener(OnClick_OpenConveyorPopup);
        
        UpdateAllUI();
    }

    void Update() {
        if (isErrorState) {
            if (Input.GetMouseButtonDown(0) || Input.anyKeyDown) {
                OnClick_CancelPopup();
            }
        }
    }

    public void UpdateAllUI() {
        var buildMgr = Ingame_Manager_Build.Instance;
        var questMgr = Ingame_Manager_Quest.Instance;

        if (buildMgr != null) {
            if (txtStatusMap != null) txtStatusMap.text = $"맵 현재 크기: {buildMgr.currentMapSize} x {buildMgr.currentMapSize}";
            int nextSize = buildMgr.currentMapSize + buildMgr.expandSizeStep;
            int expCost = buildMgr.GetCurrentExpandCost();
            if (txtExpandNextSize != null) txtExpandNextSize.text = $"{nextSize} x {nextSize}";
            if (txtExpandCost != null) txtExpandCost.text = expCost == 0 ? "무료" : $"{expCost} G";
        }

        if (questMgr != null) {
            if (txtStatusLoop != null) {
                if (questMgr.loopUpgradeLevel == 0) txtStatusLoop.text = "사용 불가";
                else if (questMgr.loopUpgradeLevel == 1) txtStatusLoop.text = "최대 10회 가능";
                else txtStatusLoop.text = "무한 루프 가능";
            }

            int convLevel = questMgr.conveyorUpgradeLevel;
            if (convLevel == 0) {
                if (txtStatusConveyor != null) txtStatusConveyor.text = "사용 불가";
                if (panelConveyorUpgrade != null) panelConveyorUpgrade.SetActive(false);
                
                if (txtConveyorNextSpeed != null) txtConveyorNextSpeed.text = "2 (Normal)";
                if (txtConveyorCost != null) txtConveyorCost.text = $"{costConveyorNormal} G";
            } else {
                if (panelConveyorUpgrade != null) panelConveyorUpgrade.SetActive(true);
                if (txtStatusConveyor != null) {
                    if (convLevel == 1) txtStatusConveyor.text = "1 (Slow)";
                    else if (convLevel == 2) txtStatusConveyor.text = "2 (Normal)";
                    else txtStatusConveyor.text = "3 (Fast)";
                }

                if (convLevel == 1) {
                    if (txtConveyorNextSpeed != null) txtConveyorNextSpeed.text = "2 (Normal)";
                    if (txtConveyorCost != null) txtConveyorCost.text = $"{costConveyorNormal} G";
                    if (btnUpgradeConveyor != null) btnUpgradeConveyor.interactable = true;
                } else if (convLevel == 2) {
                    if (txtConveyorNextSpeed != null) txtConveyorNextSpeed.text = "3 (Fast)";
                    if (txtConveyorCost != null) txtConveyorCost.text = $"{costConveyorFast} G";
                    if (btnUpgradeConveyor != null) btnUpgradeConveyor.interactable = true;
                } else {
                    if (txtConveyorNextSpeed != null) txtConveyorNextSpeed.text = "MAX (최고 속도)";
                    if (txtConveyorCost != null) txtConveyorCost.text = "-";
                    if (btnUpgradeConveyor != null) btnUpgradeConveyor.interactable = false;
                }
            }
        }
    }

    public void OnClick_OpenExpandPopup() {
        PreparePopup(PopupType.ExpandMap, "확장하시겠습니까?");
    }

    public void OnClick_OpenConveyorPopup() {
        var questMgr = Ingame_Manager_Quest.Instance;
        if (questMgr == null || questMgr.conveyorUpgradeLevel >= 3) return;

        PreparePopup(PopupType.UpgradeConveyor, "개선하시겠습니까?");
    }

    private void PreparePopup(PopupType type, string alertText) {
        currentPopupType = type;
        isErrorState = false;
        if (popupPanel != null) popupPanel.SetActive(true);
        if (buttonGroup != null) buttonGroup.SetActive(true);
        if (txtPopupAlert != null) txtPopupAlert.text = alertText;
    }

    public void OnClick_ConfirmPopup() {
        var questMgr = Ingame_Manager_Quest.Instance;
        var resMgr = Ingame_Manager_Resource.Instance;
        var buildMgr = Ingame_Manager_Build.Instance;

        if (currentPopupType == PopupType.ExpandMap) {
            if (buildMgr != null) {
                if (buildMgr.TryExpandMap()) { 
                    
                    // ✨ [튜토리얼 연동 3] 확장 "예" 버튼을 눌러 확장이 성공했을 때 튜토리얼 진행!
                    if (Ingame_UI_Tutorial.Instance != null && Ingame_UI_Tutorial.Instance.isTutorialActive) {
                        Ingame_UI_Tutorial.Instance.TriggerMapExpanded();
                    }

                    OnClick_CancelPopup(); 
                    UpdateAllUI();
                } else {
                    ShowInsufficientGoldError();
                }
            }
        } 
        else if (currentPopupType == PopupType.UpgradeConveyor) {
            if (questMgr == null || resMgr == null) return;
            
            int cost = (questMgr.conveyorUpgradeLevel <= 1) ? costConveyorNormal : costConveyorFast;

            if (resMgr.HasEnoughGold(cost)) {
                resMgr.SpendGold(cost);
                questMgr.conveyorUpgradeLevel++;
                if (buildMgr != null) buildMgr.ShowFloatingText("컨베이어 속도 개선 완료!", Camera.main.transform.position);
                OnClick_CancelPopup();
                UpdateAllUI();
            } else {
                ShowInsufficientGoldError();
            }
        }
    }

    private void ShowInsufficientGoldError() {
        isErrorState = true;
        if (txtPopupAlert != null) txtPopupAlert.text = "골드가 부족합니다!";
        if (buttonGroup != null) buttonGroup.SetActive(false); 
        
        if (autoCloseCoroutine != null) StopCoroutine(autoCloseCoroutine);
        autoCloseCoroutine = StartCoroutine(Co_AutoClosePopup(5f));
    }

    private IEnumerator Co_AutoClosePopup(float delay) {
        yield return new WaitForSeconds(delay);
        OnClick_CancelPopup();
    }

    public void OnClick_CancelPopup() {
        if (autoCloseCoroutine != null) StopCoroutine(autoCloseCoroutine);
        if (popupPanel != null) popupPanel.SetActive(false);
        currentPopupType = PopupType.None;
        isErrorState = false;
    }
}