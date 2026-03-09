using UnityEngine;
using TMPro;

public class Ingame_UI_MachineInfo : MonoBehaviour
{
    [Header("UI 텍스트 연결")]
    public TextMeshProUGUI txtMachineName;
    public TextMeshProUGUI txtSyntax;
    public TextMeshProUGUI txtCost;

    public void ShowInfo(Iteminfo_Base info)
    {
        if (info == null) return;
        
        gameObject.SetActive(true); 
        
        if (txtMachineName != null) txtMachineName.text = info.machineName;
        if (txtSyntax != null) txtSyntax.text = info.codeSyntax;
        
        if (txtCost != null)
        {
            string costStr = "";
            
            if (info.buildCost > 0) {
                costStr += $"<color=#FFD700>골드: {info.buildCost}G</color>\n";
            }
            
            foreach(var res in info.requiredResources) {
                string resName = GetKoreanName(res.resourceType);
                costStr += $"{resName}: {res.amount}개\n";
            }
            
            if (string.IsNullOrEmpty(costStr)) costStr = "무료";
            
            txtCost.text = costStr;
        }
    }

    public void HideInfo()
    {
        gameObject.SetActive(false);
    }

    private string GetKoreanName(ResourceType type) {
        switch (type) {
            case ResourceType.Common: return "기본 자원";
            case ResourceType.Rare: return "희귀 자원";       
            case ResourceType.Special: return "특수 자원";  
            case ResourceType.Legendary: return "전설 자원";
            default: return "알 수 없음";
        }
    }
}