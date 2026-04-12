using UnityEngine;
using UnityEngine.UI;
using TMPro;
using System.Collections;
using System.Collections.Generic;

public class Ingame_Manager_Quest : MonoBehaviour
{
    public static Ingame_Manager_Quest Instance;

    [Header("UI 연결")]
    public GameObject questPanel;          
    public Button btnCloseQuest;           
    public TextMeshProUGUI questTitleText; 
    public TextMeshProUGUI questGoalText;  
    public TextMeshProUGUI rewardText;     
    
    [Header("퀘스트 목록")]
    public List<QuestData> questList = new List<QuestData>();

    public int currentQuestId = 0;
    private int questStartTotalGold = 0; 

    public int builtMinerCount = 0;
    public int builtProductorCount = 0;
    public int builtConveyorCount = 0; // ✨ [신규] 컨베이어 개수
    
    public int loopUpgradeLevel = 0;
    public int conveyorUpgradeLevel = 0;
    public bool isMinerNameChanged = false;

    public int storageCollectionProgress = 0;
    public int marketSaleProgress = 0;
    public int demolishProgress = 0;
    public int expandProgress = 0; 
    
    public bool isMinerLoopUsed = false;
    public bool isProductorLoopUsed = false;

    void Awake() {
        if (Instance == null) Instance = this;
        else Destroy(gameObject);
    }

    void Start() {
        if (btnCloseQuest != null) {
            btnCloseQuest.gameObject.SetActive(false);
            btnCloseQuest.onClick.AddListener(CloseQuestPanel); 
        }

        StartQuest(currentQuestId); 
        StartCoroutine(QuestCheckRoutine());
    }

    public void CloseQuestPanel() {
        if (questPanel != null) questPanel.SetActive(false);
    }

    public void RefreshButtonStates() {
    loopUpgradeLevel = 0;
    // 컨베이어 레벨은 퀘스트 보상이 아닌 골드로 구매하므로 삭제

        for (int i = 0; i < currentQuestId && i < questList.Count; i++) {
            QuestData q = questList[i];
            
            foreach (Button btn in q.unlockButtons) {
                if (btn != null) btn.interactable = true;
            }

            loopUpgradeLevel += q.rewardLoopLevelUp;
        }

        if (currentQuestId >= questList.Count) {
            foreach (var q in questList) {
                foreach (Button btn in q.unlockButtons) {
                    if (btn != null) btn.interactable = true;
                }
            }
        }

        if (Ingame_UI_SystemControl.Instance != null) {
            Ingame_UI_SystemControl.Instance.UpdateAllUI();
        }
    }

    void StartQuest(int id) {
        currentQuestId = id;
        
        storageCollectionProgress = 0;
        marketSaleProgress = 0;
        demolishProgress = 0;
        expandProgress = 0; 
        isMinerLoopUsed = false;
        isProductorLoopUsed = false;

        if (Ingame_Manager_Resource.Instance != null) {
            questStartTotalGold = Ingame_Manager_Resource.Instance.totalEarnedGold;
        }
    }

    IEnumerator QuestCheckRoutine() {
        while (true) {
            CheckConditions();
            yield return new WaitForSeconds(0.5f);
        }
    }

    public void AddStorageProgress(int amount) { storageCollectionProgress += amount; }
    public void AddMarketProgress(int gold) { marketSaleProgress += gold; }
    public void AddDemolishProgress() { demolishProgress++; }
    public void AddExpandProgress() { expandProgress++; } 

