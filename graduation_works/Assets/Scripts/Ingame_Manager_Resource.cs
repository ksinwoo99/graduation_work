using UnityEngine;
using TMPro;

// 🔥 등급 명칭 완벽 수정 (Common, Rare, Special)
public enum ResourceType { Common, Rare, Special, Legendary }

public class Ingame_Manager_Resource : MonoBehaviour {
    public static Ingame_Manager_Resource Instance;

    [Header("UI 연결")]
    public TextMeshProUGUI txtGold;
    public TextMeshProUGUI txtResourceCommon;
    public TextMeshProUGUI txtResourceRare;     // 🔥 변경됨
    public TextMeshProUGUI txtResourceSpecial;  // 🔥 변경됨

    [Header("골드 설정")]
    public int currentGold = 300;
    public int totalEarnedGold = 0;

    [Header("자원 설정")]
    public int resCommon = 0;
    public int resRare = 0;      // 🔥 변경됨
    public int resSpecial = 0;   // 🔥 변경됨

    [Header("기본 최대 보유량")]
    public int baseMaxGold = 1000;
    public int baseMaxResCommon = 500;
    public int baseMaxResRare = 500;     // 🔥 변경됨
    public int baseMaxResSpecial = 500;  // 🔥 변경됨

    [Header("현재 최대 보유량 (건물 비례)")]
    public int maxGold = 1000;
    public int maxResCommon = 500;
    public int maxResRare = 500;     // 🔥 변경됨
    public int maxResSpecial = 500;  // 🔥 변경됨

    void Awake() {
        if (Instance == null) Instance = this;
    }

    void Start() {
        UpdateCapacities(0, 0); 
    }

    public void UpdateCapacities(int storageCount, int marketCount) {
        maxGold = baseMaxGold + (marketCount * 500);
        maxResCommon = baseMaxResCommon + (storageCount * 100);
        maxResRare = baseMaxResRare + ((storageCount / 2) * 100);    // 🔥 변경됨
        maxResSpecial = baseMaxResSpecial + ((storageCount / 5) * 100); // 🔥 변경됨

        if (currentGold > maxGold) currentGold = maxGold;
        if (resCommon > maxResCommon) resCommon = maxResCommon;
        if (resRare > maxResRare) resRare = maxResRare;             // 🔥 변경됨
        if (resSpecial > maxResSpecial) resSpecial = maxResSpecial; // 🔥 변경됨

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
            case ResourceType.Rare:     // 🔥 변경됨
                resRare += amount;
                if (resRare > maxResRare) resRare = maxResRare;
                break;
            case ResourceType.Special:  // 🔥 변경됨
                resSpecial += amount;
                if (resSpecial > maxResSpecial) resSpecial = maxResSpecial;
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
            case ResourceType.Rare: return resRare >= amount;       // 🔥 변경됨
            case ResourceType.Special: return resSpecial >= amount; // 🔥 변경됨
            default: return false;
        }
    }

    public void ConsumeResource(ResourceType type, int amount) {
        switch(type) {
            case ResourceType.Common: 
                resCommon -= amount; 
                if (resCommon < 0) resCommon = 0; 
                break;
            case ResourceType.Rare:     // 🔥 변경됨
                resRare -= amount; 
                if (resRare < 0) resRare = 0;
                break;
            case ResourceType.Special:  // 🔥 변경됨
                resSpecial -= amount; 
                if (resSpecial < 0) resSpecial = 0;
                break;
        }
        UpdateUI(); 
    }

    void UpdateUI() {
        if (txtGold != null) txtGold.text = $"{currentGold} / {maxGold}";
        if (txtResourceCommon != null) txtResourceCommon.text = $"{resCommon} / {maxResCommon}";
        if (txtResourceRare != null) txtResourceRare.text = $"{resRare} / {maxResRare}";       // 🔥 변경됨
        if (txtResourceSpecial != null) txtResourceSpecial.text = $"{resSpecial} / {maxResSpecial}"; // 🔥 변경됨
    }
}