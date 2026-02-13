using UnityEngine;
using System.Collections;
using System.Collections.Generic; 

[RequireComponent(typeof(SpriteRenderer))]
public class logic_Productor_Common : logic_CodingBase
{
    [Header("가공 설정")]
    public GameObject productPrefab; // Ingame_Item_Dropped가 붙고 IsProduct가 체크된 프리팹
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
        while (true) {
            if (!isProcessing) TryStartProcessing();
            yield return new WaitForSeconds(checkInterval);
        }
    }

    void TryStartProcessing() {
        if (Ingame_Manager_Resource.Instance == null) return;

        if (Ingame_Manager_Resource.Instance.resCommon >= requireResourceAmount) {
            Ingame_Manager_Resource.Instance.resCommon -= requireResourceAmount;
            Ingame_Manager_Resource.Instance.EarnGold(0); // UI Refresh
            StartCoroutine(ProcessingRoutine());
        }
    }

    IEnumerator ProcessingRoutine() {
        isProcessing = true;
        float timer = 0f;
        float animTimer = 0f;
        bool isSpriteA = true;

        while (timer < processingTime) {
            timer += Time.deltaTime;
            animTimer += Time.deltaTime;

            if (animTimer >= 0.5f) {
                animTimer = 0f;
                isSpriteA = !isSpriteA;
                if (spriteRenderer != null) 
                    spriteRenderer.sprite = isSpriteA ? spriteIdle : spriteActive;
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
        
        Vector3Int targetCell = FindEmptyTile(myCell, 1);
        if (targetCell == myCell) targetCell = FindEmptyTile(myCell, 2);

        Vector3 targetWorldPos = buildMgr.tilemapInstallations.GetCellCenterWorld(targetCell);
        targetWorldPos.z = -2f; 

        GameObject productObj = Instantiate(productPrefab, transform.position, Quaternion.identity);
        
        // 이름이 Ingame_Item_Dropped로 유지됨
        Ingame_Item_Dropped itemScript = productObj.GetComponent<Ingame_Item_Dropped>();
        if (itemScript != null) {
            itemScript.SetDropTarget(targetWorldPos);
        }
    }

    Vector3Int FindEmptyTile(Vector3Int center, int range) {
        var buildMgr = Ingame_Manager_Build.Instance;
        List<Vector3Int> candidates = new List<Vector3Int>();

        for (int x = -range; x <= range; x++) {
            for (int y = -range; y <= range; y++) {
                if (Mathf.Abs(x) < range && Mathf.Abs(y) < range) continue;
                if (x == 0 && y == 0) continue; 

                Vector3Int checkPos = center + new Vector3Int(x, y, 0);
                if (!buildMgr.IsOccupied(checkPos)) candidates.Add(checkPos);
            }
        }

        if (candidates.Count > 0) return candidates[Random.Range(0, candidates.Count)];
        return center;
    }
}