    void CheckConditions() {
        if (Ingame_Manager_Resource.Instance == null) return;

        if (currentQuestId >= questList.Count) {
            if (questTitleText != null) questTitleText.text = "[모든 퀘스트 완료]";
            if (questGoalText != null) questGoalText.text = "공장을 자유롭게 확장하세요!";
            if (rewardText != null) rewardText.text = "";
            
            if (btnCloseQuest != null && !btnCloseQuest.gameObject.activeSelf) {
                btnCloseQuest.gameObject.SetActive(true);
            }
            return;
        }

        QuestData currentQuest = questList[currentQuestId];
        bool allCleared = true;
        
        string titleString = $"[{currentQuest.title}]";
        string goalString = ""; 

        foreach (var goal in currentQuest.goals) {
            int currentValue = 0;
            string goalName = "";

            // ✨ enum 타입으로 깔끔하게 매칭
            if (goal.type == QuestGoalType.DemolishMachine) {
                currentValue = demolishProgress; goalName = "설치물 철거하기";
            }
            else if (goal.type == QuestGoalType.ExpandFactory) {
                currentValue = expandProgress; goalName = "공장 부지 확장하기";
            }
            else if (goal.type == QuestGoalType.UseLoopCode) {
                currentValue = (isMinerLoopUsed ? 1 : 0) + (isProductorLoopUsed ? 1 : 0); 
                goalName = "채굴기/가공기에 반복문 적용";
            }
            else if (goal.type == QuestGoalType.CollectWithStorage) {
                currentValue = storageCollectionProgress; goalName = "창고로 자원 수집";
            }
            else if (goal.type == QuestGoalType.SellWithMarket) {
                currentValue = marketSaleProgress; goalName = "판매소로 상품 판매(G)";
            }
            else if (goal.type == QuestGoalType.ChangeMinerName) {
                currentValue = isMinerNameChanged ? 1 : 0; goalName = "채굴기 이름 지정하기";
            }
            else if (goal.type == QuestGoalType.BuildMiner) {
                currentValue = builtMinerCount; goalName = "채굴기 설치";
            }
            else if (goal.type == QuestGoalType.BuildProductor) {
                currentValue = builtProductorCount; goalName = "가공기 설치";
            }
            // ✨ [신규] 컨베이어 설치 감지
            else if (goal.type == QuestGoalType.BuildConveyor) {
                currentValue = builtConveyorCount; goalName = "컨베이어 설치";
            }
            else if (goal.type == QuestGoalType.CollectCommonResource) {
                currentValue = Ingame_Manager_Resource.Instance.resCommon; goalName = "기본 자원 수집";
            }
            else if (goal.type == QuestGoalType.CollectRareResource) { 
                currentValue = Ingame_Manager_Resource.Instance.resRare; goalName = "희귀 자원 수집";
            }
            else if (goal.type == QuestGoalType.CollectSpecialResource) { 
                currentValue = Ingame_Manager_Resource.Instance.resSpecial; goalName = "특수 자원 수집";
            }
            else if (goal.type == QuestGoalType.EarnGold) {
                currentValue = Mathf.Clamp(Ingame_Manager_Resource.Instance.totalEarnedGold - questStartTotalGold, 0, goal.targetAmount);
                goalName = "상품 판매로 골드 획득";
            }

            goalString += $"- {goalName}: {currentValue} / {goal.targetAmount}\n";

            if (currentValue < goal.targetAmount) {
                allCleared = false;
            }
        }

        if (questTitleText != null) questTitleText.text = titleString;
        if (questGoalText != null) questGoalText.text = goalString;
        if (rewardText != null) rewardText.text = currentQuest.rewardText;

        if (allCleared) {
            CompleteCurrentQuest(currentQuest);
        }
    }

    void CompleteCurrentQuest(QuestData quest) {
        var resMgr = Ingame_Manager_Resource.Instance;
        
        if (quest.rewardGold > 0) resMgr.EarnGold(quest.rewardGold);
        if (quest.rewardCommonResource > 0) resMgr.resCommon += quest.rewardCommonResource;
        if (quest.rewardRareResource > 0) resMgr.resRare += quest.rewardRareResource;
        if (quest.rewardSpecialResource > 0) resMgr.resSpecial += quest.rewardSpecialResource;
        
        foreach (Button btn in quest.unlockButtons) {
            if (btn != null) {
                btn.interactable = true;
            }
        }

        if (quest.rewardLoopLevelUp > 0) {
            loopUpgradeLevel += quest.rewardLoopLevelUp;
            if (Ingame_UI_SystemControl.Instance != null) Ingame_UI_SystemControl.Instance.UpdateAllUI();
        }

        if (quest.rewardConveyorLevelUp > 0) {
            conveyorUpgradeLevel += quest.rewardConveyorLevelUp;
            if (Ingame_UI_SystemControl.Instance != null) Ingame_UI_SystemControl.Instance.UpdateAllUI();
        }

        resMgr.EarnGold(0); 
        StartQuest(currentQuestId + 1);
    }
    
    public bool IsAllQuestsCleared() {
        return currentQuestId >= questList.Count;
    }
}