using UnityEngine;
using TMPro;
using System.Collections;
using UnityEngine.EventSystems;

public class Ingame_Item_Dropped : MonoBehaviour
{
    [Header("모드 설정")]
    public bool isProduct = false; 

    [Header("자원 모드 (Is Product = false)")]
    public ResourceType resourceType = ResourceType.Common;
    public int amount = 1; 
    
    [Header("상품 모드 (Is Product = true)")]
    public int sellPrice = 100;

    [Header("고품질(대박) 설정")]
    public GameObject sparkleEffectObject;      
    public bool isHighQuality = false;          
    public float qualityMultiplier = 2.0f;      

    [Header("연출 설정")]
    public float popHeight = 0.8f;   
    public float popDuration = 0.5f; 
    
    [Header("컨베이어 이동 설정")]
    public float conveyorMoveSpeed = 1.0f; 

    private Vector3? forcedTargetPos = null;

    public void SetDropTarget(Vector3 target) {
        forcedTargetPos = target;
    }

    public void SetHighQuality()
    {
        Debug.Log("🎉 [테스트] 대박 판정 성공! 이펙트 켜기를 시도합니다!");
        isHighQuality = true;
        
        if (sparkleEffectObject != null)
        {
            Debug.Log("✨ [테스트] 자식 오브젝트가 연결되어 있습니다. 켭니다!");
            sparkleEffectObject.SetActive(true);
            
            ParticleSystem ps = sparkleEffectObject.GetComponent<ParticleSystem>();
            if (ps != null) ps.Play();
        }
        else
        {
            Debug.LogError("🚨 [테스트 실패] Sparkle Effect Object 빈칸이 비어있습니다!");
        }
    }

    void Start() {
        Vector3 startPos = transform.position;
        Vector3 targetPos = forcedTargetPos.HasValue 
                            ? forcedTargetPos.Value 
                            : startPos + (Vector3)Random.insideUnitCircle * 0.8f;
                            
        StartCoroutine(PopAnimation(targetPos));

        // ✨ [튜토리얼 연동 2] 자원인지 상품인지 구분해서 전달!
        if (Ingame_UI_Tutorial.Instance != null && Ingame_UI_Tutorial.Instance.isTutorialActive) {
            Ingame_UI_Tutorial.Instance.TriggerResourceSpawned(isProduct);
        }
    }

    private void OnMouseDown() 
    {
        if (Shared_Manager_Session.IsVisiting) return;
        if (EventSystem.current != null && EventSystem.current.IsPointerOverGameObject()) return;

        if (Ingame_UI_Tutorial.Instance != null && 
            Ingame_UI_Tutorial.Instance.isTutorialActive && 
            !Ingame_UI_Tutorial.Instance.isActionMode) return;

        CollectItem();
    }

    public void CollectItem() {
        if (Ingame_Manager_Build.Instance == null) {
            Destroy(gameObject);
            return;
        }

        if (isProduct) {
            int finalPrice = isHighQuality ? (int)(sellPrice * qualityMultiplier) : sellPrice;
            
            if (Ingame_Manager_Resource.Instance != null)
                Ingame_Manager_Resource.Instance.EarnGold(finalPrice);
            
            string msg = $"<color=#FFD700>+{finalPrice} G</color>"; 
            Ingame_Manager_Build.Instance.ShowFloatingText(msg, transform.position);
        }
        else {
            int finalAmount = isHighQuality ? (int)(amount * qualityMultiplier) : amount;

            if (Ingame_Manager_Resource.Instance != null)
                Ingame_Manager_Resource.Instance.AddResource(resourceType, finalAmount);
                
            string krName = GetKoreanName(resourceType);
            
            string colorCode = (resourceType == ResourceType.Common) ? "#00FF00" : "#FFFFFF";
            if (resourceType == ResourceType.Rare) colorCode = "#00FFFF";
            if (resourceType == ResourceType.Special) colorCode = "#FF00FF"; 
            if (resourceType == ResourceType.Exotic) colorCode = "#FF4500"; 
            
            string msg = $"<color={colorCode}>{krName} +{finalAmount}</color>";
            Ingame_Manager_Build.Instance.ShowFloatingText(msg, transform.position);
        }

        // ✨ [튜토리얼 연동 2] 자원인지 상품인지 구분해서 전달!
        if (Ingame_UI_Tutorial.Instance != null && Ingame_UI_Tutorial.Instance.isTutorialActive) {
            Ingame_UI_Tutorial.Instance.TriggerResourceCollected(isProduct);
        }

        Destroy(gameObject); 
    }
    
