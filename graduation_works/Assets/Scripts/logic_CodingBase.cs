using UnityEngine;
using UnityEngine.Tilemaps; // TileBase 사용을 위해 추가

// 🧠 모든 기계(프리팹)의 공통 두뇌
public abstract class logic_CodingBase : MonoBehaviour
{
    // 🧹 buildCost, machinePrefab 삭제됨! (Iteminfo로 이사감)
    
    [Header("기본 설정")]
    public Ingame_Manager_Build buildManager; 
    public TileBase myTile; // 설치된 후 타일맵 상호작용 등을 위해 남겨둠

    public abstract CodeState ValidateCode(string code);
    public virtual string GetDefaultCode() { return ""; }
    public enum CodeState { Empty, Error, Valid }
    
    public virtual string GetMachineName()
    {
        return gameObject.name.Replace("(Clone)", "").Trim();
    }
}