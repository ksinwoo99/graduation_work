using UnityEngine;

[RequireComponent(typeof(BoxCollider2D))] 
public class logic_Storage : logic_CodingBase {
    
    [Header("저장소 설정")]
    public bool acceptResource = true; 
    public bool acceptProduct = true;  

    void Start() {
        BoxCollider2D col = GetComponent<BoxCollider2D>();
        if (col != null) col.isTrigger = true; 

        // ✨ [추가] 창고는 기본 정지 규칙을 무시하고 항상 켜져있게 만듭니다.
        isOperating = true;
        UpdateStatusUI();
    }

    public override CodeState ValidateCode(string code) {
        return CodeState.Valid; 
    }

    private void OnTriggerEnter2D(Collider2D collision) {
        Ingame_Item_Dropped item = collision.GetComponent<Ingame_Item_Dropped>();
        if (item != null) {
            if (item.isProduct && acceptProduct) {
                if (Ingame_Manager_Quest.Instance != null) Ingame_Manager_Quest.Instance.AddMarketProgress(1); 
                item.CollectItem(); 
            }
            else if (!item.isProduct && acceptResource) {
                if (Ingame_Manager_Quest.Instance != null) Ingame_Manager_Quest.Instance.AddStorageProgress(1);
                item.CollectItem(); 
            }
        }
    }
    
    public override void ToggleOperation() {
        isOperating = true; 
    }
}