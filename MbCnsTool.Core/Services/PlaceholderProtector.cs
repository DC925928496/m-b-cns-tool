using System.Text.RegularExpressions;

namespace MbCnsTool.Core.Services;

/// <summary>
/// 保护并恢复变量占位符，防止翻译过程破坏格式。
/// </summary>
public sealed partial class PlaceholderProtector
{
    /// <summary>
    /// 将占位符替换为临时标记。
    /// </summary>
    public ProtectedText Protect(string source)
    {
        var tokens = new List<string>();
        var protectedText = PlaceholderRegex().Replace(source, match =>
        {
            var index = tokens.Count;
            tokens.Add(match.Value);
            return $"__PH_{index}__";
        });

        return new ProtectedText(protectedText, tokens);
    }

    /// <summary>
    /// 将临时标记恢复为原占位符。
    /// </summary>
    public string Restore(string translated, IReadOnlyList<string> tokens)
    {
        var restored = translated;
        for (var index = 0; index < tokens.Count; index++)
        {
            restored = restored.Replace($"__PH_{index}__", tokens[index], StringComparison.Ordinal);
        }

        return restored;
    }

    /// <summary>
    /// 校验占位符数量是否一致。
    /// </summary>
    public bool IsPlaceholderSafe(string source, string translated)
    {
        return PlaceholderRegex().Matches(source).Count == PlaceholderRegex().Matches(translated).Count;
    }

    [GeneratedRegex(@"\{[^{}]+\}|<[^<>]+>|%[^%\s]+%|__PH_\d+__")]
    private static partial Regex PlaceholderRegex();
}

/// <summary>
/// 受保护文本结构。
/// </summary>
/// <param name="Text">替换后的文本。</param>
/// <param name="Tokens">占位符列表。</param>
public sealed record ProtectedText(string Text, IReadOnlyList<string> Tokens);
