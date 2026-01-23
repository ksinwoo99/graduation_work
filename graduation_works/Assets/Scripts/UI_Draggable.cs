using UnityEngine;
using UnityEngine.EventSystems;

public class UI_Draggable : MonoBehaviour, IDragHandler, IBeginDragHandler
{
    private RectTransform targetPanel; // 움직일 패널
    private Vector2 offset;

    void Start()
    {
        // 내 부모 중에 Panel_GlobalCoding을 찾아서 타겟으로 잡음
        targetPanel = GetComponentInParent<Canvas>().transform.Find("Panel_GlobalCoding") as RectTransform;
        // 만약 못 찾으면 내 부모를 타겟으로 (유동적)
        if(targetPanel == null) targetPanel = transform.parent as RectTransform;
    }

    public void OnBeginDrag(PointerEventData eventData)
    {
        RectTransformUtility.ScreenPointToLocalPointInRectangle(
            targetPanel, eventData.position, eventData.pressEventCamera, out offset);
    }

    public void OnDrag(PointerEventData eventData)
    {
        Vector2 localPoint;
        if (RectTransformUtility.ScreenPointToLocalPointInRectangle(
            targetPanel.parent as RectTransform, eventData.position, eventData.pressEventCamera, out localPoint))
        {
            // 오프셋을 고려해 위치 이동
            targetPanel.localPosition = localPoint - offset;
        }
    }
}