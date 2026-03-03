using System.Text.Json;
using MbCnsTool.Core.Models;

namespace MbCnsTool.Core.Services;

/// <summary>
/// 翻译对比数据读写与缓存回灌服务。
/// </summary>
public sealed class TranslationReviewService
{
    /// <summary>
    /// 生成默认翻译对比文件路径。
    /// </summary>
    public static string ResolveDefaultPath(string outputPath, string moduleRootPath)
    {
        var moduleName = new DirectoryInfo(moduleRootPath).Name;
        return Path.Combine(outputPath, "review", $"{moduleName}.translation_review.json");
    }

    /// <summary>
    /// 尝试加载翻译对比文件。
    /// </summary>
    public async Task<TranslationReviewSnapshot?> TryLoadAsync(string filePath, CancellationToken cancellationToken)
    {
        if (!File.Exists(filePath))
        {
            return null;
        }

        await using var stream = File.OpenRead(filePath);
        var snapshot = await JsonSerializer.DeserializeAsync<TranslationReviewSnapshot>(stream, JsonOptions, cancellationToken);
        return snapshot;
    }

    /// <summary>
    /// 保存翻译对比文件。
    /// </summary>
    public async Task SaveAsync(string filePath, TranslationReviewSnapshot snapshot, CancellationToken cancellationToken)
    {
        var parent = Path.GetDirectoryName(filePath);
        if (!string.IsNullOrWhiteSpace(parent))
        {
            Directory.CreateDirectory(parent);
        }

        await using var stream = File.Create(filePath);
        await JsonSerializer.SerializeAsync(stream, snapshot, JsonOptions, cancellationToken);
        await stream.FlushAsync(cancellationToken);
    }

    /// <summary>
    /// 将翻译对比数据写回缓存。
    /// </summary>
    public async Task<int> ApplySnapshotToCacheAsync(TranslationReviewSnapshot snapshot, TranslationCache cache, CancellationToken cancellationToken)
    {
        var updated = 0;
        var placeholderProtector = new PlaceholderProtector();
        foreach (var entry in snapshot.Entries)
        {
            if (string.IsNullOrWhiteSpace(entry.TargetText))
            {
                continue;
            }

            var translated = entry.TargetText.Trim();
            if (!placeholderProtector.IsPlaceholderSafe(entry.SourceText, translated) ||
                !placeholderProtector.IsDoubleBraceBlockSafe(entry.SourceText, translated))
            {
                continue;
            }

            await cache.UpsertAsync(entry.CacheKey, "manual_review", entry.SourceText, translated, cancellationToken);
            updated++;
        }

        return updated;
    }

    /// <summary>
    /// 基于当前扫描结果构建翻译对比快照。
    /// </summary>
    public TranslationReviewSnapshot BuildSnapshot(
        ScanBundle bundle,
        IReadOnlyDictionary<string, string> runtimeMap,
        TranslationRunOptions options)
    {
        var merged = new Dictionary<string, TranslationReviewEntry>(StringComparer.Ordinal);

        foreach (var unit in bundle.TextUnits)
        {
            var cacheKey = unit.BuildCacheKey(options);
            if (merged.ContainsKey(cacheKey))
            {
                continue;
            }

            merged[cacheKey] = new TranslationReviewEntry
            {
                CacheKey = cacheKey,
                Category = unit.Category.ToString(),
                RelativePath = unit.RelativePath,
                FieldPath = unit.FieldPath,
                SourceText = unit.SourceText,
                TargetText = unit.ReadCurrentText(),
                IsDllLiteral = false
            };
        }

        foreach (var literal in bundle.DllLiterals)
        {
            var cacheKey = TranslationCacheKeyBuilder.BuildDllKey(literal.SourceText, options);
            if (merged.ContainsKey(cacheKey))
            {
                continue;
            }

            runtimeMap.TryGetValue(literal.SourceText, out var translated);
            merged[cacheKey] = new TranslationReviewEntry
            {
                CacheKey = cacheKey,
                Category = TextCategory.系统.ToString(),
                RelativePath = literal.AssemblyName,
                FieldPath = $"{literal.TypeName}.{literal.MethodName}",
                SourceText = literal.SourceText,
                TargetText = translated ?? literal.SourceText,
                IsDllLiteral = true
            };
        }

        return new TranslationReviewSnapshot
        {
            ModuleName = new DirectoryInfo(bundle.ModuleRootPath).Name,
            StyleProfile = options.StyleProfile,
            TargetLanguage = options.TargetLanguage,
            GeneratedAtUtc = DateTimeOffset.UtcNow,
            Entries = merged.Values
                .OrderBy(entry => entry.RelativePath, StringComparer.OrdinalIgnoreCase)
                .ThenBy(entry => entry.FieldPath, StringComparer.Ordinal)
                .ToArray()
        };
    }

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = true,
        Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping
    };
}
