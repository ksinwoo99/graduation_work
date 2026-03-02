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

        // 🔥 [수정] 매니저에게 '최종적으로 떨어질 목적지 월드 좌표'를 계산해달라고 요청
        Vector3 targetDropPos = buildMgr.GetDropPosition(myCell);

        Vector3 spawnPos = transform.position;
        spawnPos.y -= 0.5f; // 약간 아래쪽에서 튀어나오게 연출
        spawnPos.z = -1f;

        GameObject itemObj = Instantiate(droppedItemPrefab, spawnPos, Quaternion.identity);
        Ingame_Item_Dropped itemScript = itemObj.GetComponent<Ingame_Item_Dropped>();
        
        if (itemScript != null) {
            itemScript.SetDropTarget(targetDropPos);
        }
    }
}