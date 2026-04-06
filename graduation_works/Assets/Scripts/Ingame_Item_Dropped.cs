using UnityEngine;
using TMPro;
using System.Collections;

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
    public GameObject sparkleEffectObject;      // ✨ 인스펙터에서 자식 반짝이 오브젝트를 연결해주세요.
    public bool isHighQuality = false;          // 외부에서 체크할 변수
    public float qualityMultiplier = 2.0f;      // ✨ 고품질일 때 자원/가격 배율 (기본 2배)

    [Header("연출 설정")]
    public float popHeight = 0.8f;   
    public float popDuration = 0.5f; 
    
    [Header("컨베이어 이동 설정")]
    public float conveyorMoveSpeed = 1.0f; 

    private Vector3? forcedTargetPos = null;

    public void SetDropTarget(Vector3 target) {
        forcedTargetPos = target;
    }

    // ✨ [핵심 추가] 가공기에서 로또 터지면 이 함수를 호출합니다!
    public void SetHighQuality()
    {
        isHighQuality = true;
        
        // 미리 세팅해둔 반짝이 이펙트를 켭니다!
        if (sparkleEffectObject != null)
        {
            sparkleEffectObject.SetActive(true);
            
            // 파티클 시스템이라면 재생시켜줍니다.
            ParticleSystem ps = sparkleEffectObject.GetComponent<ParticleSystem>();
            if (ps != null) ps.Play();
        }
    }

    void Start() {
        Vector3 startPos = transform.position;
        Vector3 targetPos = forcedTargetPos.HasValue 
                            ? forcedTargetPos.Value 
                            : startPos + (Vector3)Random.insideUnitCircle * 0.8f;
                            
        StartCoroutine(PopAnimation(targetPos));
    }

    private void OnMouseDown() {
        if (Shared_Manager_Session.IsVisiting) return;
        CollectItem();
    }

    public void CollectItem() {
        if (Ingame_Manager_Build.Instance == null) {
            Destroy(gameObject);
            return;
        }

        // ✨ 수집할 때 고품질 여부에 따라 보상과 텍스트를 다르게 줍니다!
        if (isProduct) {
            int finalPrice = isHighQuality ? (int)(sellPrice * qualityMultiplier) : sellPrice;
            
            if (Ingame_Manager_Resource.Instance != null)
                Ingame_Manager_Resource.Instance.EarnGold(finalPrice);
            
            string qualityTag = isHighQuality ? "대박! " : "";
            string msg = $"<color=#FFD700>{qualityTag}+{finalPrice} G</color>"; 
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
            
            string qualityTag = isHighQuality ? "대박! " : "";
            string msg = $"<color={colorCode}>{qualityTag}{krName} +{finalAmount}</color>";
            Ingame_Manager_Build.Instance.ShowFloatingText(msg, transform.position);
        }
        Destroy(gameObject); 
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
                yield return null;
            }
        }
    }
}