using UnityEngine;
using UnityEngine.UI;
using TMPro;
using System.Collections;

public class Ingame_Manager_Quest : MonoBehaviour
{
    public static Ingame_Manager_Quest Instance;

    [Header("UI 연결")]
    public TextMeshProUGUI questText; 
    public TextMeshProUGUI rewardText; // 추가된 보상 표시용 텍스트
    
    [Header("해금 제어할 버튼들")]
    public Button productorButton;
    public Button demolishButton;

    private int currentQuestId = 0;
    private int quest2StartTotalGold = 0;

    void Awake() {
        if (Instance == null) Instance = this;
        else Destroy(gameObject);
    }

    void Start() {
        if (productorButton != null) productorButton.interactable = false;
        if (demolishButton != null) demolishButton.interactable = false;
        
        StartCoroutine(QuestCheckRoutine());
    }

    IEnumerator QuestCheckRoutine() {
        while (true) {
            CheckConditions();
            yield return new WaitForSeconds(0.5f);
        }
    }

    void CheckConditions() {
        if (Ingame_Manager_Resource.Instance == null) return;

        int currentResource = Ingame_Manager_Resource.Instance.resCommon;
        int totalEarned = Ingame_Manager_Resource.Instance.totalEarnedGold;

        if (currentQuestId == 0) {
            int minerCount = FindObjectsOfType<logic_Miner_Common>().Length;
            
            if (questText != null) {
                questText.text = $"[퀘스트 1]\n- 채굴기 설치: {minerCount} / 3\n- 기본 자원 수집: {currentResource} / 50";
            }
            if (rewardText != null) {
                rewardText.text = "[보상]\n- 가공기 건설 해금\n- 200 골드\n- 기본 자원 40";
            }

            if (minerCount >= 3 && currentResource >= 50) {
                CompleteQuest1();
            }
        }
        else if (currentQuestId == 1) {
            int productorCount = FindObjectsOfType<logic_Productor_Common>().Length;
            int earnedDuringQuest2 = totalEarned - quest2StartTotalGold;
            
            if (questText != null) {
                // 목표치를 200으로 수정
                questText.text = $"[퀘스트 2]\n- 가공기 설치: {productorCount} / 1\n- 상품 판매로 골드 획득: {Mathf.Clamp(earnedDuringQuest2, 0, 200)} / 200";
            }
            if (rewardText != null) {
                rewardText.text = "[보상]\n- 철거 및 이후 설치물 해금";
            }

            if (productorCount >= 1 && earnedDuringQuest2 >= 200) { // 목표치 200으로 수정
                CompleteQuest2();
            }
        }
    }

    void CompleteQuest1() {
        currentQuestId++;
        
        // 보상 지급: 골드 200 + 기본 자원 40
        Ingame_Manager_Resource.Instance.EarnGold(200); 
        Ingame_Manager_Resource.Instance.resCommon += 40; 
        
        // UI 강제 갱신 (네 리소스 매니저 구조상 EarnGold 호출 시 텍스트가 갱신된다고 가정)
        Ingame_Manager_Resource.Instance.EarnGold(0);

        quest2StartTotalGold = Ingame_Manager_Resource.Instance.totalEarnedGold; 
        if (productorButton != null) productorButton.interactable = true; 
    }

    void CompleteQuest2() {
        currentQuestId++;
        if (demolishButton != null) demolishButton.interactable = true; 
        
        if (questText != null) questText.text = "[퀘스트 완료]\n모든 기능 해제됨";
        if (rewardText != null) rewardText.text = ""; // 완료 시 보상 텍스트 비우기
    }
}