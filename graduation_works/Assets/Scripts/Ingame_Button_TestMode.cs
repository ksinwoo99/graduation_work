using UnityEngine;
using UnityEngine.UI;

[RequireComponent(typeof(Button))]
public class Ingame_Button_TestMode : MonoBehaviour
{
    void Start()
    {
        Button btn = GetComponent<Button>();
        if (btn != null)
        {
            btn.onClick.AddListener(OnClick_TestMode);
        }
    }

    void OnClick_TestMode()
    {
        // 💰 1. 자원 및 최대 한도 2000씩 무한 지급
        if (Ingame_Manager_Resource.Instance != null)
        {
            var resMgr = Ingame_Manager_Resource.Instance;
            
            // ✨ [핵심 수정] 건물을 새로 지어도 최대치가 초기화되지 않도록 Base 값을 먼저 올립니다.
            resMgr.baseMaxGold += 2000;
            resMgr.baseMaxResCommon += 2000;
            resMgr.baseMaxResRare += 2000;
            resMgr.baseMaxResSpecial += 2000;
            resMgr.baseMaxResExotic += 2000;

            // ✨ 에러 수정: 정확한 변수명(maxResCommon 등)으로 현재 한도를 올립니다.
            resMgr.maxGold += 2000;
            resMgr.maxResCommon += 2000;
            resMgr.maxResRare += 2000;
            resMgr.maxResSpecial += 2000;
            resMgr.maxResExotic += 2000;

            // 이제 한도가 넉넉해졌으니 자원 숫자를 올립니다.
            resMgr.resCommon += 2000;
            resMgr.resRare += 2000;
            resMgr.resSpecial += 2000;
            resMgr.resExotic += 2000; 

            // 골드는 RefundGold로 상한선에 맞게 올리고, EarnGold(0)으로 UI 텍스트를 즉시 갱신합니다.
            resMgr.RefundGold(2000); 
            resMgr.EarnGold(0); 
        }

        // 🔓 2. 시스템 권한 (반복문, 컨베이어) 최대치로 강제 해금
        if (Ingame_Manager_Quest.Instance != null)
        {
            Ingame_Manager_Quest.Instance.loopUpgradeLevel = 2;     // 무한 루프
            Ingame_Manager_Quest.Instance.conveyorUpgradeLevel = 2; // 고속 모드
        }

        // 🔄 3. UI 텍스트 즉시 갱신 (반복문, 컨베이어 상태창)
        if (Ingame_Manager_Build.Instance != null && Ingame_Manager_Build.Instance.codingManager != null)
        {
            Ingame_Manager_Build.Instance.codingManager.UpdateSystemStatusText();
        }

        // 🏗️ 4. 모든 설치물(건축) 버튼 강제 활성화
        Ingame_Button_Build[] buildButtons = FindObjectsOfType<Ingame_Button_Build>(true);
        foreach (var buildBtn in buildButtons)
        {
            Button btn = buildBtn.GetComponent<Button>();
            if (btn != null) btn.interactable = true;
        }

        logic_Demolish[] demolishBtns = FindObjectsOfType<logic_Demolish>(true);
        foreach (var demoBtn in demolishBtns)
        {
            Button btn = demoBtn.GetComponent<Button>();
            if (btn != null) btn.interactable = true;
        }

        // 💬 5. 시각적 피드백
        if (Ingame_Manager_Build.Instance != null)
        {
            Vector3 screenCenter = Camera.main.ScreenToWorldPoint(new Vector3(Screen.width / 2f, Screen.height / 2f, 0));
            screenCenter.z = -5f; 
            Ingame_Manager_Build.Instance.ShowFloatingText("테스트 모드: 골드/자원 한도 +2000 & 전체 해금!", screenCenter);
        }

        Debug.Log("[테스트 모드] 자원 지급 및 건물/권한 해금 완료!");
    }
}