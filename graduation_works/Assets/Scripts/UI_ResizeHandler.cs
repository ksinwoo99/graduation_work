using UnityEngine;
using UnityEngine.EventSystems;

public class UI_ResizeHandler : MonoBehaviour, IDragHandler
{
    [Header("크기 조절할 패널 (직접 연결하세요)")]
    public RectTransform targetPanel; 

    [Header("제한 설정")]
    public Vector2 minSize = new Vector2(400, 300);
    public Vector2 maxSize = new Vector2(1600, 1200);

    private Canvas canvas;

    void Start()
    {
        // 1. 타겟 자동 찾기 (혹시 안 넣었을 때 대비)
        if (targetPanel == null)
            targetPanel = transform.parent.GetComponent<RectTransform>();

        // 2. 캔버스 찾기
        canvas = GetComponentInParent<Canvas>();
    }

    public void OnDrag(PointerEventData eventData)
    {
        if (targetPanel == null || canvas == null) return;

        // 🔥 핵심 수정: 마우스 이동량만큼 크기 더하기
        // 마우스를 오른쪽(+)으로 가면 너비 증가
        // 마우스를 아래쪽(-)으로 가면 높이 증가 (그래서 y는 뺌)
        Vector2 currentSize = targetPanel.sizeDelta;
        
        currentSize.x += eventData.delta.x / canvas.scaleFactor;
        currentSize.y -= eventData.delta.y / canvas.scaleFactor; // 아래로 내리면 y좌표는 줄어드니까 뺌

        // 크기 제한 적용
        currentSize.x = Mathf.Clamp(currentSize.x, minSize.x, maxSize.x);
        currentSize.y = Mathf.Clamp(currentSize.y, minSize.y, maxSize.y);

        targetPanel.sizeDelta = currentSize;
    }
}