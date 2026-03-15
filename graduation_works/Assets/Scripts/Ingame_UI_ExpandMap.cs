using UnityEngine;
using UnityEngine.UI;
using TMPro;
using System.Collections;

public class Ingame_UI_ExpandMap : MonoBehaviour
{
    [Header("메인 UI")]
    public Button btnExpandMain; 

    [Header("팝업창 UI")]
    public GameObject popupPanel;           
    
    // ✨ [수정 1] 텍스트를 크기용과 비용용으로 분리했습니다.
    public TextMeshProUGUI txtSizeMessage; 
    public TextMeshProUGUI txtCostMessage; 
    
    // ✨ [추가] 골드 부족 시 '예/아니오' 버튼을 한 번에 숨기기 위한 그룹 객체
    public GameObject buttonGroup;         

    private Coroutine hideRoutine; // 자동 닫기 코루틴

    public void OnClick_OpenPopup()
    {
        if (Ingame_Manager_Build.Instance == null) return;

        // 팝업 열 때 예/아니오 버튼 그룹 다시 활성화
        if (buttonGroup != null) buttonGroup.SetActive(true);

        int cost = Ingame_Manager_Build.Instance.GetCurrentExpandCost();
        int currentSize = Ingame_Manager_Build.Instance.currentMapSize;
        int nextSize = currentSize + Ingame_Manager_Build.Instance.expandSizeStep;

        // 텍스트 분리해서 출력
        if (txtSizeMessage != null)
            txtSizeMessage.text = $"현재 크기: {currentSize}x{currentSize}\n다음 크기: <color=#00FF00>{nextSize}x{nextSize}</color>";
        
        if (txtCostMessage != null)
            txtCostMessage.text = $"<color=#FFD700>{cost} G</color>를 지불하고\n부지를 확장하시겠습니까?";

        if (popupPanel != null) popupPanel.SetActive(true);

        // ✨ [수정 2] 게임 일시정지 (시간 및 모든 기계 동작 멈춤)
        Time.timeScale = 0f;
    }

    public void OnClick_ConfirmExpand()
    {
        if (Ingame_Manager_Build.Instance == null) return;

        if (Ingame_Manager_Build.Instance.TryExpandMap())
        {
            // 성공 시
            Ingame_Manager_Build.Instance.ShowFloatingText("부지 확장 완료!", Vector3.zero);
            ClosePopup();
            
            if (btnExpandMain != null) btnExpandMain.interactable = false;
        }
        else
        {
            // ✨ [수정 3] 실패 (골드 부족) 시 로직
            if (buttonGroup != null) buttonGroup.SetActive(false); // 예/아니오 버튼 숨기기
            
            // 텍스트 변경
            if (txtSizeMessage != null) txtSizeMessage.text = "<color=#FF5A5A>골드가 부족합니다!</color>";
            if (txtCostMessage != null) txtCostMessage.text = "더 많은 골드를 모아오세요.";

            // 5초 대기 후 자동 닫기 코루틴 시작
            if (hideRoutine != null) StopCoroutine(hideRoutine);
            hideRoutine = StartCoroutine(AutoHide(5f));
        }
    }

    public void ClosePopup()
    {
        // 코루틴이 돌고 있다면 정지
        if (hideRoutine != null) 
        {
            StopCoroutine(hideRoutine);
            hideRoutine = null;
        }
        
        if (popupPanel != null) popupPanel.SetActive(false);

        // ✨ [수정 2] 팝업이 닫히면 게임 시간(동작) 다시 정상화
        Time.timeScale = 1f;
    }

    // ✨ [수정 3] 5초 대기 또는 아무 입력 감지 시 창 닫기
    IEnumerator AutoHide(float seconds) 
    {
        float timer = 0f;
        yield return null; // 클릭하는 순간 바로 꺼지는 것을 방지하기 위해 한 프레임 대기

        while (timer < seconds) 
        {
            // Time.timeScale이 0일 때 일반 Time.deltaTime은 작동하지 않으므로, unscaledDeltaTime을 사용해야 현실 시간으로 5초를 잴 수 있습니다!
            timer += Time.unscaledDeltaTime; 

            // 클릭, 엔터, ESC 누르면 루프 탈출
            if (Input.GetMouseButtonDown(0) || 
                Input.GetKeyDown(KeyCode.Return) || 
                Input.GetKeyDown(KeyCode.KeypadEnter) || 
                Input.GetKeyDown(KeyCode.Escape)) 
            {
                break;
            }
            yield return null;
        }

        ClosePopup();
    }
}