using UnityEngine;
using UnityEngine.SceneManagement; 

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
        // 실제 저장은 Ingame_System_Save에서 처리하므로 여기선 UI 처리만
        if(Ingame_System_Save.Instance != null) Ingame_System_Save.Instance.OnClick_Save();
        isSaved = true; 
        Debug.Log("저장 요청됨"); 
    }
    
    public void OnClick_Load() { Debug.Log("불러오기"); }

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