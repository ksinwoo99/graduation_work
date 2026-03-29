using UnityEngine;

[RequireComponent(typeof(BoxCollider2D))] 
public class logic_Storage : logic_CodingBase {
    
    [Header("저장소 설정")]
    public bool acceptResource = true; 
    public bool acceptProduct = true;  

    void Start() {
        BoxCollider2D col = GetComponent<BoxCollider2D>();
        if (col != null) {
            col.isTrigger = true; 
        }
    }

    public override CodeState ValidateCode(string code) {
        return CodeState.Valid; 
    }

    private void OnTriggerEnter2D(Collider2D collision) {
        Ingame_Item_Dropped item = collision.GetComponent<Ingame_Item_Dropped>();
        
        if (item != null) {
            // 1. 판매소(Market)가 상품(골드)을 먹었을 때
            if (item.isProduct && acceptProduct) {
                
                // ✨ 퀘스트 매니저에 판매 진행도 추가!
                // (만약 아이템마다 획득하는 골드량이 다르다면, 1 대신 item.price 같은 변수를 넣어주세요)
                if (Ingame_Manager_Quest.Instance != null) {
                    Ingame_Manager_Quest.Instance.AddMarketProgress(1); 
                }

                item.CollectItem(); 
            }
            // 2. 창고(Storage)가 자원을 먹었을 때
            else if (!item.isProduct && acceptResource) {
                
                // ✨ 퀘스트 매니저에 수집 진행도 추가!
                // (아이템별 자원량이 다르면 1 대신 item.amount를 넣어주세요)
                if (Ingame_Manager_Quest.Instance != null) {
                    Ingame_Manager_Quest.Instance.AddStorageProgress(1);
                }

                item.CollectItem(); 
            }
        }
    }
}