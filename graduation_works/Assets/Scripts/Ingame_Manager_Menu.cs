using UnityEngine;
using UnityEngine.SceneManagement; 

// ✨ [핵심 추가] 빌드 로직보다 무조건 먼저 ESC 키를 판별하도록 순서를 강제 고정!
[DefaultExecutionOrder(-100)] 
public class Ingame_Manager_Menu : MonoBehaviour {
    [Header("UI 연결")]
    public GameObject PausePanel;       
    public GameObject menu_SelectPanel; 
    public GameObject menu_ErrorPanel;  

    private bool isPaused = false;
    private bool isSaved = false;       

    void Start() {
        Time.timeScale = 1f; 
        if(menu_SelectPanel != null) menu_SelectPanel.SetActive(false);
        if(PausePanel != null) PausePanel.SetActive(false);
        if(menu_ErrorPanel != null) menu_ErrorPanel.SetActive(false);
    }

    void Update() {
        if (Input.GetKeyDown(KeyCode.Escape)) {
            if (menu_ErrorPanel != null && menu_ErrorPanel.activeSelf) {
                OnClick_ConfirmNo(); 
                return; 
            }

            if (Ingame_Manager_Build.Instance != null) {
                bool isConfirming = Ingame_Manager_Build.Instance.confirmPanel != null && Ingame_Manager_Build.Instance.confirmPanel.activeSelf;
                
                // 설치 모드이거나 저장 확인창이 떠있으면 메뉴창 안 띄우고 무시!
                if (Ingame_Manager_Build.Instance.isBuildMode || isConfirming) {
                    return; 
                }
            }

            OnClick_ToggleMenu();
        }
    }

    public void OnClick_ToggleMenu() {
        if (menu_ErrorPanel != null && menu_ErrorPanel.activeSelf) return; 

        isPaused = !isPaused;

        if (isPaused) {
            Time.timeScale = 0f; 
            PausePanel.SetActive(true);
            menu_SelectPanel.SetActive(true);
        } else {
            Time.timeScale = 1f; 
            PausePanel.SetActive(false);
            menu_SelectPanel.SetActive(false);
        }
    }

    public void OnClick_Save() { 
        if(Ingame_System_Save.Instance != null) {
            Ingame_System_Save.Instance.OnClick_Save();
        }
        isSaved = true; 
        Debug.Log("저장 요청됨"); 
    }
    
    public void OnClick_Load() { 
        if(Ingame_System_Save.Instance != null) {
            Ingame_System_Save.Instance.OnClick_Load();
        }
        OnClick_ToggleMenu(); 
    }

    public void OnClick_Exit() {
        if (isSaved) {
            RealExit();
        } else {
            if(menu_ErrorPanel != null) menu_ErrorPanel.SetActive(true);
        }
    }

    public void OnClick_ConfirmYes() {
        RealExit();
    }

    public void OnClick_ConfirmNo() {
        if(menu_ErrorPanel != null) menu_ErrorPanel.SetActive(false);
    }

    void RealExit() {
        Time.timeScale = 1f; 
        SceneManager.LoadScene("Menu_Scene");
    }
}