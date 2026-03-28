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
    public bool isConveyorUpgraded = false;
    
    // ✨ [추가] 채굴기 이름이 변경되었는지 기억할 변수
    public bool isMinerNameChanged = false;

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

            // 🔥 [에러 방지 팁] 혹시 아래 에러가 나면 QuestGoalType Enum이 선언된 곳(아마 QuestData 스크립트)에 ChangeMinerName 을 꼭 추가해주세요!
            if (goal.type.ToString() == "ChangeMinerName") { // 임시로 문자열 비교로 안전하게 해두거나 Enum을 쓰셔도 됩니다.
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