using UnityEngine;
using System.Collections;
using System.Collections.Generic;

// 🔥 확률과 프리팹을 하나로 묶어주는 클래스
[System.Serializable]
public class MinerDropRate {
    public GameObject itemPrefab;
    [Range(0f, 100f)] public float probability; 
}

[RequireComponent(typeof(SpriteRenderer))]
public class logic_Miner_Master : logic_CodingBase {

    // ==========================================
    // 🔥 [핵심 추가] 인스펙터에서 정답 명령어를 마음대로 바꿀 수 있습니다!
    // ==========================================
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

    void Awake() {
        spriteRenderer = GetComponent<SpriteRenderer>();
    }

    void Start() {
        if (spriteRenderer != null && spriteIdle != null)
            spriteRenderer.sprite = spriteIdle;
    }

    public void InitializeMiner(int count) {
        this.miningCount = count;
        if (miningCoroutine != null) StopCoroutine(miningCoroutine);
        if (miningCount != 0) miningCoroutine = StartCoroutine(MiningRoutine());
    }

    public override CodeState ValidateCode(string code) {
        string noTags = System.Text.RegularExpressions.Regex.Replace(code, "<.*?>", string.Empty);
        string cleanCode = System.Text.RegularExpressions.Regex.Replace(noTags, @"\s+", "").ToLower();
        
        // 인스펙터에 적은 정답에서도 공백을 없애고 소문자로 맞춰서 비교합니다.
        string targetSyntax = requiredSyntax.Replace(" ", "").ToLower();

        // 🚨 유저가 쓴 코드에 정답 명령어가 없으면 반응 안 함!
        if (!cleanCode.Contains(targetSyntax)) return CodeState.Empty;

        // 🔄 무한 반복 처리
        if (cleanCode.Contains("whiletrue:") || cleanCode.Contains("while(true)") || cleanCode.Contains("loop:")) {
            miningCount = -1;
            return CodeState.Valid;
        }

        // 🔄 for 반복문 처리
        if (cleanCode.Contains("for") && cleanCode.Contains("range(")) {
            try {
                int start = cleanCode.IndexOf("range(") + 6;
                int end = cleanCode.IndexOf(")", start);
                int count = int.Parse(cleanCode.Substring(start, end - start));
                if (count <= 0) return CodeState.Error;
                miningCount = count;
                return CodeState.Valid;
            } catch { return CodeState.Error; }
        }

        // ⛏️ 그냥 명령어 1줄만 쳤을 때도 기본적으로 무한 반복 되도록 세팅
        if (cleanCode.Contains(targetSyntax)) {
            miningCount = -1; 
            return CodeState.Valid;
        }
        
        return CodeState.Error;
    }

    IEnumerator MiningRoutine() {
        int currentCount = 0;
        float animTimer = 0f;
        bool isSpriteA = true; 

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
        }
        
        if (spriteRenderer != null && spriteIdle != null) spriteRenderer.sprite = spriteIdle;
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

        // 🔥 확률별 가챠 로직
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