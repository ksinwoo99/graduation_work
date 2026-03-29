using UnityEngine;
using UnityEngine.UI;
using TMPro;
using System.Collections;
using System.Collections.Generic;

public class Ingame_Manager_Quest : MonoBehaviour
{
    public static Ingame_Manager_Quest Instance;

    [Header("UI 연결")]
    public TextMeshProUGUI questTitleText; 
    public TextMeshProUGUI questGoalText;  
    public TextMeshProUGUI rewardText;     
    
    [Header("퀘스트 목록")]
    public List<QuestData> questList = new List<QuestData>();

    private int currentQuestId = 0;
    private int questStartTotalGold = 0; 

    public int builtMinerCount = 0;
    public int builtProductorCount = 0;
    public int loopUpgradeLevel = 0;
    public int conveyorUpgradeLevel = 0;
    public bool isMinerNameChanged = false;

    public int storageCollectionProgress = 0;
    public int marketSaleProgress = 0;
    
    // ✨ [추가] 철거 및 반복문 진행도 변수
    public int demolishProgress = 0;
    public int loopUsageProgress = 0;

    void Awake() {
        if (Instance == null) Instance = this;
        else Destroy(gameObject);
    }

    void Start() {
        StartQuest(0); 
        StartCoroutine(QuestCheckRoutine());
    }

    void StartQuest(int id) {
        currentQuestId = id;
        
        storageCollectionProgress = 0;
        marketSaleProgress = 0;
        // ✨ 새 퀘스트 시작 시 초기화
        demolishProgress = 0;
        loopUsageProgress = 0;

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
    
    // ✨ [추가] 철거 및 반복문 진행도 증가 함수
    public void AddDemolishProgress() { demolishProgress++; }
    public void AddLoopUsageProgress() { loopUsageProgress++; }

    void CheckConditions() {
        if (Ingame_Manager_Resource.Instance == null) return;

        if (currentQuestId >= questList.Count) {
            if (questTitleText != null) questTitleText.text = "[모든 퀘스트 완료]";
            if (questGoalText != null) questGoalText.text = "공장을 자유롭게 확장하세요!";
            if (rewardText != null) rewardText.text = "";
            return;
        }

        QuestData currentQuest = questList[currentQuestId];
        bool allCleared = true;
        
        string titleString = $"[{currentQuest.title}]";
        string goalString = ""; 

        foreach (var goal in currentQuest.goals) {
            int currentValue = 0;
            string goalName = "";

            string typeName = goal.type.ToString();

            // ✨ [새로운 목표 판정 추가]
            if (typeName == "DemolishMachine") {
                currentValue = demolishProgress;
                goalName = "설치물 철거하기";
            }
            else if (typeName == "UseLoopCode") {
                currentValue = loopUsageProgress;
                goalName = "반복문 코드 적용하기";
            }
            else if (typeName == "CollectWithStorage") {
                currentValue = storageCollectionProgress;
                goalName = "창고로 자원 수집";
            }
            else if (typeName == "SellWithMarket") {
                currentValue = marketSaleProgress;
                goalName = "판매소로 상품 판매(G)";
            }
            else if (typeName == "ChangeMinerName") {
                currentValue = isMinerNameChanged ? 1 : 0;
                goalName = "채굴기 이름 지정하기";
            }
            else if (goal.type == QuestGoalType.BuildMiner) {
                currentValue = builtMinerCount;
                goalName = "채굴기 설치";
            }
            else if (goal.type == QuestGoalType.BuildProductor) {
                currentValue = builtProductorCount;
                goalName = "가공기 설치";
            }
            else if (goal.type == QuestGoalType.CollectCommonResource) {
                currentValue = Ingame_Manager_Resource.Instance.resCommon;
                goalName = "기본 자원 수집";
            }
            else if (goal.type == QuestGoalType.CollectRareResource) { 
                currentValue = Ingame_Manager_Resource.Instance.resRare;
                goalName = "희귀 자원 수집";
            }
            else if (goal.type == QuestGoalType.CollectSpecialResource) { 
                currentValue = Ingame_Manager_Resource.Instance.resSpecial;
                goalName = "특수 자원 수집";
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

        resMgr.EarnGold(0); 
        StartQuest(currentQuestId + 1);
    }
    
    public bool IsAllQuestsCleared() {
        return currentQuestId >= questList.Count;
    }
}