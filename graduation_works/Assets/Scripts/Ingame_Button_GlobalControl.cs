using UnityEngine;
using UnityEngine.UI;

[RequireComponent(typeof(Button))]
public class Ingame_Button_GlobalControl : MonoBehaviour
{
    [Header("버튼 역할 설정")]
    public bool isStartButton = true; // ✔️ 체크하면 [전체 가동] 버튼, 체크 해제하면 [전체 정지] 버튼이 됩니다.

    void Start()
    {
        Button btn = GetComponent<Button>();
        if (btn != null) btn.onClick.AddListener(OnClick_GlobalControl);
    }

    void OnClick_GlobalControl()
    {
        // 1. 씬에 존재하는 모든 기계(채굴기, 가공기 등)를 다 찾아냅니다.
        logic_CodingBase[] allMachines = FindObjectsOfType<logic_CodingBase>();

        int affectedCount = 0;

        foreach (var machine in allMachines)
        {
            if (isStartButton)
            {
                // [전체 가동 모드] 꺼져있는 기계만 찾아서 켭니다!
                if (!machine.isOperating) 
                {
                    machine.ToggleOperation();
                    affectedCount++;
                }
            }
            else
            {
                // [전체 정지 모드] 켜져있는 기계만 찾아서 끕니다!
                if (machine.isOperating) 
                {
                    machine.ToggleOperation();
                    affectedCount++;
                }
            }
        }

        // 시각적 피드백
        if (Ingame_Manager_Build.Instance != null && affectedCount > 0)
        {
            Vector3 center = Camera.main.ScreenToWorldPoint(new Vector3(Screen.width / 2f, Screen.height * 0.8f, 0));
            center.z = -5f;
            string msg = isStartButton ? $"{affectedCount}대의 기계 재가동!" : $"{affectedCount}대의 기계 정지 대기중...";
            Ingame_Manager_Build.Instance.ShowFloatingText(msg, center);
        }
    }
}