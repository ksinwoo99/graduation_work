using UnityEngine;
using System.Collections;
using System.Collections.Generic;

[RequireComponent(typeof(SpriteRenderer))]
public class logic_Miner_Common : logic_CodingBase {

    [Header("채굴 설정")]
    public GameObject droppedItemPrefab; 
    public GameObject machinePrefab;     
    public float miningInterval = 3.0f;  
    public ResourceType myResourceType = ResourceType.Common; 
    
    [Header("애니메이션 설정")]
    public Sprite spriteIdle;   // A 이미지
    public Sprite spriteActive; // B 이미지
    
    // 내부 변수
    private SpriteRenderer spriteRenderer;
    
    // 🔥 [수정] private -> public으로 변경!
    // (저장 시스템과 빌드 매니저가 이 숫자를 읽어가야 함)
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
        
        if (miningCount != 0) {
            miningCoroutine = StartCoroutine(MiningRoutine());
        }
    }

    public override CodeState ValidateCode(string code) {
        string cleanCode = code.Replace(" ", "").ToLower();

        // 🚨 mining() 안에 숫자 넣기 금지
        if (cleanCode.Contains("mining(") && !cleanCode.Contains("mining()")) {
            return CodeState.Error; 
        }

        if (!cleanCode.Contains("mining()")) {
            return CodeState.Empty;
        }

        // 🔄 1) 무한 반복
        if (cleanCode.Contains("while(true)") || cleanCode.Contains("loop:")) {
            miningCount = -1;
            return CodeState.Valid;
        }

        // 🔄 2) for 반복문
        if (cleanCode.Contains("for") && cleanCode.Contains("range(")) {
            try {
                int start = cleanCode.IndexOf("range(") + 6;
                int end = cleanCode.IndexOf(")", start);
                string numStr = cleanCode.Substring(start, end - start);
                
                int count = int.Parse(numStr);
                if (count <= 0) return CodeState.Error;

                miningCount = count;
                return CodeState.Valid;
            } catch { return CodeState.Error; }
        }

        // ⛏️ 3) 기본 1회
        if (cleanCode.Contains("mining()")) {
            miningCount = 1;
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

                    // 📺 0.5초마다 깜빡깜빡
                    if (animTimer >= 0.5f) {
                        animTimer = 0f;
                        isSpriteA = !isSpriteA;
                        
                        if (spriteRenderer != null) {
                            spriteRenderer.sprite = isSpriteA ? spriteIdle : spriteActive;
                        }
                    }
                }
                yield return null;
            }

            SpawnResource();
            
            if (miningCount != -1) currentCount++;
        }
        
        // 🛑 종료 시 A 이미지 복귀
        if (spriteRenderer != null && spriteIdle != null)
            spriteRenderer.sprite = spriteIdle;
            
        miningCoroutine = null; 
    }

    void SpawnResource() {
        if (droppedItemPrefab == null || Ingame_Manager_Build.Instance == null) return;

        var buildMgr = Ingame_Manager_Build.Instance;
        Vector3Int myCell = buildMgr.tilemapInstallations.WorldToCell(transform.position);
        
        // 1차 시도: 3x3 범위 (거리 1)
        Vector3Int targetCell = FindEmptyTile(myCell, 1);

        // 2차 시도: 3x3가 꽉 찼다면 5x5 범위 (거리 2)
        if (targetCell == myCell) {
            targetCell = FindEmptyTile(myCell, 2);
        }

        // 3. 찾은 위치로 아이템 생성 및 발사
        // 만약 5x5도 꽉 찼다면(targetCell == myCell) 그냥 제자리(기계 위)에 생성됨
        Vector3 targetWorldPos = buildMgr.tilemapInstallations.GetCellCenterWorld(targetCell);
        targetWorldPos.z = -2f; 

        GameObject itemObj = Instantiate(droppedItemPrefab, transform.position, Quaternion.identity);
        Ingame_Item_Dropped itemScript = itemObj.GetComponent<Ingame_Item_Dropped>();
        
        if (itemScript != null) {
            itemScript.SetDropTarget(targetWorldPos);
        }
    }

    // 빈 타일 찾는 함수 (range: 1이면 3x3, 2면 5x5 테두리)
    Vector3Int FindEmptyTile(Vector3Int center, int range) {
        var buildMgr = Ingame_Manager_Build.Instance;
        List<Vector3Int> candidates = new List<Vector3Int>();

        for (int x = -range; x <= range; x++) {
            for (int y = -range; y <= range; y++) {
                // 안쪽 범위는 이미 검사했으므로 건너뜀 (5x5 검사 시 3x3 영역 제외)
                if (Mathf.Abs(x) < range && Mathf.Abs(y) < range) continue;
                if (x == 0 && y == 0) continue; 

                Vector3Int checkPos = center + new Vector3Int(x, y, 0);

                // 설치물이 없는 곳만 후보로 등록
                if (!buildMgr.IsOccupied(checkPos)) {
                    candidates.Add(checkPos);
                }
            }
        }

        if (candidates.Count > 0) {
            return candidates[Random.Range(0, candidates.Count)];
        }
        return center; // 실패 시 자기 위치 반환
    }
}