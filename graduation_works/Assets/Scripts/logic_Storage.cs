using UnityEngine;

[RequireComponent(typeof(BoxCollider2D))] // 콜라이더가 무조건 있어야 함
public class logic_Storage : logic_CodingBase {
    
    [Header("저장소 설정")]
    public bool acceptResource = true; // 체크하면 자원(돌, 철 등)을 먹음
    public bool acceptProduct = true;  // 체크하면 상품(골드)을 먹음

    void Start() {
        BoxCollider2D col = GetComponent<BoxCollider2D>();
        if (col != null) {
            col.isTrigger = true; 
        }
    }

    // 저장소는 딱히 코딩이 필요 없으니 무조건 통과!
    public override CodeState ValidateCode(string code) {
        return CodeState.Valid; 
    }

    private void OnTriggerEnter2D(Collider2D collision) {
        // 닿은 녀석이 '드롭된 아이템'인지 확인
        Ingame_Item_Dropped item = collision.GetComponent<Ingame_Item_Dropped>();
        
        if (item != null) {
            // 이 건물이 설정상 먹을 수 있는 종류인지 확인
            if (item.isProduct && acceptProduct) {
                item.CollectItem(); // 꿀꺽 (골드 획득 및 파괴)
            }
            else if (!item.isProduct && acceptResource) {
                item.CollectItem(); // 꿀꺽 (자원 획득 및 파괴)
            }
        }
    }
}