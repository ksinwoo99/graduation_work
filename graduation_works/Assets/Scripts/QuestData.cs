using UnityEngine;
using UnityEngine.UI;
using System.Collections.Generic;

public enum QuestGoalType { 
    None,
    BuildMiner,
    BuildProductor,
    BuildConveyor,         // ✨ [신규] 컨베이어 설치 목표
    CollectCommonResource,
    CollectRareResource,
    CollectSpecialResource,
    CollectWithStorage,
    SellWithMarket,
    EarnGold,
    ChangeMinerName,
    DemolishMachine,
    UseLoopCode,
    ExpandFactory          // ✨ [신규] 공장 확장 목표
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
    public int rewardLoopLevelUp = 0;      
    public int rewardConveyorLevelUp = 0;  
}