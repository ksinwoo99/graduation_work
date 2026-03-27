using UnityEngine;
using UnityEngine.Tilemaps;
using System.Collections.Generic;

// 인스펙터에서 자원 종류와 필요 개수를 묶어서 보여주기 위한 클래스
[System.Serializable]
public class ResourceCost
{
    public ResourceType resourceType; 
    public int amount;                
}

// 🗂️ 모든 아이템 버튼의 공통 조상
public class Iteminfo_Base : MonoBehaviour
{
    [Header("📝 기본 정보")]
    public string machineName = "기계 이름";
    
    [TextArea(2, 4)] 
    public string codeSyntax = "사용 예시:\nmining()";

    // ✨ [추가] 건물이 차지하는 타일 크기 (기본값은 1x1)
    [Header("📐 크기 정보")]
    public Vector2Int buildingSize = new Vector2Int(1, 1);
    
    [Header("💰 설치 비용")]
    public int buildCost = 100; 
    
    public List<ResourceCost> requiredResources = new List<ResourceCost>();
    
    [Header("🏗️ 설치 정보")]
    public GameObject machinePrefab; 
    public TileBase iconTile;        

    public logic_CodingBase GetLogicFromPrefab()
    {
        if (machinePrefab != null)
            return machinePrefab.GetComponentInChildren<logic_CodingBase>(); 
        return null;
    }
}