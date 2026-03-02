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
    public int maxGold = 1000;
    public int totalEarnedGold = 0;

    [Header("자원 설정")]
    public int resCommon = 0;
    public int resUncommon = 0;
    public int resRare = 0;

    public int maxResCommon = 500;
    public int maxResUncommon = 500;
    public int maxResRare = 500;

    void Awake() {
        if (Instance == null) Instance = this;
    }

    void Start() {
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

    // =====================================
    // 🔥 [새로 추가] 복합 자원 결제를 위한 검사/차감 함수들
    // =====================================
    
    // 1. 골드가 충분히 있는지 '검사만' 하는 함수
    public bool HasEnoughGold(int amount) {
        return currentGold >= amount; 
    }

    // 2. 특정 자원이 충분히 있는지 '검사만' 하는 함수
    public bool HasEnoughResource(ResourceType type, int amount) {
        switch(type) {
            case ResourceType.Common: return resCommon >= amount;
            case ResourceType.Uncommon: return resUncommon >= amount;
            case ResourceType.Rare: return resRare >= amount;
            // (Legendary는 아직 보관 변수가 없으므로 무조건 false 처리)
            default: return false;
        }
    }

    // 3. 특정 자원을 '차감'하는 함수 (AddResource의 반대 역할)
    public void ConsumeResource(ResourceType type, int amount) {
        switch(type) {
            case ResourceType.Common: 
                resCommon -= amount; 
                if (resCommon < 0) resCommon = 0; // 혹시 모를 마이너스 방지
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
        UpdateUI(); // 자원이 깎였으니 UI 텍스트 즉시 갱신
    }

    void UpdateUI() {
        if (txtGold != null) 
            txtGold.text = $"{currentGold} / {maxGold}";
        if (txtResourceCommon != null) 
            txtResourceCommon.text = $"{resCommon} / {maxResCommon}";
        if (txtResourceUncommon != null) 
            txtResourceUncommon.text = $"{resUncommon} / {maxResUncommon}";
        if (txtResourceRare != null) 
            txtResourceRare.text = $"{resRare} / {maxResRare}";
    }
}