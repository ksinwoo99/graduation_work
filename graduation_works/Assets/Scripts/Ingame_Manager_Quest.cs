using UnityEngine;
using UnityEngine.UI;
using TMPro;
using System.Collections;
using System.Collections.Generic;

public class Ingame_Manager_Quest : MonoBehaviour
{
    public static Ingame_Manager_Quest Instance;

    [Header("UI 연결")]
    public TextMeshProUGUI questTitleText; // 네가 만든 제목 UI
    public TextMeshProUGUI questGoalText;  // 🔥 네가 만든 목표 UI (여기에 자동 텍스트가 꽂힘!)
    public TextMeshProUGUI rewardText;     // 네가 만든 보상 UI
    
    [Header("해금 제어할 버튼들")]
    public Button productorButton;
    public Button demolishButton;

    [Header("퀘스트 목록")]
    public List<QuestData> questList = new List<QuestData>();

    private int currentQuestId = 0;
    private int questStartTotalGold = 0; 

    public int builtMinerCount = 0;
    public int builtProductorCount = 0;

    void Awake() {
        if (Instance == null) Instance = this;
        else Destroy(gameObject);
    }

    void Start() {
        if (productorButton != null) productorButton.interactable = false;
        if (demolishButton != null) demolishButton.interactable = false;
        
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
        string goalString = ""; // 🔥 부가 설명 없이 깔끔하게 바로 목록 시작!

        foreach (var goal in currentQuest.goals) {
            int currentValue = 0;
            string goalName = "";

            // 🔥 타입에 맞춰서 숫자와 '자동 완성 이름(goalName)'을 세팅
            if (goal.type == QuestGoalType.BuildMiner) {
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
            else if (goal.type == QuestGoalType.CollectUncommonResource) {
                currentValue = Ingame_Manager_Resource.Instance.resUncommon;
                goalName = "특수 자원 수집";
            }
            else if (goal.type == QuestGoalType.CollectRareResource) {
                currentValue = Ingame_Manager_Resource.Instance.resRare;
                goalName = "희귀 자원 수집";
            }
            else if (goal.type == QuestGoalType.EarnGold) {
                currentValue = Mathf.Clamp(Ingame_Manager_Resource.Instance.totalEarnedGold - questStartTotalGold, 0, goal.targetAmount);
                goalName = "상품 판매로 골드 획득";
            }

            // 세팅된 이름을 goalString에 차곡차곡 쌓음
            goalString += $"- {goalName}: {currentValue} / {goal.targetAmount}\n";

            if (currentValue < goal.targetAmount) {
                allCleared = false;
            }
        }

        // 🔥 완성된 텍스트들을 네가 연결해 둔 3개의 TMP UI에 각각 딱! 꽂아줌
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
        if (quest.rewardUncommonResource > 0) resMgr.resUncommon += quest.rewardUncommonResource;
        if (quest.rewardRareResource > 0) resMgr.resRare += quest.rewardRareResource;
        
        if (quest.unlockProductor && productorButton != null) productorButton.interactable = true;
        if (quest.unlockDemolish && demolishButton != null) demolishButton.interactable = true;

        resMgr.EarnGold(0); 

        StartQuest(currentQuestId + 1);
    }
}