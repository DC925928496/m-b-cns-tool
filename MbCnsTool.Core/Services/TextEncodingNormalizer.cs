using System.Text;

namespace MbCnsTool.Core.Services;

/// <summary>
/// 文本编码归一化工具，用于修复常见 UTF-8/1252 串码。
/// </summary>
public static class TextEncodingNormalizer
{
    static TextEncodingNormalizer()
    {
        Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
        Encoding1252 = Encoding.GetEncoding(1252);
    }

    /// <summary>
    /// 修复常见乱码，如 â€” -> —。
    /// </summary>
    public static string NormalizeMojibake(string text)
    {
        if (string.IsNullOrEmpty(text))
        {
            return text;
        }

        var sourceScore = GetMojibakeScore(text);
        if (sourceScore == 0)
        {
            return text;
        }

        var best = text;
        var bestScore = sourceScore;

        TryUseCandidate(TryDecode(text, Encoding.Latin1), ref best, ref bestScore);
        TryUseCandidate(TryDecode(text, Encoding1252), ref best, ref bestScore);
        return best;
    }

    private static void TryUseCandidate(string? candidate, ref string best, ref int bestScore)
    {
        if (string.IsNullOrWhiteSpace(candidate))
        {
            return;
        }

        var score = GetMojibakeScore(candidate);
        if (score >= bestScore)
        {
            return;
        }

        best = candidate;
        bestScore = score;
    }

    private static string? TryDecode(string text, Encoding sourceEncoding)
    {
        try
        {
            var bytes = sourceEncoding.GetBytes(text);
            var decoded = Encoding.UTF8.GetString(bytes);
            return decoded.Contains('\uFFFD') ? null : decoded;
        }
        catch
        {
            return null;
        }
    }

    private static int GetMojibakeScore(string text)
    {
        var score = 0;
        foreach (var character in text)
        {
            if (character is 'Ã' or 'Â' or 'â' or 'ð' or '\uFFFD')
            {
                score += 3;
                continue;
            }

            if (character is >= '\u0080' and <= '\u009F')
            {
                score += 4;
            }
        }

        return score;
    }

    private static readonly Encoding Encoding1252;
}
