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
    public string requiredSyntax = "mining("; 

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
        if (spriteRenderer != null && spriteIdle != null) spriteRenderer.sprite = spriteIdle;
        UpdateStatusUI();
    }

    public override void ToggleOperation() {
        if (miningCount == 0) {
            if (Ingame_Manager_Build.Instance != null) {
                Vector3 pos = transform.position; pos.z = -5f;
                Ingame_Manager_Build.Instance.ShowFloatingText("명령어가 없습니다.", pos);
            }
            return;
        }

        if (isOperating) {
            isOperating = false;
            isStopping = true;
            UpdateStatusUI(); 
        } else {
            isOperating = true;
            isStopping = false;
            UpdateStatusUI(); 
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

        if (!cleanCode.Contains(targetSyntax)) {
            miningCount = 0; 
            return CodeState.Empty;
        }

        if (!GameCodeValidator.AllMiningCallsMatch(noTags, requiredSyntax)) {
            miningCount = 0;
            return CodeState.Error_WrongMachineSyntax;
        }

        int loopLevel = 0;
        if (Ingame_Manager_Quest.Instance != null) loopLevel = Ingame_Manager_Quest.Instance.loopUpgradeLevel;

        // 1. 무한 반복 (while true / for i in count(...)) 검사
        //    - `for i in count(...)` -> 공백 제거 시 `incount(` 패턴 등장
        if (cleanCode.Contains("whiletrue:") || cleanCode.Contains("while(true)") || cleanCode.Contains("loop:")
            || cleanCode.Contains("incount(")) {
            if (loopLevel < 2) { miningCount = 0; return CodeState.Error_InfiniteLocked; }
            miningCount = -1; 
            return CodeState.Valid;
        }

        // 2. 횟수 반복 (for range) 검사
        if (cleanCode.Contains("for") && cleanCode.Contains("range(")) {
            if (loopLevel < 1) { miningCount = 0; return CodeState.Error_LoopLocked; }
            try {
                int start = cleanCode.IndexOf("range(") + 6;
                int end = cleanCode.IndexOf(")", start);
                int count = int.Parse(cleanCode.Substring(start, end - start));
                if (count <= 0) { miningCount = 0; return CodeState.Error; }
                if (loopLevel == 1 && count > 10) { miningCount = 0; return CodeState.Error_LoopLimit; }

                miningCount = count; 
                return CodeState.Valid;
            } catch { miningCount = 0; return CodeState.Error; }
        }

        // ✨ 3. [핵심 추가] 횟수 반복 (while 조건문 숫자로 파싱) 검사
        System.Text.RegularExpressions.Match whileMatch = System.Text.RegularExpressions.Regex.Match(cleanCode, @"while.*?([0-9]+).*?:");
        if (whileMatch.Success) {
            if (loopLevel < 1) { miningCount = 0; return CodeState.Error_LoopLocked; }
            try {
                int count = int.Parse(whileMatch.Groups[1].Value);
                if (count <= 0) { miningCount = 0; return CodeState.Error; }
                if (loopLevel == 1 && count > 10) { miningCount = 0; return CodeState.Error_LoopLimit; }

                miningCount = count; 
                return CodeState.Valid;
            } catch { miningCount = 0; return CodeState.Error; }
        }

        // 4. 일반 1회 실행
        if (cleanCode.Contains(targetSyntax)) {
            miningCount = 1; 
            return CodeState.Valid;
        }
        
        miningCount = 0;
        return CodeState.Error;
    }

    IEnumerator MiningRoutine() {
        int currentCount = 0;
        float animTimer = 0f;
        bool isSpriteA = true; 
        
        isOperating = true; 
        isStopping = false;
        UpdateStatusUI(); 

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
            if (Ingame_Manager_Build.Instance != null && Ingame_Manager_Build.Instance.codingManager != null && Ingame_System_Save.Instance != null) {
                int myMachineId = Ingame_System_Save.Instance.GetMachineTypeInt(this.GetMachineName());
                Ingame_Manager_Build.Instance.codingManager.ReportMachineWork(myMachineId);
            }
            if (isStopping) break; 
        }
        
        if (spriteRenderer != null && spriteIdle != null) spriteRenderer.sprite = spriteIdle;
        
        isOperating = false; 
        isStopping = false;
        UpdateStatusUI(); 
        miningCoroutine = null; 
    }

    void SpawnResource() {
        if (dropList.Count == 0 || Ingame_Manager_Build.Instance == null) return;
        var buildMgr = Ingame_Manager_Build.Instance;
        Vector3Int myCell = buildMgr.tilemapInstallations.WorldToCell(transform.position);
        Vector3 targetDropPos = buildMgr.GetDropPosition(myCell);

        Vector3 spawnPos = transform.position; spawnPos.y -= 0.5f; spawnPos.z = -1f;
        float rand = Random.Range(0f, 100f); float cumulative = 0f;
        GameObject prefabToDrop = null;

        foreach (var drop in dropList) {
            cumulative += drop.probability;
            if (rand <= cumulative) { prefabToDrop = drop.itemPrefab; break; }
        }

        if (prefabToDrop == null) prefabToDrop = dropList[dropList.Count - 1].itemPrefab;
        if (prefabToDrop == null) return;

        GameObject itemObj = Instantiate(prefabToDrop, spawnPos, Quaternion.identity);
        Ingame_Item_Dropped itemScript = itemObj.GetComponent<Ingame_Item_Dropped>();
        if (itemScript != null) itemScript.SetDropTarget(targetDropPos);
    }
}