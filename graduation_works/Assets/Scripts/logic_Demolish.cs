using UnityEngine;

public class logic_Demolish : logic_CodingBase
{
    // 철거는 코드를 검사할 필요가 없으므로 무조건 Valid 리턴
    public override CodeState ValidateCode(string code)
    {
        return CodeState.Valid;
    }

    public override string GetMachineName()
    {
        return "철거 모드";
    }
}