using UnityEngine;
using UnityEngine.UI;
using UnityEngine.Tilemaps; // 이건 이제 안 쓰지만 혹시 몰라 남겨둠

public class Ingame_Button_Build : MonoBehaviour
{
    [Header("설정")]
    public Ingame_Manager_Build buildManager; 
    
    private Image myImage;  

    void Start()
    {
        myImage = GetComponent<Image>(); 
    }

    public void OnClick()
    {
        if (buildManager != null)
        {
            // 🔥 [수정] 인자를 2개(myTile, myImage)에서 -> 1개(myImage)로 변경!
            buildManager.SelectMachine(myImage);
        }
    }
}