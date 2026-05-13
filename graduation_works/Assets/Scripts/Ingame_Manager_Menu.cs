using UnityEngine;
using UnityEngine.SceneManagement; 
using TMPro;
using System.Collections;

[DefaultExecutionOrder(-100)] 
public class Ingame_Manager_Menu : MonoBehaviour {
    public static Ingame_Manager_Menu Instance;
    
    [Header("UI 연결")]
    public GameObject PausePanel;
    public GameObject menu_SelectPanel; 
    public GameObject menu_ErrorPanel;

    public TextMeshProUGUI errorText;
    public GameObject infoWindow;
    public TextMeshProUGUI infoText;


    [Header("메뉴 상태 텍스트")]
    public TextMeshProUGUI statusText; 

    private bool isPaused = false;
    [HideInInspector] public bool isSaved = false; 


    private enum PendingAction { None, Exit, Load }
    private PendingAction currentAction = PendingAction.None;
    private System.Collections.Generic.Dictionary<GameObject, Coroutine> hideRoutines = new System.Collections.Generic.Dictionary<GameObject, Coroutine>();
    
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

    public void ShowInfoWindow(string msg, bool autoHide = true) {
        if (infoWindow == null || infoText == null) return;
        infoText.text = msg;
        infoWindow.SetActive(true);
        
        // 해당 패널로 이미 실행 중인 소멸 코루틴이 있다면 완벽히 추적하여 중지
        if (hideRoutines.TryGetValue(infoWindow, out var running) && running != null) {
            StopCoroutine(running);
        }

        if (autoHide) {
            hideRoutines[infoWindow] = StartCoroutine(AutoHideInfo(infoWindow, 3f));
        } else {
            hideRoutines[infoWindow] = null;
        }
    }

    IEnumerator AutoHideInfo(GameObject panel, float seconds) {
        float timer = 0f;
        yield return null; // 버튼을 누른 동일 프레임 클릭 감지로 바로 꺼지는 현상 방지

        while (timer < seconds) {
            timer += Time.unscaledDeltaTime; // Time.timeScale = 0f 상태에서도 정상 타이머 흐름 유지

            // 3초가 지나기 전이라도 마우스 클릭이나 아무 키나 입력되면 즉시 소멸
            if (Input.GetMouseButtonDown(0) || Input.anyKeyDown) {
                break; 
            }
            yield return null;
        }

        if (panel != null) panel.SetActive(false);
        hideRoutines[panel] = null; // 초기화
    }

    IEnumerator Co_HideInfo(float seconds) {
        float timer = 0f;
        // 저장 버튼을 누른 마우스 클릭 입력이 동시 인식되어 창이 바로 닫히는 현상 방지
        yield return null; 

        while (timer < seconds) {
            timer += Time.unscaledDeltaTime; // 일시정지 상태이므로 unscaledDeltaTime 필수

            // 화면을 클릭하거나 마우스/키보드 아무 입력이나 들어오면 즉시 루프 탈출
            if (Input.GetMouseButtonDown(0) || Input.anyKeyDown) {
                break; 
            }
            yield return null;
        }
        infoWindow.SetActive(false);
    }

    public void OnClick_ToggleMenu() {
        if (menu_ErrorPanel != null && menu_ErrorPanel.activeSelf) return;
        isPaused = !isPaused;

        if (isPaused) {
            Time.timeScale = 0f;
            PausePanel.SetActive(true);
            menu_SelectPanel.SetActive(true);

            if (statusText != null) {
                if (Shared_Manager_Session.IsVisiting) {
                    statusText.text = "다른 플레이어의 공장에\n놀러왔습니다!";
                } else {
                    statusText.text = "종료하기 전,\n저장은 필수!";
                }
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
    }
    
    public void OnClick_Load() { 
        if (Ingame_System_Save.Instance == null) return;

        int status = Ingame_System_Save.Instance.GetDirtyStatus();
        if (status != 0) {
            currentAction = PendingAction.Load;
            string msg = "";

            if (status == 1) {
                msg = "설치물 또는 코드가 저장되지 않았습니다.\n그래도 불러오시겠습니까?";
            } else if (status == 2) {
                int seconds = (int)Ingame_System_Save.Instance.GetSecondsSinceLastSave();
                msg = $"{seconds}초의 자원이 저장되지 않았습니다.\n그래도 불러오시겠습니까?";
            }
            OpenConfirm(msg);
        } else {
            Ingame_System_Save.Instance.OnClick_Load();
            OnClick_ToggleMenu();
        }
    }

    public void OnClick_Exit() {
        if (Shared_Manager_Session.IsVisiting) { RealExit(); return; }
        
        if (Ingame_System_Save.Instance != null) {
            int status = Ingame_System_Save.Instance.GetDirtyStatus();
            currentAction = PendingAction.Exit;
            string msg = "";

            if (status == 1) {
                msg = "저장되지 않았습니다.\n그래도 종료하시겠습니까?";
            } else if (status == 2) {
                int seconds = (int)Ingame_System_Save.Instance.GetSecondsSinceLastSave();
                msg = $"{seconds}초 이상 저장되지 않았습니다.\n그래도 종료하시겠습니까?";
            } else {
                msg = "종료하시겠습니까?";
            }
            OpenConfirm(msg);
        } else {
            RealExit();
        }
    }

    private void OpenConfirm(string msg) {
        if (errorText != null) errorText.text = msg;
        if (menu_ErrorPanel != null) menu_ErrorPanel.SetActive(true);
    }

    public void OnClick_ConfirmYes() {
        if (currentAction == PendingAction.Exit) RealExit();
        else if (currentAction == PendingAction.Load && Ingame_System_Save.Instance != null) { 
            Ingame_System_Save.Instance.OnClick_Load(); 
            OnClick_ToggleMenu(); 
        }
        
        currentAction = PendingAction.None;
        if (menu_ErrorPanel != null) menu_ErrorPanel.SetActive(false);
    }

    public void OnClick_ConfirmNo() {
        currentAction = PendingAction.None;
        if(menu_ErrorPanel != null) menu_ErrorPanel.SetActive(false);
    }

    void RealExit() {
        Time.timeScale = 1f; 
        SceneManager.LoadScene("Menu_Scene");
    }
}