using UnityEngine;
using UnityEngine.SceneManagement; 

public class ingame_MenuManager : MonoBehaviour 
{
    [Header("UI 연결")]
    public GameObject PausePanel;       
    public GameObject menu_SelectPanel; 
    public GameObject menu_ErrorPanel;  

    private bool isPaused = false;
    private bool isSaved = false;       

    void Start()
    {
        Time.timeScale = 1f; 
        if(menu_SelectPanel != null) menu_SelectPanel.SetActive(false);
        if(PausePanel != null) PausePanel.SetActive(false);
        if(menu_ErrorPanel != null) menu_ErrorPanel.SetActive(false);
    }

    void Update()
    {
        // ESC 키 처리
        if (Input.GetKeyDown(KeyCode.Escape))
        {
            // 1. 팝업이 켜져있으면 -> 팝업을 끈다 (아니오 버튼과 동일)
            if (menu_ErrorPanel != null && menu_ErrorPanel.activeSelf)
            {
                OnClick_ConfirmNo(); 
                return; 
            }
            
            // 2. 팝업이 없을 때만 메뉴 토글
            OnClick_ToggleMenu();
        }
    }

    // ==========================================
    // 1. 메뉴 열기/닫기 (수정됨 ⭐)
    // ==========================================
    public void OnClick_ToggleMenu()
    {
        // 🔥 [철벽 방어] 팝업이 떠 있다면, 메뉴 버튼을 눌러도 무시한다!
        if (menu_ErrorPanel != null && menu_ErrorPanel.activeSelf)
        {
            return; 
        }

        isPaused = !isPaused;

        if (isPaused)
        {
            Debug.Log("⏸ 일시정지");
            Time.timeScale = 0f; 
            PausePanel.SetActive(true);
            menu_SelectPanel.SetActive(true);
        }
        else
        {
            Debug.Log("▶ 재개");
            Time.timeScale = 1f; 
            PausePanel.SetActive(false);
            menu_SelectPanel.SetActive(false);
        }
    }

    // ... (저장, 불러오기 코드는 동일) ...
    public void OnClick_Save() { isSaved = true; Debug.Log("저장됨"); }
    public void OnClick_Load() { Debug.Log("불러오기"); }

    // ==========================================
    // 4. 종료하기
    // ==========================================
    public void OnClick_Exit()
    {
        if (isSaved == true)
        {
            RealExit();
        }
        else
        {
            // 팝업 띄우기
            if(menu_ErrorPanel != null) 
            {
                menu_ErrorPanel.SetActive(true);
                // 팝업이 뜨는 순간, 화면 전체를 덮는 Image가 뒤쪽 클릭을 다 막아줍니다.
            }
        }
    }

    // [네] 버튼
    public void OnClick_ConfirmYes()
    {
        RealExit();
    }

    // [아니오] 버튼
    public void OnClick_ConfirmNo()
    {
        if(menu_ErrorPanel != null) menu_ErrorPanel.SetActive(false);
    }

    void RealExit()
    {
        Time.timeScale = 1f; 
        SceneManager.LoadScene("Menu_Scene");
    }
}