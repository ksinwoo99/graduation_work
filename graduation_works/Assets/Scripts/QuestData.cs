using UnityEngine;
using UnityEngine.UI;
using System.Collections.Generic;

public enum QuestGoalType { 
    None,
    BuildMiner,
    BuildProductor,
    CollectCommonResource,
    CollectRareResource,
    CollectSpecialResource,
    CollectWithStorage,
    SellWithMarket,
    EarnGold,
    ChangeMinerName,
    DemolishMachine,
    UseLoopCode
}

[System.Serializable]
public class QuestGoal {
    public QuestGoalType type; 
    public int targetAmount;   
}

[System.Serializable]
public class QuestData {
    [Header("퀘스트 정보")]
    public string title;
    [TextArea] public string rewardText; 

    [Header("목표 설정")]
    public List<QuestGoal> goals = new List<QuestGoal>();

    [Header("실제 보상 지급 설정")]
    public int rewardGold;
    public int rewardCommonResource;    
    public int rewardRareResource;     
    public int rewardSpecialResource;  

    [Header("해금될 하단 메뉴 버튼들")]
    public List<Button> unlockButtons = new List<Button>();

    [Header("시스템 해금 보상")]
    public int rewardLoopLevelUp = 0;      // 1을 넣으면 반복문 레벨업
    public int rewardConveyorLevelUp = 0;  // 1을 넣으면 컨베이어 레벨업
}