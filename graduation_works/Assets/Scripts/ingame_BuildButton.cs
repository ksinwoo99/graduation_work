using UnityEngine;
using UnityEngine.UI;
using UnityEngine.Tilemaps;

public class Ingame_BuildButton : MonoBehaviour
{
    [Header("설정")]
    public Ingame_BuildManager buildManager; // 매니저에게 연락해야 하니까
    public TileBase myTile; // 나는 채굴기 타일이다! (여기에 타일 넣기)
    
    private Image myImage;  // 내 버튼 색깔 바꿀 이미지

    void Start()
    {
        myImage = GetComponent<Image>(); // 내 몸에 붙은 이미지 컴포넌트 찾기
    }

    // 버튼을 누르면 이 함수가 실행됨
    public void OnClick()
    {
        // 매니저에게 "나(타일)랑 내 얼굴(이미지) 보낼게, 나를 선택해줘!" 라고 요청
        buildManager.SelectMachine(myTile, myImage);
    }
}