using MbCnsTool.Core;
using MbCnsTool.Core.Models;

namespace MbCnsTool.Cli;

/// <summary>
/// 命令行入口。
/// </summary>
public static class Program
{
    /// <summary>
    /// 主入口。
    /// </summary>
    public static async Task<int> Main(string[] args)
    {
        try
        {
            var options = ParseArguments(args);
            var pipeline = new LocalizationPipeline();
            var progress = new Progress<string>(message => Console.WriteLine($"[进度] {message}"));
            var summary = await pipeline.RunAsync(options, CancellationToken.None, progress);

            Console.WriteLine("汉化执行完成。");
            Console.WriteLine($"Mod根目录: {summary.ModuleRootPath}");
            Console.WriteLine($"输出目录: {summary.OutputPath}");
            Console.WriteLine($"文本总量: {summary.TotalTextCount}");
            Console.WriteLine($"缓存命中: {summary.CacheHitCount}");
            Console.WriteLine($"翻译调用: {summary.ProviderCallCount}");
            Console.WriteLine($"DLL字符串: {summary.DllLiteralCount}");
            Console.WriteLine($"Runtime映射: {summary.RuntimeMapPath ?? "未生成"}");
            return 0;
        }
        catch (Exception exception)
        {
            Console.Error.WriteLine($"执行失败: {exception.Message}");
            return 1;
        }
    }

    private static TranslationRunOptions ParseArguments(string[] args)
    {
        var map = BuildArgumentMap(args);
        if (!map.TryGetValue("mod", out var modPath) || string.IsNullOrWhiteSpace(modPath))
        {
            throw new ArgumentException("缺少必填参数 --mod <路径>");
        }

        var outputPath = map.TryGetValue("output", out var output) && !string.IsNullOrWhiteSpace(output)
            ? output
            : Path.Combine(Environment.CurrentDirectory, "artifacts");
        var mode = map.TryGetValue("mode", out var modeValue) && !string.IsNullOrWhiteSpace(modeValue)
            ? modeValue
            : "external";
        if (!mode.Equals("external", StringComparison.OrdinalIgnoreCase) &&
            !mode.Equals("overlay", StringComparison.OrdinalIgnoreCase))
        {
            throw new ArgumentException("参数 --mode 仅支持 external 或 overlay");
        }

        var style = map.TryGetValue("style", out var styleValue) && !string.IsNullOrWhiteSpace(styleValue)
            ? styleValue
            : "请用骑马与砍杀2的中世纪风格进行翻译";
        var targetLanguage = map.TryGetValue("target", out var target) && !string.IsNullOrWhiteSpace(target)
            ? target
            : "zh-CN";
        var glossary = map.TryGetValue("glossary", out var glossaryPath) && !string.IsNullOrWhiteSpace(glossaryPath)
            ? glossaryPath
            : Path.Combine(Environment.CurrentDirectory, "glossary", "default_glossary.txt");
        var cachePath = map.TryGetValue("cache", out var cacheDbPath) ? cacheDbPath : null;
        var maxConcurrency = map.TryGetValue("concurrency", out var concurrencyText) &&
                             int.TryParse(concurrencyText, out var parsedConcurrency)
            ? parsedConcurrency
            : 6;
        var providers = map.TryGetValue("providers", out var providerChain) && !string.IsNullOrWhiteSpace(providerChain)
            ? providerChain.Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries)
            : ["google_free", "fallback"];

        return new TranslationRunOptions
        {
            ModPath = modPath,
            OutputPath = outputPath,
            Mode = mode,
            StyleProfile = style,
            TargetLanguage = targetLanguage,
            GlossaryFilePath = glossary,
            CacheDbPath = cachePath,
            MaxConcurrency = maxConcurrency,
            ProviderChain = providers
        };
    }

    private static Dictionary<string, string> BuildArgumentMap(string[] args)
    {
        var map = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        for (var index = 0; index < args.Length; index++)
        {
            var token = args[index];
            if (!token.StartsWith("--", StringComparison.Ordinal))
            {
                continue;
            }

            var key = token[2..];
            if (index + 1 >= args.Length)
            {
                map[key] = string.Empty;
                continue;
            }

            var value = args[index + 1];
            if (value.StartsWith("--", StringComparison.Ordinal))
            {
                map[key] = string.Empty;
                continue;
            }

            map[key] = value;
            index++;
        }

        return map;
    }
}
