using UnityEngine;
using System.Collections;
using System.Collections.Generic;

[System.Serializable]
public class RequireResourceInfo {
    public ResourceType resourceType;
    public int amount;
}

[System.Serializable]
public class ProductorDropRate {
    public GameObject productPrefab;
    [Range(0f, 100f)] public float probability;
}

[RequireComponent(typeof(SpriteRenderer))]
public class logic_Productor_Master : logic_CodingBase
{
    // 🔥 가공기도 명령어를 직접 세팅합니다!
    [Header("코딩 설정")]
    public string requiredSyntax = "producting()";

    [Header("마스터 가공 소모 자원 설정")]
    public List<RequireResourceInfo> consumeList = new List<RequireResourceInfo>(); 
    
    [Header("마스터 가공 결과 (가챠) 설정")]
    public List<ProductorDropRate> resultList = new List<ProductorDropRate>();

    public float checkInterval = 1.0f; 
    public float processingTime = 5.0f; 

    [Header("애니메이션 설정")]
    public Sprite spriteIdle;   
    public Sprite spriteActive; 

    private SpriteRenderer spriteRenderer;
    public int processingCount = 0; 
    private Coroutine processingCoroutine;

    void Awake() {
        spriteRenderer = GetComponent<SpriteRenderer>();
    }

    void Start() {
        if (spriteRenderer != null && spriteIdle != null)
            spriteRenderer.sprite = spriteIdle;
    }

    // 건설 직후 무한 반복 등을 세팅할 때 쓰임
    public void InitializeProductor(int count) {
        this.processingCount = count;
        if (processingCoroutine != null) StopCoroutine(processingCoroutine);
        if (processingCount != 0) processingCoroutine = StartCoroutine(MasterRoutine());
    }

    public override CodeState ValidateCode(string code) {
        string noTags = System.Text.RegularExpressions.Regex.Replace(code, "<.*?>", string.Empty);
        string cleanCode = System.Text.RegularExpressions.Regex.Replace(noTags, @"\s+", "").ToLower();
        string targetSyntax = requiredSyntax.Replace(" ", "").ToLower();

        if (!cleanCode.Contains(targetSyntax)) return CodeState.Empty;

        if (cleanCode.Contains("whiletrue:") || cleanCode.Contains("while(true)") || cleanCode.Contains("loop:")) {
            processingCount = -1;
            return CodeState.Valid;
        }

        if (cleanCode.Contains("for") && cleanCode.Contains("range(")) {
            try {
                int start = cleanCode.IndexOf("range(") + 6;
                int end = cleanCode.IndexOf(")", start);
                int count = int.Parse(cleanCode.Substring(start, end - start));
                if (count <= 0) return CodeState.Error;
                processingCount = count;
                return CodeState.Valid;
            } catch { return CodeState.Error; }
        }

        if (cleanCode.Contains(targetSyntax)) {
            processingCount = -1; 
            return CodeState.Valid;
        }

        return CodeState.Error;
    }

    IEnumerator MasterRoutine() {
        int currentCount = 0;

        while (processingCount == -1 || currentCount < processingCount) {
            bool isBuildMode = (Ingame_Manager_Build.Instance != null && Ingame_Manager_Build.Instance.isBuildMode);
            bool isPaused = (Ingame_Manager_Time.Instance != null && Ingame_Manager_Time.Instance.isPaused);

            if (!isBuildMode && !isPaused) {
                // 1. 자원이 충분한지 검사하고 깎음
                if (CheckAndConsumeResources()) {
                    // 2. 가공 애니메이션 대기 (processingTime 만큼 시간 소요)
                    yield return StartCoroutine(ProcessingAnimationRoutine());
                    
                    // 3. 상품 가챠 생성
                    SpawnProduct();
                    
                    if (processingCount != -1) currentCount++;
                } else {
                    // 자원이 부족하면 인터벌(1초)만큼 대기했다가 다시 체크
                    yield return new WaitForSeconds(checkInterval);
                }
            } else {
                yield return null;
            }
        }
        
        if (spriteRenderer != null && spriteIdle != null) spriteRenderer.sprite = spriteIdle;
        processingCoroutine = null;
    }

    bool CheckAndConsumeResources() {
        var resMgr = Ingame_Manager_Resource.Instance;
        if (resMgr == null || consumeList.Count == 0) return false;

        foreach (var req in consumeList) {
            if (!resMgr.HasEnoughResource(req.resourceType, req.amount)) return false; 
        }

        foreach (var req in consumeList) {
            resMgr.ConsumeResource(req.resourceType, req.amount);
        }
        
        resMgr.EarnGold(0); 
        return true;
    }

    IEnumerator ProcessingAnimationRoutine() {
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
                    if (spriteRenderer != null) spriteRenderer.sprite = isSpriteA ? spriteIdle : spriteActive;
                }
            }
            yield return null;
        }
    }

    void SpawnProduct() {
        if (resultList.Count == 0 || Ingame_Manager_Build.Instance == null) return;

        var buildMgr = Ingame_Manager_Build.Instance;
        Vector3Int myCell = buildMgr.tilemapInstallations.WorldToCell(transform.position);
        Vector3 targetDropPos = buildMgr.GetDropPosition(myCell);

        Vector3 spawnPos = transform.position;
        spawnPos.y -= 0.5f; 
        spawnPos.z = -1f;

        float rand = Random.Range(0f, 100f);
        float cumulative = 0f;
        GameObject prefabToDrop = null;

        foreach (var result in resultList) {
            cumulative += result.probability;
            if (rand <= cumulative) {
                prefabToDrop = result.productPrefab;
                break;
            }
        }

        if (prefabToDrop == null) prefabToDrop = resultList[resultList.Count - 1].productPrefab;
        if (prefabToDrop == null) return;

        GameObject productObj = Instantiate(prefabToDrop, spawnPos, Quaternion.identity);
        Ingame_Item_Dropped itemScript = productObj.GetComponent<Ingame_Item_Dropped>();
        if (itemScript != null) itemScript.SetDropTarget(targetDropPos);
    }
}