using UnityEngine;
using TMPro;

public enum ResourceType { Common, Uncommon, Rare, Legendary }

public class Ingame_Manager_Resource : MonoBehaviour {
    public static Ingame_Manager_Resource Instance;

    [Header("UI 연결")]
    public TextMeshProUGUI txtGold;
    public TextMeshProUGUI txtResourceCommon;
    public TextMeshProUGUI txtResourceUncommon;
    public TextMeshProUGUI txtResourceRare;

    [Header("골드 설정")]
    public int currentGold = 300;
    public int totalEarnedGold = 0;

    [Header("자원 설정")]
    public int resCommon = 0;
    public int resUncommon = 0;
    public int resRare = 0;

    // 🔥 [새로 추가] 아무것도 안 지었을 때의 '기본' 최대치
    [Header("기본 최대 보유량")]
    public int baseMaxGold = 1000;
    public int baseMaxResCommon = 500;
    public int baseMaxResUncommon = 500;
    public int baseMaxResRare = 500;

    // 🔥 기존 변수 유지 (UI나 타 스크립트 오류 방지용)
    [Header("현재 최대 보유량 (건물 비례)")]
    public int maxGold = 1000;
    public int maxResCommon = 500;
    public int maxResUncommon = 500;
    public int maxResRare = 500;

    void Awake() {
        if (Instance == null) Instance = this;
    }

    void Start() {
        // 시작할 때 기본 최대치로 한 번 세팅해 줍니다.
        UpdateCapacities(0, 0); 
    }

    public void UpdateCapacities(int storageCount, int marketCount) {
        maxGold = baseMaxGold + (marketCount * 500);
        maxResCommon = baseMaxResCommon + (storageCount * 100);
        maxResUncommon = baseMaxResUncommon + ((storageCount / 2) * 100); 
        maxResRare = baseMaxResRare + ((storageCount / 5) * 100);         

        if (currentGold > maxGold) currentGold = maxGold;
        if (resCommon > maxResCommon) resCommon = maxResCommon;
        if (resUncommon > maxResUncommon) resUncommon = maxResUncommon;
        if (resRare > maxResRare) resRare = maxResRare;

        UpdateUI();
    }

    public void EarnGold(int amount) {
        currentGold += amount;
        totalEarnedGold += amount;
        if (currentGold > maxGold) currentGold = maxGold;
        UpdateUI();
    }

    public void RefundGold(int amount) {
        currentGold += amount;
        if (currentGold > maxGold) currentGold = maxGold;
        UpdateUI();
    }

    public bool SpendGold(int amount) {
        if (currentGold >= amount) {
            currentGold -= amount;
            UpdateUI();
            return true;
        }
        return false;
    }

    public void AddResource(ResourceType type, int amount) {
        switch (type) {
            case ResourceType.Common:
                resCommon += amount;
                if (resCommon > maxResCommon) resCommon = maxResCommon;
                break;
            case ResourceType.Uncommon:
                resUncommon += amount;
                if (resUncommon > maxResUncommon) resUncommon = maxResUncommon;
                break;
            case ResourceType.Rare:
                resRare += amount;
                if (resRare > maxResRare) resRare = maxResRare;
                break;
        }
        UpdateUI();
    }
    
    public bool HasEnoughGold(int amount) {
        return currentGold >= amount; 
    }

    public bool HasEnoughResource(ResourceType type, int amount) {
        switch(type) {
            case ResourceType.Common: return resCommon >= amount;
            case ResourceType.Uncommon: return resUncommon >= amount;
            case ResourceType.Rare: return resRare >= amount;
            default: return false;
        }
    }

    public void ConsumeResource(ResourceType type, int amount) {
        switch(type) {
            case ResourceType.Common: 
                resCommon -= amount; 
                if (resCommon < 0) resCommon = 0; 
                break;
            case ResourceType.Uncommon: 
                resUncommon -= amount; 
                if (resUncommon < 0) resUncommon = 0;
                break;
            case ResourceType.Rare: 
                resRare -= amount; 
                if (resRare < 0) resRare = 0;
                break;
        }
        UpdateUI(); 
    }

    void UpdateUI() {
        if (txtGold != null) txtGold.text = $"{currentGold} / {maxGold}";
        if (txtResourceCommon != null) txtResourceCommon.text = $"{resCommon} / {maxResCommon}";
        if (txtResourceUncommon != null) txtResourceUncommon.text = $"{resUncommon} / {maxResUncommon}";
        if (txtResourceRare != null) txtResourceRare.text = $"{resRare} / {maxResRare}";
    }
}