using UnityEngine;
using System.Collections;

[RequireComponent(typeof(SpriteRenderer))]
public class logic_Productor_Advanced : logic_CodingBase
{
    [Header("가공 설정")]
    public GameObject productPrefab; 
    
    // 두 가지 자원을 모두 요구하도록 변수 분리!
    public int requireCommonAmount = 50;  // 기본 자원 요구량
    public int requireSpecialAmount = 20; // 희귀 자원(Special) 요구량
    
    public float checkInterval = 1.0f; 
    public float processingTime = 5.0f; 

    [Header("애니메이션 설정")]
    public Sprite spriteIdle;   
    public Sprite spriteActive; 

    private SpriteRenderer spriteRenderer;
    private bool isProcessing = false;

    void Awake() {
        spriteRenderer = GetComponent<SpriteRenderer>();
    }

    void Start() {
        if (spriteRenderer != null && spriteIdle != null)
            spriteRenderer.sprite = spriteIdle;
        StartCoroutine(CheckResourceRoutine());
    }

    public override CodeState ValidateCode(string code) {
        return CodeState.Valid; 
    }

    IEnumerator CheckResourceRoutine() {
        float timer = 0f;
        while (true) {
            bool isBuildMode = (Ingame_Manager_Build.Instance != null && Ingame_Manager_Build.Instance.isBuildMode);
            bool isPaused = (Ingame_Manager_Time.Instance != null && Ingame_Manager_Time.Instance.isPaused);

            if (!isBuildMode && !isPaused && !isProcessing) {
                timer += Time.deltaTime;
                if (timer >= checkInterval) {
                    timer = 0f;
                    TryStartProcessing();
                }
            }
            yield return null;
        }
    }

    void TryStartProcessing() {
        if (Ingame_Manager_Resource.Instance == null) return;

        var resMgr = Ingame_Manager_Resource.Instance;

        // 기본 자원과 희귀 자원(Special)이 "둘 다" 충분한지 검사!
        if (resMgr.resCommon >= requireCommonAmount && resMgr.resSpecial >= requireSpecialAmount) {
            
            // 둘 다 차감
            resMgr.resCommon -= requireCommonAmount;
            resMgr.resSpecial -= requireSpecialAmount;
            
            resMgr.EarnGold(0); // UI 갱신용
            StartCoroutine(ProcessingRoutine());
        }
    }

    IEnumerator ProcessingRoutine() {
        isProcessing = true;
        float timer = 0f;
        float animTimer = 0f;
        bool isSpriteA = true;

        while (timer < processingTime) {
            bool isBuildMode = (Ingame_Manager_Build.Instance != null && Ingame_Manager_Build.Instance.isBuildMode);
            bool isPaused = (Ingame_Manager_Time.Instance != null && Ingame_Manager_Time.Instance.isPaused);

            if (!isBuildMode && !isPaused) {
                timer += Time.deltaTime;
                animTimer += Time.deltaTime;

                if (animTimer >= 0.5f) {
                    animTimer = 0f;
                    isSpriteA = !isSpriteA;
                    if (spriteRenderer != null) 
                        spriteRenderer.sprite = isSpriteA ? spriteIdle : spriteActive;
                }
            }
            yield return null;
        }

        SpawnProduct();

        isProcessing = false;
        if (spriteRenderer != null) spriteRenderer.sprite = spriteIdle;
    }

    void SpawnProduct() {
        if (productPrefab == null || Ingame_Manager_Build.Instance == null) return;

        var buildMgr = Ingame_Manager_Build.Instance;
        Vector3Int myCell = buildMgr.tilemapInstallations.WorldToCell(transform.position);
        Vector3 targetDropPos = buildMgr.GetDropPosition(myCell);

        Vector3 spawnPos = transform.position;
        spawnPos.y -= 0.5f; 
        spawnPos.z = -1f;

        GameObject productObj = Instantiate(productPrefab, spawnPos, Quaternion.identity);
        
        Ingame_Item_Dropped itemScript = productObj.GetComponent<Ingame_Item_Dropped>();
        if (itemScript != null) {
            itemScript.SetDropTarget(targetDropPos);
        }
    }
}