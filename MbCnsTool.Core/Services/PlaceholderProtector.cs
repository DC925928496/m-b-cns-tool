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
    /// 校验占位符集合是否一致（内容不允许被改写）。
    /// </summary>
    public bool IsPlaceholderSafe(string source, string translated)
    {
        var sourceTokens = PlaceholderRegex().Matches(source).Select(match => match.Value).ToArray();
        var translatedTokens = PlaceholderRegex().Matches(translated).Select(match => match.Value).ToArray();
        if (sourceTokens.Length != translatedTokens.Length)
        {
            return false;
        }

        var sourceMap = sourceTokens
            .GroupBy(token => token, StringComparer.Ordinal)
            .ToDictionary(group => group.Key, group => group.Count(), StringComparer.Ordinal);
        var translatedMap = translatedTokens
            .GroupBy(token => token, StringComparer.Ordinal)
            .ToDictionary(group => group.Key, group => group.Count(), StringComparer.Ordinal);
        if (sourceMap.Count != translatedMap.Count)
        {
            return false;
        }

        foreach (var (token, count) in sourceMap)
        {
            if (!translatedMap.TryGetValue(token, out var translatedCount) || translatedCount != count)
            {
                return false;
            }
        }

        return true;
    }

    /// <summary>
    /// 校验双花括号块内容是否被篡改。
    /// </summary>
    public bool IsDoubleBraceBlockSafe(string source, string translated)
    {
        var sourceBlocks = DoubleBraceBlockRegex().Matches(source).Select(match => match.Value).ToArray();
        var translatedBlocks = DoubleBraceBlockRegex().Matches(translated).Select(match => match.Value).ToArray();
        if (sourceBlocks.Length != translatedBlocks.Length)
        {
            return false;
        }

        for (var index = 0; index < sourceBlocks.Length; index++)
        {
            if (!sourceBlocks[index].Equals(translatedBlocks[index], StringComparison.Ordinal))
            {
                return false;
            }
        }

        return true;
    }

    [GeneratedRegex(@"\{\{[\s\S]*?\}\}|\{[^{}]+\}|<[^<>]+>|%[^%\s]+%|__PH_\d+__")]
    private static partial Regex PlaceholderRegex();

    [GeneratedRegex(@"\{\{[\s\S]*?\}\}")]
    private static partial Regex DoubleBraceBlockRegex();
}

/// <summary>
/// 受保护文本结构。
/// </summary>
/// <param name="Text">替换后的文本。</param>
/// <param name="Tokens">占位符列表。</param>
public sealed record ProtectedText(string Text, IReadOnlyList<string> Tokens);
