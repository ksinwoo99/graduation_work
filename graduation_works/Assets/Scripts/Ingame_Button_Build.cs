using UnityEngine;
using UnityEngine.UI;
using TMPro; // 🔥 TMP를 제어하기 위해 꼭 필요한 네임스페이스

public class Ingame_Button_Build : MonoBehaviour
{
    [Header("설정")]
    public Ingame_Manager_Build buildManager; 
    
    [Header("UI 연결")]
    public TextMeshProUGUI nameText; // 🔥 기계 이름이 표시될 텍스트
    
    private Image myImage;  

    void Start()
    {
        myImage = GetComponent<Image>(); 

        // ✨ [핵심 추가] 게임이 시작될 때, 같은 오브젝트에 있는 Iteminfo_Base를 찾아서 이름을 가져옵니다.
        Iteminfo_Base info = GetComponent<Iteminfo_Base>();
        
        // info가 있고, 연결된 텍스트가 있다면 글자를 바꿔치기!
        if (info != null && nameText != null)
        {
            nameText.text = info.machineName;
        }
    }

    public void OnClick()
    {
        if (buildManager != null)
        {
            buildManager.SelectMachine(myImage);
        }
    }
}