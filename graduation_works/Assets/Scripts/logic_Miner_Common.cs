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
    public Sprite spriteIdle;   
    public Sprite spriteActive; 
    
    // 🔥 자식(고급 채굴기)도 쓸 수 있게 protected로 변경
    protected SpriteRenderer spriteRenderer; 
    public int miningCount = 0; 
    protected Coroutine miningCoroutine;

    // 🔥 자식이 덮어쓸 수 있게 virtual 추가
    protected virtual void Awake() { 
        spriteRenderer = GetComponent<SpriteRenderer>();
    }

    protected virtual void Start() {
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
        string noTags = System.Text.RegularExpressions.Regex.Replace(code, "<.*?>", string.Empty);
        string cleanCode = System.Text.RegularExpressions.Regex.Replace(noTags, @"\s+", "").ToLower();

        if (cleanCode.Contains("mining(") && !cleanCode.Contains("mining()")) return CodeState.Error; 
        if (!cleanCode.Contains("mining()")) return CodeState.Empty;

        // 1. 횟수가 정해진 for 반복문 처리
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

        if (cleanCode.Contains("mining()")) {
            miningCount = -1;
            return CodeState.Valid;
        }

        return CodeState.Error;
    }

    protected virtual IEnumerator MiningRoutine() {
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
                        
                        if (spriteRenderer != null) {
                            spriteRenderer.sprite = isSpriteA ? spriteIdle : spriteActive;
                        }
                    }
                }
                yield return null;
            }

            SpawnResource(); // 자식이 덮어쓴 함수가 실행됨
            
            if (miningCount != -1) currentCount++;
        }
        
        if (spriteRenderer != null && spriteIdle != null)
            spriteRenderer.sprite = spriteIdle;
            
        miningCoroutine = null; 
    }

    protected virtual void SpawnResource() {
        if (droppedItemPrefab == null || Ingame_Manager_Build.Instance == null) return;

        var buildMgr = Ingame_Manager_Build.Instance;
        Vector3Int myCell = buildMgr.tilemapInstallations.WorldToCell(transform.position);
        Vector3 targetDropPos = buildMgr.GetDropPosition(myCell);

        Vector3 spawnPos = transform.position;
        spawnPos.y -= 0.5f; 
        spawnPos.z = -1f;

        GameObject itemObj = Instantiate(droppedItemPrefab, spawnPos, Quaternion.identity);
        Ingame_Item_Dropped itemScript = itemObj.GetComponent<Ingame_Item_Dropped>();
        
        if (itemScript != null) {
            itemScript.SetDropTarget(targetDropPos);
        }
    }
}