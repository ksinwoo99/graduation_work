using System.Text;
using System.Text.RegularExpressions;

/// <summary>
/// 2차 게임 문법 검사 — 기계 등급에 맞는 mining / producting 인자만 허용합니다.
/// </summary>
public static class GameCodeValidator
{
    static readonly Regex MiningCallRegex =
        new Regex(@"mining\s*\(\s*([^)]*)\s*\)", RegexOptions.IgnoreCase | RegexOptions.Compiled);

    static readonly Regex ProductingCallRegex =
        new Regex(
            @"producting\s*\(\s*([^,)]+)\s*,\s*['""]?([ab])['""]?\s*\)",
            RegexOptions.IgnoreCase | RegexOptions.Compiled);

    static readonly Regex RequiredMiningArgRegex =
        new Regex(@"mining\s*\(\s*([^)]*)\s*\)", RegexOptions.IgnoreCase | RegexOptions.Compiled);

    public static string StripCommentsAndStringLiterals(string code)
    {
        if (string.IsNullOrEmpty(code)) return string.Empty;

        var sb = new StringBuilder(code.Length);
        foreach (string line in code.Split('\n'))
        {
            bool inSingle = false;
            bool inDouble = false;
            for (int i = 0; i < line.Length; i++)
            {
                char c = line[i];
                if (!inSingle && !inDouble && c == '#') break;
                if (!inDouble && c == '\'') { inSingle = !inSingle; sb.Append(' '); continue; }
                if (!inSingle && c == '"')  { inDouble = !inDouble; sb.Append(' '); continue; }
                sb.Append((inSingle || inDouble) ? ' ' : c);
            }
            sb.Append('\n');
        }
        return sb.ToString();
    }

    /// <summary>requiredSyntax 예: mining(resCommon) — 코드 내 모든 mining 호출이 동일 인자인지 검사.</summary>
    public static bool AllMiningCallsMatch(string code, string requiredSyntax)
    {
        if (string.IsNullOrWhiteSpace(requiredSyntax)) return true;

        Match req = RequiredMiningArgRegex.Match(requiredSyntax.Replace(" ", ""));
        if (!req.Success) return true;

        string expected = NormalizeToken(req.Groups[1].Value);
        string sanitized = StripCommentsAndStringLiterals(code);
        MatchCollection calls = MiningCallRegex.Matches(sanitized);
        if (calls.Count == 0) return false;

        foreach (Match call in calls)
        {
            if (NormalizeToken(call.Groups[1].Value) != expected)
                return false;
        }
        return true;
    }

    /// <summary>producting 첫 인자가 common|rare|special|exotic 중 allowedTier 와 일치하는지 검사.</summary>
    public static bool AllProductingCallsMatch(string code, string allowedTier)
    {
        if (string.IsNullOrWhiteSpace(allowedTier)) return true;

        string expected = allowedTier.Trim().ToLower();
        string sanitized = StripCommentsAndStringLiterals(code);
        MatchCollection calls = ProductingCallRegex.Matches(sanitized);
        if (calls.Count == 0) return true;

        foreach (Match call in calls)
        {
            string tier = NormalizeToken(call.Groups[1].Value);
            if (tier != expected)
                return false;
        }
        return true;
    }

    public static string GetProductingTierForMachine(string machineName)
    {
        if (string.IsNullOrEmpty(machineName)) return "";
        if (machineName.IndexOf("Common", System.StringComparison.OrdinalIgnoreCase) >= 0) return "common";
        if (machineName.IndexOf("Advanced", System.StringComparison.OrdinalIgnoreCase) >= 0) return "rare";
        if (machineName.IndexOf("Hightech", System.StringComparison.OrdinalIgnoreCase) >= 0) return "special";
        if (machineName.IndexOf("Superior", System.StringComparison.OrdinalIgnoreCase) >= 0) return "exotic";
        return "";
    }

    public static string NormalizeProductingTierToken(string raw)
    {
        string t = NormalizeToken(raw);
        switch (t)
        {
            case "advanced": return "rare";
            case "hightech": return "special";
            case "superior": return "exotic";
            default: return t;
        }
    }

    static string NormalizeToken(string raw) =>
        Regex.Replace(raw ?? "", @"\s+", "").ToLower();
}
