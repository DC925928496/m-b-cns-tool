using System.Text;
using System.Text.Json;
using MbCnsTool.Core.Models;

namespace MbCnsTool.Core.Services;

/// <summary>
/// 运行时注入映射文件读写服务。
/// </summary>
public sealed class RuntimeLocalizationMapService
{
    public async Task<RuntimeLocalizationMap?> TryLoadAsync(string path, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(path) || !File.Exists(path))
        {
            return null;
        }

        await using var stream = File.OpenRead(path);
        var map = await JsonSerializer.DeserializeAsync<RuntimeLocalizationMap>(stream, JsonOptions, cancellationToken);
        if (map is null)
        {
            return null;
        }

        NormalizeEntries(map.Entries);
        return map;
    }

    public async Task SaveAsync(string path, RuntimeLocalizationMap map, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(map);

        var parent = Path.GetDirectoryName(path);
        if (!string.IsNullOrWhiteSpace(parent))
        {
            Directory.CreateDirectory(parent);
        }

        NormalizeEntries(map.Entries);

        await using var stream = File.Create(path);
        await JsonSerializer.SerializeAsync(stream, map, JsonOptions, cancellationToken);
        await stream.FlushAsync(cancellationToken);
    }

    private static void NormalizeEntries(IReadOnlyList<RuntimeLocalizationEntry> entries)
    {
        foreach (var entry in entries)
        {
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

