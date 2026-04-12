using UnityEngine;
using UnityEngine.EventSystems;

public class Ingame_Manager_Camera : MonoBehaviour
{
    private Camera cam;

    [Header("카메라 줌 설정")]
    public float zoomSpeed = 4f;
    public float minZoom = 3f;
    public float maxZoom = 15f;

    [Header("카메라 이동 제한 구역 (타일맵 크기에 맞춰 수정)")]
    public Vector2 mapMin = new Vector2(-20f, -20f);
    public Vector2 mapMax = new Vector2(20f, 20f);

    private Vector3 dragOrigin;
    
    //Z축을 고정
    private float fixedZ; 

    void Awake()
    {
        cam = Camera.main;
        if (cam == null)
        {
            Debug.LogError("Main Camera를 찾을 수 없습니다. 카메라에 MainCamera 태그가 있는지 확인하세요.");
        }
        else 
        {
            //게임 시작할 때 카메라의 원래 Z값(-10)을 기억해둠
            fixedZ = cam.transform.position.z; 
        }
    }

    void LateUpdate()
    {
        if (cam == null) return;

        HandleZoom();
        HandlePan();
    }

    void HandleZoom()
    {
        // 컨트롤(Ctrl) 키를 누르고 있다면, 코딩창 확대/축소 중이므로 카메라 줌을 완벽히 차단합니다!
        if (Input.GetKey(KeyCode.LeftControl) || Input.GetKey(KeyCode.RightControl)) return;

        float scroll = Input.GetAxis("Mouse ScrollWheel");
        if (scroll != 0f)
        {
            if (EventSystem.current.IsPointerOverGameObject()) return;

            cam.orthographicSize -= scroll * zoomSpeed;
            cam.orthographicSize = Mathf.Clamp(cam.orthographicSize, minZoom, maxZoom);
            
            ClampCameraPosition();
        }
    }

    void HandlePan()
    {
        // ✨ [핵심 추가] 튜토리얼 중이고, 액션 모드가 아닐 때는 우클릭 카메라 이동을 막습니다!
        if (Ingame_UI_Tutorial.Instance != null && 
            Ingame_UI_Tutorial.Instance.isTutorialActive && 
            !Ingame_UI_Tutorial.Instance.isActionMode)
        {
            return; 
        }

        if (Input.GetMouseButtonDown(1))
        {
            if (EventSystem.current.IsPointerOverGameObject()) return;
            dragOrigin = cam.ScreenToWorldPoint(Input.mousePosition);
            dragOrigin.z = fixedZ; 
        }

        if (Input.GetMouseButton(1))
        {
            Vector3 currentPos = cam.ScreenToWorldPoint(Input.mousePosition);
            currentPos.z = fixedZ; 

            Vector3 difference = dragOrigin - currentPos;
            cam.transform.position += difference;
            
            ClampCameraPosition();
        }
    }

    void ClampCameraPosition()
    {
        float camHeight = cam.orthographicSize;
        float camWidth = camHeight * cam.aspect;

        float minX = mapMin.x + camWidth;
        float maxX = mapMax.x - camWidth;
        float minY = mapMin.y + camHeight;
        float maxY = mapMax.y - camHeight;

        if (maxX < minX) { float temp = minX; minX = maxX; maxX = temp; }
        if (maxY < minY) { float temp = minY; minY = maxY; maxY = temp; }

        Vector3 clampedPos = cam.transform.position;
        clampedPos.x = Mathf.Clamp(clampedPos.x, minX, maxX);
        clampedPos.y = Mathf.Clamp(clampedPos.y, minY, maxY);
        
        clampedPos.z = fixedZ; 
        
        cam.transform.position = clampedPos;
    }
}