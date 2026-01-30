using UnityEngine;
using TMPro;
using System.Collections;

public class Ingame_Item_Dropped : MonoBehaviour
{
    [Header("자원 정보")]
    public ResourceType resourceType = ResourceType.Common;
    public int amount = 20; 
    
    [Header("튀어오르는 연출")]
    public float popHeight = 0.8f;   
    public float popDuration = 0.6f; 

    void Start()
    {
        StartCoroutine(PopAnimation());
    }

    private void OnMouseDown()
    {
        if (Ingame_Manager_Resource.Instance != null)
        {
            Ingame_Manager_Resource.Instance.AddResource(resourceType, amount);
            Debug.Log($"자원 획득: {resourceType} +{amount}");
        }

        if (Ingame_Manager_Build.Instance != null)
        {
            string krName = GetKoreanName(resourceType);
            
            // 색상 설정
            string colorCode = "#FFFFFF"; 
            if (resourceType == ResourceType.Common) colorCode = "#00FF00"; // 초록
            else if (resourceType == ResourceType.Rare) colorCode = "#00FFFF"; // 하늘

            string msg = $"<color={colorCode}>{krName} +{amount}</color>";
            
            Ingame_Manager_Build.Instance.ShowFloatingText(msg, transform.position);
        }

        Destroy(gameObject);
    }

    string GetKoreanName(ResourceType type)
    {
        switch (type)
        {
            case ResourceType.Common: return "기본 자원";
            case ResourceType.Uncommon: return "고급 자원";
            case ResourceType.Rare: return "희귀 자원";
            case ResourceType.Legendary: return "전설 자원";
            default: return "알 수 없음";
        }
    }

    IEnumerator PopAnimation()
    {
        Vector3 startPos = transform.position;
        Vector3 targetPos = startPos + (Vector3)Random.insideUnitCircle * 0.8f; 

        float timer = 0;
        while (timer < popDuration)
        {
            timer += Time.deltaTime;
            float progress = timer / popDuration;
            float height = Mathf.Sin(progress * Mathf.PI) * popHeight;
            transform.position = Vector3.Lerp(startPos, targetPos, progress) + Vector3.up * height;
            yield return null;
        }
    }
}