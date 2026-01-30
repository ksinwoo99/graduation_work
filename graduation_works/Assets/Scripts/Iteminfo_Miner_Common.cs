using UnityEngine;

// 🏷️ 버튼에 붙이는 스크립트: 일반 채굴기 전용 정보
public class Iteminfo_Miner_Common : Iteminfo_Base
{
    void Reset() // 컴포넌트 추가할 때 자동으로 기본값 채워주기
    {
        machineName = "일반 채굴기";
        buildCost = 100;
    }
}