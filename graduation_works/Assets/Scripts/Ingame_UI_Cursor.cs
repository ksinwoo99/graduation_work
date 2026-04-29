using UnityEngine;
using UnityEngine.EventSystems;

// 마우스가 UI나 콜라이더 위에 올라갔는지 감지하는 인터페이스 사용
public class UI_CursorHandler : MonoBehaviour, IPointerEnterHandler, IPointerExitHandler
{
    [Header("변경할 커서 이미지")]
    public Texture2D customCursor;
    
    [Header("클릭 기준점 (Hotspot)")]
    // 화살표는 (0,0), I자(텍스트) 모양은 보통 이미지의 중앙 (예: X:16, Y:16)으로 맞춥니다.
    public Vector2 hotspot = Vector2.zero; 

    // 마우스가 이 오브젝트 위에 올라왔을 때
    public void OnPointerEnter(PointerEventData eventData)
    {
        if (customCursor != null)
        {
            Cursor.SetCursor(customCursor, hotspot, CursorMode.Auto);
        }
    }

    // 마우스가 이 오브젝트 밖으로 나갔을 때
    public void OnPointerExit(PointerEventData eventData)
    {
        // null을 넣으면 아까 2단계에서 설정한 '기본 커서(Cursor_Default)'로 알아서 돌아갑니다!
        Cursor.SetCursor(null, Vector2.zero, CursorMode.Auto);
    }
}