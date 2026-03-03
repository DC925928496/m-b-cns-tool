using System.Collections.Concurrent;
using MbCnsTool.Core.Abstractions;
using MbCnsTool.Core.Models;

namespace MbCnsTool.Core.Services;

/// <summary>
/// 翻译调度器，负责缓存、占位符保护、术语统一与多引擎回退。
/// </summary>
public sealed class TranslationOrchestrator
{
    private readonly PlaceholderProtector _placeholderProtector;
    private readonly GlossaryService _glossaryService;
    private readonly TranslationCache _cache;
    private readonly IReadOnlyDictionary<string, ITranslationProvider> _providerMap;

    /// <summary>
    /// 初始化调度器。
    /// </summary>
    public TranslationOrchestrator(
        PlaceholderProtector placeholderProtector,
        GlossaryService glossaryService,
        TranslationCache cache,
        IReadOnlyDictionary<string, ITranslationProvider> providerMap)
    {
        _placeholderProtector = placeholderProtector;
        _glossaryService = glossaryService;
        _cache = cache;
        _providerMap = providerMap;
    }

    /// <summary>
    /// 执行文本翻译并回写。
    /// </summary>
    public async Task<(int cacheHitCount, int providerCallCount)> TranslateAndApplyAsync(
        IReadOnlyList<TextUnit> textUnits,
        TranslationRunOptions options,
        CancellationToken cancellationToken,
        IProgress<string>? progress = null)
    {
        var cacheHitCount = 0;
        var providerCallCount = 0;
        var uniqueMap = textUnits
            .GroupBy(unit => unit.BuildCacheKey(options), StringComparer.Ordinal)
            .ToDictionary(group => group.Key, group => group.First(), StringComparer.Ordinal);
        var translatedMap = new ConcurrentDictionary<string, string>(StringComparer.Ordinal);
        var total = uniqueMap.Count;
        var current = 0;
        var parallelOptions = new ParallelOptions
        {
            MaxDegreeOfParallelism = ResolveConcurrency(options.MaxConcurrency),
            CancellationToken = cancellationToken
        };

        await Parallel.ForEachAsync(uniqueMap, parallelOptions, async (entry, token) =>
        {
            var (cacheKey, unit) = entry;
            var cached = await _cache.TryGetAsync(cacheKey, token);
            if (!string.IsNullOrWhiteSpace(cached) && IsTranslationSafe(unit.SourceText, cached))
            {
                translatedMap[cacheKey] = cached;
                Interlocked.Increment(ref cacheHitCount);
                ReportTextProgress(progress, ref current, total, cacheHitCount);
                return;
            }

            var protectedText = _placeholderProtector.Protect(unit.SourceText);
            var request = new TranslationProviderRequest
            {
                Text = protectedText.Text,
                Category = unit.Category,
                StyleProfile = options.StyleProfile,
                TargetLanguage = options.TargetLanguage,
                GlossaryPrompt = _glossaryService.BuildPrompt()
            };

            var translated = await TryTranslateAsync(request, options.ProviderChain, token);
            Interlocked.Increment(ref providerCallCount);
            translated ??= unit.SourceText;

            translated = _placeholderProtector.Restore(translated, protectedText.Tokens);
            translated = _glossaryService.ApplyToTranslation(translated);

            if (!IsTranslationSafe(unit.SourceText, translated))
            {
                translated = unit.SourceText;
            }

            translatedMap[cacheKey] = translated;
            await _cache.UpsertAsync(cacheKey, "chain", unit.SourceText, translated, token);
            ReportTextProgress(progress, ref current, total, cacheHitCount);
        });

        foreach (var unit in textUnits)
        {
            var key = unit.BuildCacheKey(options);
            if (translatedMap.TryGetValue(key, out var translated))
            {
                unit.ApplyTranslation(translated);
            }
        }

        return (cacheHitCount, providerCallCount);
    }

    /// <summary>
    /// 翻译 DLL 字符串映射。
    /// </summary>
    public async Task<IReadOnlyDictionary<string, string>> TranslateDllLiteralsAsync(
        IReadOnlyList<DllStringLiteral> literals,
        TranslationRunOptions options,
        CancellationToken cancellationToken,
        IProgress<string>? progress = null)
    {
        var result = new ConcurrentDictionary<string, string>(StringComparer.Ordinal);
        var total = literals.Count;
        var current = 0;
        var parallelOptions = new ParallelOptions
        {
            MaxDegreeOfParallelism = ResolveConcurrency(options.MaxConcurrency),
            CancellationToken = cancellationToken
        };

        await Parallel.ForEachAsync(literals, parallelOptions, async (literal, token) =>
        {
            var cacheKey = TranslationCacheKeyBuilder.BuildDllKey(literal.SourceText, options);
            var cached = await _cache.TryGetAsync(cacheKey, token);
            if (!string.IsNullOrWhiteSpace(cached) && IsTranslationSafe(literal.SourceText, cached))
            {
                result[literal.SourceText] = cached;
                ReportDllProgress(progress, ref current, total);
                return;
            }

            var protectedText = _placeholderProtector.Protect(literal.SourceText);
            var request = new TranslationProviderRequest
            {
                Text = protectedText.Text,
                Category = TextCategory.系统,
                StyleProfile = options.StyleProfile,
                TargetLanguage = options.TargetLanguage,
                GlossaryPrompt = _glossaryService.BuildPrompt()
            };

            var translated = await TryTranslateAsync(request, options.ProviderChain, token) ?? literal.SourceText;
            translated = _placeholderProtector.Restore(translated, protectedText.Tokens);
            translated = _glossaryService.ApplyToTranslation(translated);
            if (!IsTranslationSafe(literal.SourceText, translated))
            {
                translated = literal.SourceText;
            }

            result[literal.SourceText] = translated;
            await _cache.UpsertAsync(cacheKey, "chain", literal.SourceText, translated, token);
            ReportDllProgress(progress, ref current, total);
        });

        return result;
    }

    private async Task<string?> TryTranslateAsync(TranslationProviderRequest request, IReadOnlyList<string> providerChain, CancellationToken cancellationToken)
    {
        foreach (var providerName in providerChain)
        {
            if (!_providerMap.TryGetValue(providerName, out var provider) || !provider.IsAvailable)
            {
                continue;
            }

            var translated = await provider.TranslateAsync(request, cancellationToken);
            if (!string.IsNullOrWhiteSpace(translated))
            {
                return translated.Trim();
            }
        }

        return null;
    }

    private static int ResolveConcurrency(int requested)
    {
        return Math.Clamp(requested, 1, 32);
    }

    private static void ReportTextProgress(IProgress<string>? progress, ref int current, int total, int cacheHitCount)
    {
        var processed = Interlocked.Increment(ref current);
        if (processed % 200 == 0 || processed == total)
        {
            progress?.Report($"文本翻译进度：{processed}/{total}（缓存命中 {cacheHitCount}）");
        }
    }

    private static void ReportDllProgress(IProgress<string>? progress, ref int current, int total)
    {
        var processed = Interlocked.Increment(ref current);
        if (processed % 300 == 0 || processed == total)
        {
            progress?.Report($"DLL 翻译进度：{processed}/{total}");
        }
    }

    private bool IsTranslationSafe(string source, string translated)
    {
        return _placeholderProtector.IsPlaceholderSafe(source, translated) &&
               _placeholderProtector.IsDoubleBraceBlockSafe(source, translated);
    }
}
