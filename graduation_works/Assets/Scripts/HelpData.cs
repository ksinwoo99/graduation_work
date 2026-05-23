using UnityEngine;

[CreateAssetMenu(fileName = "New Help Data", menuName = "py.Factory/Help Data")]
public class HelpData : ScriptableObject
{
    [Header("도움말 정보")]
    public string title;       // 예: "1. 변수란"
    [TextArea(5, 10)]
    public string content;     // 예: "변수는 데이터를 저장하는 공간입니다..."
    
    [Header("해금 조건")]
    public int unlockTutorialStep; // 이 튜토리얼 Step에 도달하면 해금됨
    
    [Header("프리팹 설정")]
    public GameObject categoryPrefab;
}