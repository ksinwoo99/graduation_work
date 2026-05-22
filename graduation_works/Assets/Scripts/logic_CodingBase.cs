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

    public bool isOperating = false; 
    public bool isStopping = false; 

    public abstract CodeState ValidateCode(string code);
    public virtual string GetDefaultCode() { return ""; }
    public enum CodeState { 
        Empty, Error, Valid, Error_LoopLocked, Error_LoopLimit, 
        Error_InfiniteLocked, Error_ConveyorLocked, Error_ConveyorFastLocked,
        Error_WrongMachineSyntax
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
        
        // ✨ 빌드(설치) 모드이거나 철거 모드일 때는 클릭 무시 (설치/철거가 우선)
        if (buildMgr != null && buildMgr.isBuildMode) return;
        
        if (Shared_Manager_Session.IsVisiting) return;
        if (UnityEngine.EventSystems.EventSystem.current.IsPointerOverGameObject()) return;

        // ✨ [수정] 개별 기계를 클릭해서 정지/가동하는 로직 제거
        // ToggleOperation(); 

        // ✨ [신규] 기계를 클릭하면, 하단 메뉴 버튼을 클릭한 것과 동일한 로직 실행!
        OpenMyCodingWindow();
    }

    private void OpenMyCodingWindow() {
        // 내 프리팹 원본 이름 추출 (예: "Miner_Common(Clone)" -> "Miner_Common")
        string myPrefabName = gameObject.name.Replace("(Clone)", "").Trim();

        // 씬(하단 UI 패널)에 있는 모든 설치물 정보(Iteminfo_Base)를 뒤져서 나랑 일치하는 버튼을 찾습니다.
        Iteminfo_Base[] allInfos = FindObjectsOfType<Iteminfo_Base>(true);
        foreach (var info in allInfos) {
            if (info.machinePrefab != null && info.machinePrefab.name == myPrefabName) {
                
                // 찾았다면, 해당 버튼의 Image 컴포넌트를 가져옵니다.
                UnityEngine.UI.Image btnImage = info.GetComponent<UnityEngine.UI.Image>();
                
                // ✨ 빌드 매니저에게 "이 버튼을 누른 것처럼 처리해줘!" 라고 명령합니다.
                if (btnImage != null && Ingame_Manager_Build.Instance != null) {
                    Ingame_Manager_Build.Instance.SelectMachine(btnImage);
                    return;
                }
            }
        }
    }

    // ✨ 전체 가동/정지 버튼용으로 기능은 유지해야 하므로 비워둡니다.
    public virtual void ToggleOperation() { }

    public void UpdateStatusUI() {
        if (statusIconRenderer == null) return;
        statusIconRenderer.color = Color.white; 

        if (!isOperating && isStopping) statusIconRenderer.sprite = spriteStopping; 
        else if (isOperating) statusIconRenderer.sprite = spriteRunning; 
        else statusIconRenderer.sprite = spriteStopped; 
    }
}