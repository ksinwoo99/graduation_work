using UnityEngine;
using TMPro;

public class Ingame_UI_MachineInfo : MonoBehaviour
{
    [Header("UI 텍스트 연결")]
    public TextMeshProUGUI txtMachineName;
    public TextMeshProUGUI txtSyntax;
    public TextMeshProUGUI txtCost;

    // 빌드 매니저가 기계 정보를 넘겨주며 패널을 켤 때 부르는 함수
    public void ShowInfo(Iteminfo_Base info)
    {
        if (info == null) return;
        
        gameObject.SetActive(true); // 패널 켜기
        
        if (txtMachineName != null) txtMachineName.text = info.machineName;
        if (txtSyntax != null) txtSyntax.text = info.codeSyntax;
        
        if (txtCost != null)
        {
            string costStr = "";
            
            // 1. 골드 비용 추가
            if (info.buildCost > 0) {
                costStr += $"<color=#FFD700>골드: {info.buildCost}G</color>\n";
            }
            
            // 2. 기타 자원 비용 추가
            foreach(var res in info.requiredResources) {
                string resName = GetKoreanName(res.resourceType);
                costStr += $"{resName}: {res.amount}개\n";
            }
            
            // 무료일 경우
            if (string.IsNullOrEmpty(costStr)) costStr = "무료";
            
            txtCost.text = costStr;
        }
    }

    // 설치 모드가 취소되면 패널을 끄는 함수
    public void HideInfo()
    {
        gameObject.SetActive(false);
    }

    private string GetKoreanName(ResourceType type) {
        switch (type) {
            case ResourceType.Common: return "기본 자원 (돌)";
            case ResourceType.Uncommon: return "고급 자원 (철)";
            case ResourceType.Rare: return "희귀 자원 (보석)";
            case ResourceType.Legendary: return "전설 자원";
            default: return "알 수 없음";
        }
    }
}