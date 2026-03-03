using System.Text.RegularExpressions;

namespace MbCnsTool.Core.Services;

/// <summary>
/// 文本过滤规则。
/// </summary>
public static partial class TextRules
{
    private static readonly HashSet<string> CandidateKeys =
    [
        "text", "title", "description", "desc", "caption", "label", "tooltip", "message", "line", "content", "hint", "summary"
    ];

    /// <summary>
    /// 判断字符串是否值得翻译。
    /// </summary>
    public static bool IsTranslatableString(string text)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return false;
        }

        if (text.Length <= 1)
        {
            return false;
        }

        if (text.StartsWith('@'))
        {
            return false;
        }

        if (LooksLikeIdentifierRegex().IsMatch(text))
        {
            return false;
        }

        if (!ContainsLatinRegex().IsMatch(text))
        {
            return false;
        }

        return true;
    }

    /// <summary>
    /// 判断键名是否常见文本字段。
    /// </summary>
    public static bool IsCandidateKey(string keyName)
    {
        if (string.IsNullOrWhiteSpace(keyName))
        {
            return false;
        }

        var normalized = keyName.ToLowerInvariant();
        return CandidateKeys.Contains(normalized) ||
               normalized.Contains("text", StringComparison.OrdinalIgnoreCase) ||
               normalized.Contains("description", StringComparison.OrdinalIgnoreCase) ||
               normalized.Contains("title", StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// 检查文本是否携带骑砍翻译接口（{=id}xxx）。
    /// </summary>
    public static string? ExtractTranslationId(string text)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return null;
        }

        var match = TranslationIdRegex().Match(text.Trim());
        return match.Success ? match.Groups["id"].Value : null;
    }

    /// <summary>
    /// 移除翻译接口前缀，返回纯文本部分。
    /// </summary>
    public static string StripTranslationIdPrefix(string text)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return string.Empty;
        }

        return TranslationIdRegex().Replace(text.Trim(), string.Empty, 1).Trim();
    }

    [GeneratedRegex(@"[A-Za-z]")]
    private static partial Regex ContainsLatinRegex();

    [GeneratedRegex(@"^\{=(?<id>[^}]+)\}")]
    private static partial Regex TranslationIdRegex();

    [GeneratedRegex(@"^[a-z0-9_./-]+$", RegexOptions.IgnoreCase)]
    private static partial Regex LooksLikeIdentifierRegex();
}
