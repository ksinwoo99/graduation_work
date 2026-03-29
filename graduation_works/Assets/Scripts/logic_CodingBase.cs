using UnityEngine;
using UnityEngine.Tilemaps; 

public abstract class logic_CodingBase : MonoBehaviour
{
    [Header("기본 설정")]
    public Ingame_Manager_Build buildManager; 
    public TileBase myTile; 

    [Header("상태 UI 설정 (자동할당)")]
    public SpriteRenderer statusIconRenderer; 
    
    // ✨ [수정] 3단계 상태 이미지로 분리!
    [Header("상태 스프라이트 (3단계)")]
    public Sprite spriteRunning;  // ▶️ 실행 중 아이콘
    public Sprite spriteStopping; // ⏳ 멈추는 중 (대기) 아이콘
    public Sprite spriteStopped;  // ⏹️ 완전히 정지 아이콘

    public bool isOperating = true; 
    public bool isStopping = false; 

    public abstract CodeState ValidateCode(string code);
    public virtual string GetDefaultCode() { return ""; }
    public enum CodeState { 
        Empty, 
        Error, 
        Valid, 
        Error_LoopLocked,     // 반복문 자체 금지
        Error_LoopLimit,      // 10회 초과
        Error_InfiniteLocked,  // 무한루프 금지
        Error_ConveyorLocked,     // 아예 사용 불가
        Error_ConveyorFastLocked  // 고속 모드 사용 불가
    }
    
    public virtual string GetMachineName()
    {
        return gameObject.name.Replace("(Clone)", "").Trim();
    }

    protected virtual void Awake() {
        Transform iconTransform = transform.Find("Status_Icon");
        if (iconTransform != null) {
            statusIconRenderer = iconTransform.GetComponent<SpriteRenderer>();
        }
        UpdateStatusUI(); 
    }

    public virtual void OnMouseDown() {
        var buildMgr = Ingame_Manager_Build.Instance;
        
        if (buildMgr != null && buildMgr.isBuildMode) return;
        if (Shared_Manager_Session.IsVisiting) return;
        if (UnityEngine.EventSystems.EventSystem.current.IsPointerOverGameObject()) return;

        ToggleOperation();
    }

    public virtual void ToggleOperation() { }

    // ✨ [핵심 수정] 3단계 이미지 적용 및 색상 초기화
    public void UpdateStatusUI() {
        if (statusIconRenderer == null) return;

        // 원본 이미지의 색상을 100% 보여주기 위해 하얀색으로 세팅
        statusIconRenderer.color = Color.white; 

        if (!isOperating && isStopping) {
            // 1. 가동 중인데 정지 예약 (멈추는 중)
            statusIconRenderer.sprite = spriteStopping; 
        } else if (isOperating) {
            // 2. 정상 가동 중
            statusIconRenderer.sprite = spriteRunning; 
        } else {
            // 3. 완전히 정지 상태
            statusIconRenderer.sprite = spriteStopped; 
        }
    }
}