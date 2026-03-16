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
    public TextMeshProUGUI txtSizeMessage; 
    public TextMeshProUGUI txtCostMessage; 
    public GameObject buttonGroup;         
    
    [Header("팝업창 내 알림 텍스트")]
    public TextMeshProUGUI txtAlertMessage; 

    private Coroutine autoHideRoutine; 

    void Start()
    {
        UpdatePopupText();
    }

    public void UpdatePopupText()
    {
        if (Ingame_Manager_Build.Instance == null) return;

        int cost = Ingame_Manager_Build.Instance.GetCurrentExpandCost();
        int currentSize = Ingame_Manager_Build.Instance.currentMapSize;
        int nextSize = currentSize + Ingame_Manager_Build.Instance.expandSizeStep;

        if (txtSizeMessage != null)
            txtSizeMessage.text = $"<color=#00FF00>{nextSize}x{nextSize}</color>";
        
        if (txtCostMessage != null)
            txtCostMessage.text = $"<color=#FFD700>{cost} G</color>";

        if (txtAlertMessage != null)
            txtAlertMessage.text = "공장을 확장합니까?";
    }

    public void OnClick_OpenPopup()
    {
        if (Ingame_Manager_Build.Instance == null) return;

        if (autoHideRoutine != null) 
        {
            StopCoroutine(autoHideRoutine);
            autoHideRoutine = null;
        }

        if (buttonGroup != null) buttonGroup.SetActive(true);

        UpdatePopupText();

        if (popupPanel != null) popupPanel.SetActive(true);

        Time.timeScale = 0f;
    }

    public void OnClick_ConfirmExpand()
    {
        if (Ingame_Manager_Build.Instance == null) return;

        if (Ingame_Manager_Build.Instance.TryExpandMap())
        {
            Ingame_Manager_Build.Instance.ShowFloatingText("부지 확장 완료!", Vector3.zero);
            ClosePopup();
            
            bool isAllCleared = false;
            if (Ingame_Manager_Quest.Instance != null) {
                isAllCleared = Ingame_Manager_Quest.Instance.IsAllQuestsCleared();
            }

            if (!isAllCleared) {
                if (btnExpandMain != null) btnExpandMain.interactable = false;
            }

            UpdatePopupText();
        }
        else
        {
            // 실패 시 (골드 부족)
            if (buttonGroup != null) buttonGroup.SetActive(false); 
            
            // ✨ [핵심] 기존에 글자를 지우던 코드를 완전히 삭제했습니다!
            // 절대 텍스트가 사라지지 않고 그대로 유지됩니다.

            if (txtAlertMessage != null) 
                txtAlertMessage.text = "<color=#FF5A5A>골드가 부족합니다!</color>";
            
            if (autoHideRoutine != null) StopCoroutine(autoHideRoutine);
            autoHideRoutine = StartCoroutine(AutoHidePopup(5f));
        }
    }

    public void ClosePopup()
    {
        if (autoHideRoutine != null) 
        {
            StopCoroutine(autoHideRoutine);
            autoHideRoutine = null;
        }
        if (popupPanel != null) popupPanel.SetActive(false);
        
        Time.timeScale = 1f; 
    }

    IEnumerator AutoHidePopup(float seconds) 
    {
        float timer = 0f;
        yield return null; 

        while (timer < seconds) 
        {
            timer += Time.unscaledDeltaTime; 

            if (Input.GetMouseButtonDown(0) || Input.anyKeyDown) 
            {
                break;
            }
            yield return null;
        }

        ClosePopup();
    }
}