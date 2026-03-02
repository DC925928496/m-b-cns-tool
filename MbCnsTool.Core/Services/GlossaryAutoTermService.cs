using System.Text;
using System.Text.RegularExpressions;
using MbCnsTool.Core.Models;

namespace MbCnsTool.Core.Services;

/// <summary>
/// 高频词自动入术语表服务。
/// </summary>
public static partial class GlossaryAutoTermService
{
    private static readonly HashSet<string> StopWords =
    [
        "the", "and", "for", "you", "your", "with", "this", "that", "have", "from", "will", "can", "are", "was", "were",
        "not", "but", "into", "out", "their", "they", "them", "what", "when", "where", "which", "while", "would", "could"
    ];

    /// <summary>
    /// 基于文本单元自动提取高频词并写入术语表。
    /// </summary>
    public static async Task<int> AppendFrequentTermsAsync(
        string glossaryPath,
        IReadOnlyList<TextUnit> textUnits,
        CancellationToken cancellationToken,
        Func<string, CancellationToken, Task<string?>>? termTranslator = null,
        int minFrequency = 20,
        int maxTerms = 80)
    {
        var directory = Path.GetDirectoryName(glossaryPath);
        if (!string.IsNullOrWhiteSpace(directory))
        {
            Directory.CreateDirectory(directory);
        }

        if (!File.Exists(glossaryPath))
        {
            await File.WriteAllLinesAsync(glossaryPath, ["# 自动术语表"], new UTF8Encoding(false), cancellationToken);
        }

        var existingLines = await File.ReadAllLinesAsync(glossaryPath, cancellationToken);
        var existingSources = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var rawLine in existingLines)
        {
            var line = rawLine.Trim();
            if (line.Length == 0 || line.StartsWith('#'))
            {
                continue;
            }

            var separator = line.Contains('=') ? '=' : ',';
            var split = line.Split(separator, 2, StringSplitOptions.TrimEntries);
            if (split.Length == 2 && split[0].Length > 0)
            {
                existingSources.Add(split[0]);
            }
        }

        var frequency = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        foreach (var unit in textUnits)
        {
            foreach (Match match in EnglishWordRegex().Matches(unit.SourceText))
            {
                var word = match.Value.Trim();
                if (word.Length < 3)
                {
                    continue;
                }

                var lower = word.ToLowerInvariant();
                if (StopWords.Contains(lower))
                {
                    continue;
                }

                frequency[lower] = frequency.GetValueOrDefault(lower) + 1;
            }
        }

        var candidates = frequency
            .Where(entry => entry.Value >= minFrequency)
            .OrderByDescending(entry => entry.Value)
            .ThenBy(entry => entry.Key, StringComparer.Ordinal)
            .Take(maxTerms)
            .Select(entry => entry.Key)
            .Where(word => !existingSources.Contains(word))
            .ToArray();

        if (candidates.Length == 0)
        {
            return 0;
        }

        var appendLines = new List<string> { $"# 自动提取高频词 {DateTime.Now:yyyy-MM-dd HH:mm:ss}" };
        var added = 0;
        foreach (var word in candidates)
        {
            var translated = termTranslator is null
                ? word
                : (await termTranslator(word, cancellationToken))?.Trim();
            if (string.IsNullOrWhiteSpace(translated))
            {
                continue;
            }

            if (string.Equals(translated, word, StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            appendLines.Add($"{word}={translated}");
            added++;
        }

        if (added == 0)
        {
            return 0;
        }

        await File.AppendAllLinesAsync(glossaryPath, appendLines, new UTF8Encoding(false), cancellationToken);
        return added;
    }

    [GeneratedRegex(@"[A-Za-z][A-Za-z'\-]{2,}")]
    private static partial Regex EnglishWordRegex();
}
