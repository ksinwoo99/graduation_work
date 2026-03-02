using UnityEngine;
using UnityEngine.Tilemaps;
using System.Collections.Generic;

// 🔥 [추가] 인스펙터에서 자원 종류와 필요 개수를 묶어서 보여주기 위한 클래스
[System.Serializable]
public class ResourceCost
{
    public ResourceType resourceType; // 자원 종류 (Common, Rare 등)
    public int amount;                // 필요한 개수
}

// 🗂️ 모든 아이템 버튼의 공통 조상
public class Iteminfo_Base : MonoBehaviour
{
    [Header("📝 기본 정보")]
    public string machineName = "기계 이름";
    
    [TextArea(2, 4)] 
    public string codeSyntax = "사용 예시:\nmining()";
    
    [Header("💰 설치 비용")]
    public int buildCost = 100; // 기존에 쓰던 골드 비용
    
    // 🔥 [추가] 골드 외에 추가로 소모할 자원들의 목록
    public List<ResourceCost> requiredResources = new List<ResourceCost>();
    
    [Header("🏗️ 설치 정보")]
    public GameObject machinePrefab; // 실제 설치될 프리팹 (logic 스크립트가 붙어있음)
    public TileBase iconTile;        // 마우스 커서에 따라다닐 그림

    public logic_CodingBase GetLogicFromPrefab()
    {
        if (machinePrefab != null)
            return machinePrefab.GetComponentInChildren<logic_CodingBase>(); 
        return null;
    }
}