using System.Text.RegularExpressions;

namespace MbCnsTool.RuntimeLocalization;

internal static class PlaceholderSafety
{
    private static readonly Regex DoubleBraceRegex = new(@"\{\{[^{}]*\}\}", RegexOptions.Compiled);
    private static readonly Regex SingleBraceRegex = new(@"\{[^{}]+\}", RegexOptions.Compiled);
    private static readonly Regex AngleRegex = new(@"<[^<>]+>", RegexOptions.Compiled);
    private static readonly Regex PercentRegex = new(@"%[^%]+%", RegexOptions.Compiled);

    public static bool IsSafe(string source, string target)
    {
        if (source is null || target is null)
        {
            return false;
        }

        if (string.Equals(source, target, StringComparison.Ordinal))
        {
            return true;
        }

        return IsTokenMultisetEqual(source, target, DoubleBraceRegex) &&
               IsTokenMultisetEqual(RemoveDoubleBraces(source), RemoveDoubleBraces(target), SingleBraceRegex) &&
               IsTokenMultisetEqual(source, target, AngleRegex) &&
               IsTokenMultisetEqual(source, target, PercentRegex);
    }

    private static string RemoveDoubleBraces(string text)
    {
        return string.IsNullOrEmpty(text) ? string.Empty : DoubleBraceRegex.Replace(text, string.Empty);
    }

    private static bool IsTokenMultisetEqual(string source, string target, Regex regex)
    {
        var sourceTokens = ExtractMultiset(source, regex);
        var targetTokens = ExtractMultiset(target, regex);
        if (sourceTokens.Count != targetTokens.Count)
        {
            return false;
        }

        foreach (var pair in sourceTokens)
        {
            if (!targetTokens.TryGetValue(pair.Key, out var targetCount) || targetCount != pair.Value)
            {
                return false;
            }
        }

        return true;
    }

    private static Dictionary<string, int> ExtractMultiset(string text, Regex regex)
    {
        var result = new Dictionary<string, int>(StringComparer.Ordinal);
        if (string.IsNullOrEmpty(text))
        {
            return result;
        }

        foreach (Match match in regex.Matches(text))
        {
            var value = match.Value;
            if (result.TryGetValue(value, out var count))
            {
                result[value] = count + 1;
            }
            else
            {
                result[value] = 1;
            }
        }

        return result;
    }
}
