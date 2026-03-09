using UnityEngine;

// 🔥 핵심: 부모를 logic_Miner_Common으로 바꿨습니다! (게임 시스템이 얘도 채굴기로 인식함)
public class logic_Miner_Advanced : logic_Miner_Common 
{
    [Header("고급 채굴 드롭 설정")]
    public GameObject droppedRarePrefab; // 30% 확률로 나올 희귀(Special) 프리팹

    // 문법 검사(ValidateCode)와 애니메이션(MiningRoutine)은 부모 코드를 100% 재사용합니다!

    // 🔥 자원을 소환하는 부분만 가로채서 30% / 70% 확률 로직으로 덮어씁니다.
    protected override void SpawnResource() 
    {
        if (Ingame_Manager_Build.Instance == null) return;

        var buildMgr = Ingame_Manager_Build.Instance;
        Vector3Int myCell = buildMgr.tilemapInstallations.WorldToCell(transform.position);
        Vector3 targetDropPos = buildMgr.GetDropPosition(myCell);

        Vector3 spawnPos = transform.position;
        spawnPos.y -= 0.5f; 
        spawnPos.z = -1f;

        // 30% 확률로 희귀 드랍 계산
        int rand = Random.Range(0, 100); 
        GameObject prefabToDrop = null;

        if (rand < 30) {
            prefabToDrop = droppedRarePrefab; // 30% 당첨! 희귀(Special) 프리팹
        } else {
            // 🔥 70% 꽝! 부모가 이미 가지고 있는 기본 프리팹(droppedItemPrefab) 변수를 그대로 씁니다.
            prefabToDrop = droppedItemPrefab; 
        }

        if (prefabToDrop == null) return; 

        GameObject itemObj = Instantiate(prefabToDrop, spawnPos, Quaternion.identity);
        Ingame_Item_Dropped itemScript = itemObj.GetComponent<Ingame_Item_Dropped>();
        
        if (itemScript != null) {
            itemScript.SetDropTarget(targetDropPos);
        }
    }
}