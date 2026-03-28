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

    protected override void Awake() {
        spriteRenderer = GetComponent<SpriteRenderer>();
        if (GetComponent<BoxCollider2D>() == null) gameObject.AddComponent<BoxCollider2D>();
        base.Awake(); 
    }

    void Start() {
        if (spriteRenderer != null && spriteIdle != null)
            spriteRenderer.sprite = spriteIdle;
    }

    public override void ToggleOperation() {
        if (isOperating) {
            isOperating = false;
            isStopping = true;
            UpdateStatusUI(); // ⏳ 멈추는 중 아이콘!
        } else {
            isOperating = true;
            isStopping = false;
            UpdateStatusUI(); // ▶️ 실행 중 아이콘!
            InitializeProductor(this.processingCount); 
        }
    }

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

        // ✨ 퀘스트 매니저에서 현재 해금 레벨을 가져옵니다.
        int loopLevel = 0;
        if (Ingame_Manager_Quest.Instance != null) {
            loopLevel = Ingame_Manager_Quest.Instance.loopUpgradeLevel;
        }

        // 🔄 무한 반복 (while) 검사
        if (cleanCode.Contains("whiletrue:") || cleanCode.Contains("while(true)") || cleanCode.Contains("loop:")) {
            if (loopLevel < 2) return CodeState.Error_InfiniteLocked; // 레벨 2 미만이면 컷!
            
            processingCount = -1; // ✨ 수정완료!
            return CodeState.Valid;
        }

        // 🔄 횟수 반복 (for) 검사
        if (cleanCode.Contains("for") && cleanCode.Contains("range(")) {
            if (loopLevel < 1) return CodeState.Error_LoopLocked; // 레벨 1 미만이면 컷!
            
            try {
                int start = cleanCode.IndexOf("range(") + 6;
                int end = cleanCode.IndexOf(")", start);
                int count = int.Parse(cleanCode.Substring(start, end - start));
                
                if (count <= 0) return CodeState.Error;
                
                // 레벨 1인데 10회를 초과해서 적었다면 컷!
                if (loopLevel == 1 && count > 10) return CodeState.Error_LoopLimit;

                processingCount = count; // ✨ 수정완료!
                return CodeState.Valid;
            } catch { return CodeState.Error; }
        }

        // ⛏️ 그냥 명령어 1줄만 쳤을 때
        if (cleanCode.Contains(targetSyntax)) {
            processingCount = 1; // ✨ 수정완료! (기본 1회 실행 후 멈춤)
            return CodeState.Valid;
        }
        
        return CodeState.Error;
    }

    IEnumerator MasterRoutine() {
        int currentCount = 0;
        
        isOperating = true; 
        isStopping = false;
        UpdateStatusUI(); // 시작 확인

        while (processingCount == -1 || currentCount < processingCount) {
            bool isBuildMode = (Ingame_Manager_Build.Instance != null && Ingame_Manager_Build.Instance.isBuildMode);
            bool isPaused = (Ingame_Manager_Time.Instance != null && Ingame_Manager_Time.Instance.isPaused);

            if (!isBuildMode && !isPaused) {
                if (CheckAndConsumeResources()) {
                    yield return StartCoroutine(ProcessingAnimationRoutine());
                    SpawnProduct();
                    if (processingCount != -1) currentCount++;

                    if (isStopping) break; 
                } else {
                    yield return new WaitForSeconds(checkInterval);
                    if (isStopping) break;
                }
            } else {
                yield return null;
            }
        }
        
        if (spriteRenderer != null && spriteIdle != null) spriteRenderer.sprite = spriteIdle;
        
        isOperating = false; 
        isStopping = false;
        UpdateStatusUI(); // ⏹️ 완전히 정지 아이콘으로 변경!
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

        if (spriteRenderer != null && spriteIdle != null) {
            spriteRenderer.sprite = spriteIdle;
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