using UnityEngine;
using System.Collections.Generic;

public enum QuestGoalType { 
    None, 
    BuildMiner,        
    BuildProductor,    
    CollectCommonResource,   
    CollectUncommonResource, 
    CollectRareResource,     
    EarnGold           
}

[System.Serializable]
public class QuestGoal {
    public QuestGoalType type; 
    public int targetAmount;   // 🔥 텍스트 적는 칸 없이 깔끔하게 목표치만!
}

[System.Serializable]
public class QuestData {
    [Header("퀘스트 정보")]
    public string title;
    [TextArea] public string rewardText; 

    [Header("목표 설정 (진행도는 자동 표시됨)")]
    public List<QuestGoal> goals = new List<QuestGoal>();

    [Header("실제 보상 지급 설정")]
    public int rewardGold;
    public int rewardCommonResource;    
    public int rewardUncommonResource;  
    public int rewardRareResource;      
    public bool unlockProductor; 
    public bool unlockDemolish;  
}