using System.Text;

namespace MbCnsTool.Core.Services;

/// <summary>
/// 术语表服务，支持加载与统一替换。
/// </summary>
public sealed class GlossaryService
{
    private readonly IReadOnlyList<KeyValuePair<string, string>> _terms;

    private GlossaryService(IReadOnlyList<KeyValuePair<string, string>> terms)
    {
        _terms = terms;
    }

    /// <summary>
    /// 空术语表实例。
    /// </summary>
    public static GlossaryService Empty { get; } = new([]);

    /// <summary>
    /// 从文本文件加载术语表，支持 "=" 或 "," 分隔。
    /// </summary>
    public static async Task<GlossaryService> LoadAsync(string? path, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(path) || !File.Exists(path))
        {
            return Empty;
        }

        var lines = await File.ReadAllLinesAsync(path, cancellationToken);
        var items = new List<KeyValuePair<string, string>>();

        foreach (var rawLine in lines)
        {
            var line = rawLine.Trim();
            if (line.Length == 0 || line.StartsWith('#'))
            {
                continue;
            }

            var separator = line.Contains('=') ? '=' : ',';
            var split = line.Split(separator, 2, StringSplitOptions.TrimEntries);
            if (split.Length != 2 || split[0].Length == 0 || split[1].Length == 0)
            {
                continue;
            }

            items.Add(new KeyValuePair<string, string>(split[0], split[1]));
        }

        return new GlossaryService(items);
    }

    /// <summary>
    /// 获取提示词使用的术语文本。
    /// </summary>
    public string BuildPrompt()
    {
        if (_terms.Count == 0)
        {
            return string.Empty;
        }

        var builder = new StringBuilder();
        foreach (var (source, target) in _terms)
        {
            builder.Append(source).Append('=').Append(target).Append("; ");
        }

        return builder.ToString().Trim();
    }

    /// <summary>
    /// 对译文执行术语统一替换。
    /// </summary>
    public string ApplyToTranslation(string translated)
    {
        var text = translated;
        foreach (var (source, target) in _terms)
        {
            text = text.Replace(source, target, StringComparison.OrdinalIgnoreCase);
        }

        return text;
    }
}
