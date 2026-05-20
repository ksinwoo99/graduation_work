using UnityEngine;
using TMPro;

public class Ingame_Manager_Time : MonoBehaviour {
    public static Ingame_Manager_Time Instance;

    [Header("UI 연결")]
    public TextMeshProUGUI txtTime;

    [Header("상태 확인용")]
    public float gameTime = 0f;
    public bool isPaused = false;

    void Awake() {
        if (Instance == null) Instance = this;
    }

    void Update() {
        // 1. 현재 시간이 멈춰야 하는 상태인지 확인
        bool isStopped = isPaused || IsBuildOrCodingMode();

        if (Ingame_UI_Tutorial.Instance.skipPanel != null && Ingame_UI_Tutorial.Instance.skipPanel.activeSelf)
        {
            isStopped = true;
        }

        // 튜토리얼이 켜져 있고, 액션 모드가 아니라면 시간 정지
        if (Ingame_UI_Tutorial.Instance != null && 
            Ingame_UI_Tutorial.Instance.isTutorialActive && 
            !Ingame_UI_Tutorial.Instance.isActionMode) 
        {
            isStopped = true;
        }

        // 🔥 상태에 따라 텍스트 색상 변경
        if (txtTime != null) {
            // 멈췄으면 회색(Gray), 흐르고 있으면 흰색(White)
            txtTime.color = isStopped ? Color.gray : Color.white;
        }

        // 2. 멈춘 상태라면 시간 증가 로직 건너뜀
        if (isStopped) return;

        // 3. 시간 증가
        gameTime += Time.deltaTime;
        UpdateTimerUI();
    }

    bool IsBuildOrCodingMode() {
        if (Ingame_Manager_Build.Instance != null) {
            return Ingame_Manager_Build.Instance.isBuildMode;
        }
        return false;
    }

    void UpdateTimerUI() {
        if (txtTime != null) {
            int minutes = Mathf.FloorToInt(gameTime / 60F);
            int seconds = Mathf.FloorToInt(gameTime % 60F);
            txtTime.text = string.Format("{0:00}:{1:00}", minutes, seconds);
        }
    }
    
    public void SetPause(bool pause) {
        isPaused = pause;
    }
}