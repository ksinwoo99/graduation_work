using UnityEngine;
using UnityEngine.Tilemaps; 

public abstract class logic_CodingBase : MonoBehaviour
{
    [Header("기본 설정")]
    public Ingame_Manager_Build buildManager; 
    public TileBase myTile; 

    [Header("상태 UI 설정 (자동할당)")]
    public SpriteRenderer statusIconRenderer; 
    
    [Header("상태 스프라이트 (3단계)")]
    public Sprite spriteRunning;  
    public Sprite spriteStopping; 
    public Sprite spriteStopped;  

    // ✨ [핵심 수정] 이제 건물을 지으면 '정지 상태'로 시작합니다!
    public bool isOperating = false; 
    public bool isStopping = false; 

    public abstract CodeState ValidateCode(string code);
    public virtual string GetDefaultCode() { return ""; }
    public enum CodeState { 
        Empty, Error, Valid, Error_LoopLocked, Error_LoopLimit, 
        Error_InfiniteLocked, Error_ConveyorLocked, Error_ConveyorFastLocked
    }
    
    public virtual string GetMachineName() {
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

    public void UpdateStatusUI() {
        if (statusIconRenderer == null) return;
        statusIconRenderer.color = Color.white; 

        if (!isOperating && isStopping) statusIconRenderer.sprite = spriteStopping; 
        else if (isOperating) statusIconRenderer.sprite = spriteRunning; 
        else statusIconRenderer.sprite = spriteStopped; 
    }
}