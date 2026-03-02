using System.Text.RegularExpressions;

namespace MbCnsTool.Core.Services;

/// <summary>
/// 文本过滤规则。
/// </summary>
public static partial class TextRules
{
    private static readonly HashSet<string> CandidateKeys =
    [
        "text", "name", "title", "description", "desc", "caption", "label", "tooltip", "message", "line", "content"
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

        return CandidateKeys.Contains(keyName.ToLowerInvariant()) || keyName.Contains("text", StringComparison.OrdinalIgnoreCase);
    }

    [GeneratedRegex(@"[A-Za-z]")]
    private static partial Regex ContainsLatinRegex();
}
