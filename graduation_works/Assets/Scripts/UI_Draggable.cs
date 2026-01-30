using UnityEngine;
using UnityEngine.EventSystems;

public class UI_Draggable : MonoBehaviour, IDragHandler, IBeginDragHandler
{
    [Header("이동시킬 패널 (직접 연결하세요)")]
    public RectTransform targetPanel; 

    private Canvas canvas; // 캔버스의 스케일을 알기 위해 필요

    void Start()
    {
        // 1. 타겟 패널이 없으면 내 부모를 찾음
        if (targetPanel == null)
            targetPanel = transform.parent.GetComponent<RectTransform>();

        // 2. 캔버스 찾기 (최상위 부모 탐색)
        canvas = GetComponentInParent<Canvas>();
        
        // 3. 만약 캔버스가 없거나 타겟이 없으면 에러
        if (canvas == null || targetPanel == null)
        {
            Debug.LogError("UI_Draggable: 캔버스나 타겟 패널을 찾을 수 없습니다!");
        }
    }

    public void OnBeginDrag(PointerEventData eventData)
    {
        // 드래그 시작 시 할 일 (필요하면 추가)
    }

    public void OnDrag(PointerEventData eventData)
    {
        if (targetPanel == null || canvas == null) return;

        // 🔥 핵심 수정: 델타(이동량)를 캔버스 스케일로 나눠서 적용
        // 이렇게 해야 해상도가 바뀌어도 마우스랑 딱 붙어서 움직임
        targetPanel.anchoredPosition += eventData.delta / canvas.scaleFactor;
    }
}