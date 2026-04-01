using UnityEngine;
using UnityEngine.UI;

[RequireComponent(typeof(Button))]
public class Ingame_Button_TestMode : MonoBehaviour
{
    void Start()
    {
        Button btn = GetComponent<Button>();
        if (btn != null) btn.onClick.AddListener(OnClick_TestMode);
    }

    void OnClick_TestMode()
    {
        // 💰 1. 자원 및 최대 한도 지급
        if (Ingame_Manager_Resource.Instance != null)
        {
            var resMgr = Ingame_Manager_Resource.Instance;
            
            resMgr.baseMaxGold += 2000;
            resMgr.baseMaxResCommon += 2000;
            resMgr.baseMaxResRare += 2000;
            resMgr.baseMaxResSpecial += 2000;
            resMgr.baseMaxResExotic += 2000;

            resMgr.maxGold += 2000;
            resMgr.maxResCommon += 2000;
            resMgr.maxResRare += 2000;
            resMgr.maxResSpecial += 2000;
            resMgr.maxResExotic += 2000;

            resMgr.resCommon += 2000;
            resMgr.resRare += 2000;
            resMgr.resSpecial += 2000;
            resMgr.resExotic += 2000; 

            resMgr.RefundGold(2000); 
            resMgr.EarnGold(0); 
        }

        // ✨ 2. [수정] 퀘스트 권한 강제 해금 (반복문 2, 컨베이어 1)
        if (Ingame_Manager_Quest.Instance != null)
        {
            // 반복문은 즉시 무한 루프로 뚫어줌
            Ingame_Manager_Quest.Instance.loopUpgradeLevel = 2;
            
            // 컨베이어가 아예 잠겨있을(0) 경우에만 1 (Slow)로 해금해줌
            if (Ingame_Manager_Quest.Instance.conveyorUpgradeLevel < 1) {
                Ingame_Manager_Quest.Instance.conveyorUpgradeLevel = 1;
            }
        }

        // 🔄 3. 통합 UI 최신화
        if (Ingame_UI_SystemControl.Instance != null)
        {
            Ingame_UI_SystemControl.Instance.UpdateAllUI();

            if (Ingame_UI_SystemControl.Instance.btnExpandMain != null)
                Ingame_UI_SystemControl.Instance.btnExpandMain.interactable = true;

            if (Ingame_UI_SystemControl.Instance.panelConveyorUpgrade != null)
                Ingame_UI_SystemControl.Instance.panelConveyorUpgrade.SetActive(true);

            if (Ingame_UI_SystemControl.Instance.btnUpgradeConveyor != null)
                Ingame_UI_SystemControl.Instance.btnUpgradeConveyor.interactable = true;
        }

        // 🏗️ 4. 모든 설치/철거 버튼 강제 활성화
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

        // 💬 5. 텍스트 안내
        if (Ingame_Manager_Build.Instance != null)
        {
            Vector3 screenCenter = Camera.main.ScreenToWorldPoint(new Vector3(Screen.width / 2f, Screen.height / 2f, 0));
            screenCenter.z = -5f; 
            Ingame_Manager_Build.Instance.ShowFloatingText("테스트 모드: 자원 지급 및 권한 해금!", screenCenter);
        }
    }
}