using UnityEngine;
using UnityEngine.SceneManagement; 

// 원래대로 든든하게 유지!
[DefaultExecutionOrder(-100)] 
public class Ingame_Manager_Menu : MonoBehaviour {
    public static Ingame_Manager_Menu Instance;
    [Header("UI 연결")]
    public GameObject PausePanel;
    public GameObject menu_SelectPanel; 
    public GameObject menu_ErrorPanel;  

    // ✨ [추가된 부분] 관전 모드일 때만 띄울 텍스트!
    [Header("관전 모드 전용 UI")]
    public GameObject visitNoticeText; 

    private bool isPaused = false;
    public bool isSaved = false; 
    
    void Awake() {
        if (Instance == null) Instance = this;
    }

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

            // ✨ [추가된 부분] ESC 메뉴가 열릴 때, 관전 모드면 글자를 켜고, 내 공장이면 끕니다!
            if (visitNoticeText != null) {
                visitNoticeText.SetActive(Shared_Manager_Session.IsVisiting);
            }

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
        if (Shared_Manager_Session.IsVisiting) {
            Shared_Manager_Session.IsVisiting = false;
            Shared_Manager_Session.VisitTargetId = "";
            Shared_Manager_Session.IsReadOnlyMode = false;

            Time.timeScale = 1f; 
            SceneManager.LoadScene("Menu_Scene"); 
            return;
        }

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