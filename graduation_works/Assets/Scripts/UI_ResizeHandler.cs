using UnityEngine;
using UnityEngine.EventSystems;

public class UI_ResizeHandler : MonoBehaviour, IDragHandler, IPointerDownHandler
{
    public RectTransform targetPanel; // Panel_GlobalCoding 연결
    public Vector2 minSize = new Vector2(400, 300);
    public Vector2 maxSize = new Vector2(1000, 800);

    private Vector2 originalLocalPointerPosition;
    private Vector2 originalSizeDelta;

    public void OnPointerDown(PointerEventData data)
    {
        originalSizeDelta = targetPanel.sizeDelta;
        RectTransformUtility.ScreenPointToLocalPointInRectangle(targetPanel, data.position, data.pressEventCamera, out originalLocalPointerPosition);
    }

    public void OnDrag(PointerEventData data)
    {
        Vector2 localPointerPosition;
        RectTransformUtility.ScreenPointToLocalPointInRectangle(targetPanel, data.position, data.pressEventCamera, out localPointerPosition);

        Vector2 diff = localPointerPosition - originalLocalPointerPosition;

        float newWidth = Mathf.Clamp(originalSizeDelta.x + diff.x, minSize.x, maxSize.x);
        float newHeight = Mathf.Clamp(originalSizeDelta.y - diff.y, minSize.y, maxSize.y);

        targetPanel.sizeDelta = new Vector2(newWidth, newHeight);
    }
}