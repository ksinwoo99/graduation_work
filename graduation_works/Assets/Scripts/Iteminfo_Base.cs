using UnityEngine;
using UnityEngine.Tilemaps;

// 🗂️ 모든 아이템 버튼의 공통 조상
public class Iteminfo_Base : MonoBehaviour
{
    [Header("📝 기본 정보")]
    public string machineName = "기계 이름";
    public int buildCost = 100;
    
    [Header("🏗️ 설치 정보")]
    public GameObject machinePrefab; // 실제 설치될 프리팹 (logic 스크립트가 붙어있음)
    public TileBase iconTile;        // 마우스 커서에 따라다닐 그림

    // 프리팹 안에 있는 로직(기계 두뇌)을 미리 꺼내보는 함수
    public logic_CodingBase GetLogicFromPrefab()
    {
        if (machinePrefab != null)
            return machinePrefab.GetComponent<logic_CodingBase>();
        return null;
    }
}