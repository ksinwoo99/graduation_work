using UnityEngine;
using TMPro;

public class Ingame_UI_ExpandMap : MonoBehaviour
{
    [Header("UI 연결")]
    public GameObject expandPopupPanel;    // 확장 확인 팝업창 (배경 패널)
    public TextMeshProUGUI txtCostMessage; // 확장 안내 및 비용 텍스트

    // 하단 메뉴의 [부지 확장] 버튼을 눌렀을 때 팝업 열기
    public void OnClick_OpenExpandPopup()
    {
        if (Ingame_Manager_Build.Instance == null) return;

        // 현재 얼마가 필요한지, 크기는 어떻게 변하는지 가져옵니다.
        int cost = Ingame_Manager_Build.Instance.GetCurrentExpandCost();
        int currentSize = Ingame_Manager_Build.Instance.currentMapSize;
        int nextSize = currentSize + Ingame_Manager_Build.Instance.expandSizeStep;

        // 팝업창 텍스트 업데이트
        if (txtCostMessage != null)
        {
            txtCostMessage.text = $"현재 크기: {currentSize}x{currentSize}\n다음 크기: <color=#00FF00>{nextSize}x{nextSize}</color>\n\n<color=#FFD700>{cost} G</color>를 지불하고\n부지를 확장하시겠습니까?";
        }

        if (expandPopupPanel != null) expandPopupPanel.SetActive(true);
    }

    // 팝업창에서 [예(확장)] 버튼을 눌렀을 때
    public void OnClick_ConfirmExpand()
    {
        if (Ingame_Manager_Build.Instance == null) return;

        // 골드가 충분해서 확장에 성공했다면 팝업 닫기
        if (Ingame_Manager_Build.Instance.TryExpandMap())
        {
            ClosePopup();
            // 맵 중앙에 성공 메시지 띄우기
            Ingame_Manager_Build.Instance.ShowFloatingText("부지 확장 완료!", Vector3.zero);
        }
        else
        {
            // 골드 부족 텍스트 띄우기
            Ingame_Manager_Build.Instance.ShowFloatingText("골드가 부족합니다!", Vector3.zero);
        }
    }

    // 팝업창에서 [아니오(취소)] 버튼을 눌렀을 때
    public void ClosePopup()
    {
        if (expandPopupPanel != null) expandPopupPanel.SetActive(false);
    }
}