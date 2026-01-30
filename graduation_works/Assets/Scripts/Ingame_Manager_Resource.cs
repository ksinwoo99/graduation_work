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
    public int currentGold = 500;
    public int maxGold = 1000;

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