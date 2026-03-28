using UnityEngine;
using System.Collections;
using System.Collections.Generic;

[System.Serializable]
public class MinerDropRate {
    public GameObject itemPrefab;
    [Range(0f, 100f)] public float probability; 
}

[RequireComponent(typeof(SpriteRenderer))]
public class logic_Miner_Master : logic_CodingBase {

    [Header("코딩 설정")]
    public string requiredSyntax = "mining()"; 

    [Header("마스터 채굴 드롭 설정")]
    public List<MinerDropRate> dropList = new List<MinerDropRate>(); 
    public float miningInterval = 3.0f;  
    
    [Header("애니메이션 설정")]
    public Sprite spriteIdle;   
    public Sprite spriteActive; 
    
    private SpriteRenderer spriteRenderer;
    public int miningCount = 0; 
    private Coroutine miningCoroutine;

    protected override void Awake() {
        spriteRenderer = GetComponent<SpriteRenderer>();
        if (GetComponent<BoxCollider2D>() == null) gameObject.AddComponent<BoxCollider2D>();
        base.Awake(); 
    }

    void Start() {
        if (spriteRenderer != null && spriteIdle != null)
            spriteRenderer.sprite = spriteIdle;
    }

    // ✨ [추가] 상태가 변할 때마다 UpdateStatusUI() 호출!
    public override void ToggleOperation() {
        if (isOperating) {
            isOperating = false;
            isStopping = true;
            UpdateStatusUI(); // ⏳ 멈추는 중 아이콘으로 변경!
        } else {
            isOperating = true;
            isStopping = false;
            UpdateStatusUI(); // ▶️ 실행 중 아이콘으로 변경!
            InitializeMiner(this.miningCount); 
        }
    }

    public void InitializeMiner(int count) {
        this.miningCount = count;
        if (miningCoroutine != null) StopCoroutine(miningCoroutine);
        if (miningCount != 0) miningCoroutine = StartCoroutine(MiningRoutine());
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
            
            miningCount = -1; // 가공기 스크립트에서는 processingCount = -1; 로 변경해주세요!
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

                miningCount = count; // 가공기: processingCount = count;
                return CodeState.Valid;
            } catch { return CodeState.Error; }
        }

        // ⛏️ 그냥 명령어 1줄만 쳤을 때
        if (cleanCode.Contains(targetSyntax)) {
            miningCount = 1; // ✨ [핵심 수정] 이제 기본은 1회 실행 후 멈춥니다! (가공기: processingCount = 1;)
            return CodeState.Valid;
        }
        
        return CodeState.Error;
    }

    IEnumerator MiningRoutine() {
        int currentCount = 0;
        float animTimer = 0f;
        bool isSpriteA = true; 
        
        isOperating = true; 
        isStopping = false;
        UpdateStatusUI(); // 루프 시작 시 실행 아이콘 확인

        while (miningCount == -1 || currentCount < miningCount) {
            float timer = 0;
            while (timer < miningInterval) {
                bool isBuildMode = (Ingame_Manager_Build.Instance != null && Ingame_Manager_Build.Instance.isBuildMode);
                bool isPaused = (Ingame_Manager_Time.Instance != null && Ingame_Manager_Time.Instance.isPaused);

                if (!isPaused && !isBuildMode) {
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

            SpawnResource();
            if (miningCount != -1) currentCount++;

            if (isStopping) {
                break; // 이번 사이클 끝내고 멈춤 예약 확인
            }
        }
        
        if (spriteRenderer != null && spriteIdle != null) spriteRenderer.sprite = spriteIdle;
        
        isOperating = false; 
        isStopping = false;
        UpdateStatusUI(); // ⏹️ 완전히 정지 아이콘으로 변경!
        miningCoroutine = null; 
    }

    void SpawnResource() {
        if (dropList.Count == 0 || Ingame_Manager_Build.Instance == null) return;

        var buildMgr = Ingame_Manager_Build.Instance;
        Vector3Int myCell = buildMgr.tilemapInstallations.WorldToCell(transform.position);
        Vector3 targetDropPos = buildMgr.GetDropPosition(myCell);

        Vector3 spawnPos = transform.position;
        spawnPos.y -= 0.5f; 
        spawnPos.z = -1f;

        float rand = Random.Range(0f, 100f);
        float cumulative = 0f;
        GameObject prefabToDrop = null;

        foreach (var drop in dropList) {
            cumulative += drop.probability;
            if (rand <= cumulative) {
                prefabToDrop = drop.itemPrefab;
                break;
            }
        }

        if (prefabToDrop == null) prefabToDrop = dropList[dropList.Count - 1].itemPrefab;
        if (prefabToDrop == null) return;

        GameObject itemObj = Instantiate(prefabToDrop, spawnPos, Quaternion.identity);
        Ingame_Item_Dropped itemScript = itemObj.GetComponent<Ingame_Item_Dropped>();
        if (itemScript != null) itemScript.SetDropTarget(targetDropPos);
    }
}