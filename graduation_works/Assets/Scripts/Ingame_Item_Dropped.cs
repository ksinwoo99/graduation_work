using UnityEngine;
using TMPro;
using System.Collections;

public class Ingame_Item_Dropped : MonoBehaviour
{
    [Header("모드 설정")]
    public bool isProduct = false; // 체크하면 '상품(골드)', 끄면 '자원'

    [Header("자원 모드 (Is Product = false)")]
    public ResourceType resourceType = ResourceType.Common;
    public int amount = 1; 
    
    [Header("상품 모드 (Is Product = true)")]
    public int sellPrice = 100;

    [Header("연출 설정")]
    public float popHeight = 0.8f;   
    public float popDuration = 0.5f; 

    private Vector3? forcedTargetPos = null;

    public void SetDropTarget(Vector3 target) {
        forcedTargetPos = target;
    }

    void Start() {
        Vector3 startPos = transform.position;
        // 목표 지점이 있으면 거기로, 없으면 랜덤 (기존 로직 유지)
        Vector3 targetPos = forcedTargetPos.HasValue 
                            ? forcedTargetPos.Value 
                            : startPos + (Vector3)Random.insideUnitCircle * 0.8f;
                            
        StartCoroutine(PopAnimation(targetPos));
    }

    private void OnMouseDown() {
        if (Ingame_Manager_Build.Instance == null) {
            Destroy(gameObject);
            return;
        }

        if (isProduct) {
            // 💰 [상품] 클릭 시 골드 획득
            if (Ingame_Manager_Resource.Instance != null)
                Ingame_Manager_Resource.Instance.EarnGold(sellPrice);

            string msg = $"<color=#FFD700>+{sellPrice} G</color>"; // 금색 텍스트
            Ingame_Manager_Build.Instance.ShowFloatingText(msg, transform.position);
        }
        else {
            // ⛏️ [자원] 클릭 시 자원 획득
            if (Ingame_Manager_Resource.Instance != null)
                Ingame_Manager_Resource.Instance.AddResource(resourceType, amount);

            string krName = GetKoreanName(resourceType);
            string colorCode = (resourceType == ResourceType.Common) ? "#00FF00" : "#FFFFFF";
            if (resourceType == ResourceType.Rare) colorCode = "#00FFFF";

            string msg = $"<color={colorCode}>{krName} +{amount}</color>";
            Ingame_Manager_Build.Instance.ShowFloatingText(msg, transform.position);
        }

        Destroy(gameObject);
    }

    string GetKoreanName(ResourceType type) {
        switch (type) {
            case ResourceType.Common: return "기본 자원";
            case ResourceType.Uncommon: return "고급 자원";
            case ResourceType.Rare: return "희귀 자원";
            case ResourceType.Legendary: return "전설 자원";
            default: return "알 수 없음";
        }
    }

    IEnumerator PopAnimation(Vector3 targetPos) {
        Vector3 startPos = transform.position;
        float timer = 0;
        while (timer < popDuration) {
            timer += Time.deltaTime;
            float progress = timer / popDuration;
            float height = Mathf.Sin(progress * Mathf.PI) * popHeight;
            transform.position = Vector3.Lerp(startPos, targetPos, progress) + Vector3.up * height;
            yield return null;
        }
        transform.position = targetPos;
    }
}