    public void StopMovement() {
        // 1. 현재 진행 중인 컨베이어 이동 코루틴(ConveyorRideRoutine)을 즉시 멈춥니다.
        StopAllCoroutines();

        // 2. 공중에 떠있거나 엉뚱한 깊이에 있지 않도록 바닥(z: -1)으로 확실히 떨어뜨려 줍니다.
        Vector3 currentPos = transform.position;
        currentPos.z = -1f;
        transform.position = currentPos;

        // 3. (선택) 유저에게 벨트가 끊어져 아이템이 떨어졌다는 시각적 알림을 줍니다.
        if (Ingame_Manager_Build.Instance != null) {
            Ingame_Manager_Build.Instance.ShowFloatingText("벨트 끊어짐!", transform.position);
        }
    }

    string GetKoreanName(ResourceType type) {
        switch (type) {
            case ResourceType.Common: return "기본 자원";
            case ResourceType.Rare: return "희귀 자원";    
            case ResourceType.Special: return "특수 자원"; 
            case ResourceType.Exotic: return "경이 자원";
            default: return "알 수 없음";
        }
    }

    IEnumerator PopAnimation(Vector3 targetPos) {
        Vector3 startPos = transform.position;
        float timer = 0;
        while (timer < popDuration) {
            bool isBuildMode = (Ingame_Manager_Build.Instance != null && Ingame_Manager_Build.Instance.isBuildMode);
            bool isPaused = (Ingame_Manager_Time.Instance != null && Ingame_Manager_Time.Instance.isPaused);
            
            if (!isBuildMode && !isPaused) {
                timer += Time.deltaTime;
                float progress = timer / popDuration;
                float height = Mathf.Sin(progress * Mathf.PI) * popHeight;
                transform.position = Vector3.Lerp(startPos, targetPos, progress) + Vector3.up * height;
            }
            yield return null;
        }
        transform.position = targetPos;
        StartCoroutine(ConveyorRideRoutine(targetPos));
    }

    IEnumerator ConveyorRideRoutine(Vector3 startBasePos) {
        var buildMgr = Ingame_Manager_Build.Instance;
        if (buildMgr == null) yield break;

        Vector3 basePos = startBasePos; 

        while (true) {
            Vector3Int currentCell = buildMgr.tilemapInstallations.WorldToCell(basePos);
            var installed = buildMgr.GetInstalledObjects();
            bool moved = false;

            if (installed.ContainsKey(currentCell)) {
                logic_Conveyor conveyor = installed[currentCell].GetComponent<logic_Conveyor>();
                
                if (conveyor != null && conveyor.isWorking) {
                    Vector3Int pushDir = conveyor.GetPushDirection();
                    Vector3Int nextCell = currentCell + pushDir;
                    
                    Vector3 startLerpPos = basePos;
                    Vector3 nextWorldPos = buildMgr.tilemapInstallations.GetCellCenterWorld(nextCell);
                    nextWorldPos.z = startLerpPos.z;

                    float moveTimer = 0f;
                    
                    float currentConveyorSpeed = conveyor.itemMoveDuration; 

                    while (moveTimer < currentConveyorSpeed) {
                        bool isBuildMode = buildMgr.isBuildMode;
                        bool isPaused = (Ingame_Manager_Time.Instance != null && Ingame_Manager_Time.Instance.isPaused);
                        
                        if (!isBuildMode && !isPaused) {
                            moveTimer += Time.deltaTime;
                            float t = moveTimer / currentConveyorSpeed;
                            basePos = Vector3.Lerp(startLerpPos, nextWorldPos, t);
                        }
                        
                        transform.position = basePos;
                        yield return null;
                    }
                    basePos = nextWorldPos; 
                    transform.position = basePos; 
                    moved = true;
                }
            }

            if (!moved) {
                transform.position = basePos;
                yield return new WaitForSeconds(0.3f);
            }
        }
    }
}