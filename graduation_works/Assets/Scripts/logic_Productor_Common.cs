using UnityEngine;
using System.Collections;
using System.Collections.Generic; 

[RequireComponent(typeof(SpriteRenderer))]
public class logic_Productor_Common : logic_CodingBase
{
    [Header("가공 설정")]
    public GameObject productPrefab; 
    public int requireResourceAmount = 50; 
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

            // 건설 모드가 아니고 일시정지가 아니며, 가공 중이 아닐 때만 체크 타이머 증가
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

        if (Ingame_Manager_Resource.Instance.resCommon >= requireResourceAmount) {
            Ingame_Manager_Resource.Instance.resCommon -= requireResourceAmount;
            Ingame_Manager_Resource.Instance.EarnGold(0); // UI 갱신용
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

            // 시간이 제대로 흐르고 있을 때만 가공 진행
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

    // 🔥 [수정] 무작위 3x3 탐색 대신, 매니저의 방향 계산 로직을 사용하도록 변경
    void SpawnProduct() {
        if (productPrefab == null || Ingame_Manager_Build.Instance == null) return;

        var buildMgr = Ingame_Manager_Build.Instance;
        Vector3Int myCell = buildMgr.tilemapInstallations.WorldToCell(transform.position);
        
        // 🔥 매니저에게 '최종적으로 떨어질 목적지 월드 좌표'를 계산해달라고 요청
        Vector3 targetDropPos = buildMgr.GetDropPosition(myCell);

        Vector3 spawnPos = transform.position;
        spawnPos.y -= 0.5f; // 약간 아래쪽에서 튀어나오게 연출
        spawnPos.z = -1f;

        GameObject productObj = Instantiate(productPrefab, spawnPos, Quaternion.identity);
        
        Ingame_Item_Dropped itemScript = productObj.GetComponent<Ingame_Item_Dropped>();
        if (itemScript != null) {
            itemScript.SetDropTarget(targetDropPos);
        }
    }

    // 🔥 기존에 있던 FindEmptyTile 함수는 이제 필요 없으므로 삭제!
}