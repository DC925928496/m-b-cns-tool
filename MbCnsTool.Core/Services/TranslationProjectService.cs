using System.Text;
using System.Text.Json;
using MbCnsTool.Core.Models;

namespace MbCnsTool.Core.Services;

/// <summary>
/// 翻译工程记录文件读写服务。
/// </summary>
public sealed class TranslationProjectService
{
    /// <summary>
    /// 解析默认工程记录路径。
    /// </summary>
    public static string ResolveDefaultPath(string outputRoot, string moduleId)
    {
        return Path.Combine(outputRoot, "records", $"{moduleId}.mbcns_project.json");
    }

    /// <summary>
    /// 读取工程记录，并补齐缺失的 Base64 字段。
    /// </summary>
    public async Task<TranslationProject?> TryLoadAsync(string path, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(path) || !File.Exists(path))
        {
            return null;
        }

        await using var stream = File.OpenRead(path);
        var project = await JsonSerializer.DeserializeAsync<TranslationProject>(stream, JsonOptions, cancellationToken);
        if (project is null)
        {
            return null;
        }

        NormalizeEntries(project.Entries);
        return project;
    }

    /// <summary>
    /// 保存工程记录（UTF-8，无 BOM），并确保 Base64 字段正确。
    /// </summary>
    public async Task SaveAsync(string path, TranslationProject project, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(project);

        var parent = Path.GetDirectoryName(path);
        if (!string.IsNullOrWhiteSpace(parent))
        {
            Directory.CreateDirectory(parent);
        }

        NormalizeEntries(project.Entries);

        await using var stream = File.Create(path);
        await JsonSerializer.SerializeAsync(stream, project, JsonOptions, cancellationToken);
        await stream.FlushAsync(cancellationToken);
    }

    private static void NormalizeEntries(IReadOnlyList<TranslationProjectEntry> entries)
    {
        foreach (var entry in entries)
        {
            if (string.IsNullOrWhiteSpace(entry.SourceTextBase64))
            {
                entry.SourceTextBase64 = BuildBase64(entry.SourceText);
                continue;
            }

            var expected = BuildBase64(entry.SourceText);
            if (!string.Equals(entry.SourceTextBase64, expected, StringComparison.Ordinal))
            {
                entry.SourceTextBase64 = expected;
            }
        }
    }

    private static string BuildBase64(string text)
    {
        var bytes = Encoding.UTF8.GetBytes(text ?? string.Empty);
        return Convert.ToBase64String(bytes);
    }

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = true,
        Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping
    };
}

