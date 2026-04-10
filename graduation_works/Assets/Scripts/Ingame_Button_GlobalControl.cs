using UnityEngine;
using UnityEngine.UI;

[RequireComponent(typeof(Button))]
public class Ingame_Button_GlobalControl : MonoBehaviour
{
    [Header("버튼 역할 설정")]
    public bool isStartButton = true; 

    void Start()
    {
        Button btn = GetComponent<Button>();
        if (btn != null) btn.onClick.AddListener(OnClick_GlobalControl);
    }

    void OnClick_GlobalControl()
    {
        logic_CodingBase[] allMachines = FindObjectsOfType<logic_CodingBase>();
        int affectedCount = 0;

        foreach (var machine in allMachines)
        {
            // ✨ [핵심 추가] 창고나 판매소(logic_Storage)는 무조건 패스합니다!
            if (machine is logic_Storage) continue;

            if (isStartButton)
            {
                if (!machine.isOperating) 
                {
                    machine.ToggleOperation();
                    affectedCount++;
                }
            }
            else
            {
                if (machine.isOperating) 
                {
                    machine.ToggleOperation();
                    affectedCount++;
                }
            }
        }

        if (Ingame_Manager_Build.Instance != null && affectedCount > 0)
        {
            Vector3 center = Camera.main.ScreenToWorldPoint(new Vector3(Screen.width / 2f, Screen.height * 0.8f, 0));
            center.z = -5f;
            string msg = isStartButton ? $"{affectedCount}대의 기계 재가동!" : $"{affectedCount}대의 기계 정지 대기중...";
            Ingame_Manager_Build.Instance.ShowFloatingText(msg, center);
        }

        // =========================================================
        // ✨ [튜토리얼 연동] 전체 재가동 버튼 클릭 감지!
        // =========================================================
        if (isStartButton && Ingame_UI_Tutorial.Instance != null && Ingame_UI_Tutorial.Instance.isTutorialActive)
        {
            Ingame_UI_Tutorial.Instance.TriggerMinerRestarted();
        }
    }
}