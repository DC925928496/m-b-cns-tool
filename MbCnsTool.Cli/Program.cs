using MbCnsTool.Core;
using MbCnsTool.Core.Models;

namespace MbCnsTool.Cli;

/// <summary>
/// 命令行入口。
/// </summary>
public static class Program
{
    /// <summary>
    /// 入口。
    /// </summary>
    public static async Task<int> Main(string[] args)
    {
        try
        {
            var pipeline = new LocalizationPipeline();
            var progress = new Progress<string>(message => Console.WriteLine($"[进度] {message}"));

            var (command, map) = ParseCommand(args);
            if (command == "help")
            {
                PrintUsage();
                return 0;
            }

            var options = BuildOptions(command, map);
            var summary = command switch
            {
                "scan" => await pipeline.RunScanStageAsync(options, CancellationToken.None, progress),
                "translate" => await pipeline.RunTranslationStageAsync(options, CancellationToken.None, progress),
                "package" => await pipeline.RunPackageStageAsync(options, CancellationToken.None, progress),
                _ => throw new ArgumentException($"未知命令：{command}")
            };

            if (command == "package")
            {
                Console.WriteLine("打包完成。");
            }
            else if (command == "translate")
            {
                Console.WriteLine("翻译阶段完成，等待人工校对。");
                Console.WriteLine("请编辑工程记录（JSON）后再运行 package 命令打包。");
            }
            else
            {
                Console.WriteLine("扫描完成。");
            }

            Console.WriteLine($"Mod 根目录: {summary.ModuleRootPath}");
            Console.WriteLine($"输出目录: {summary.OutputPath}");
            Console.WriteLine($"文本总量: {summary.TotalTextCount}");
            Console.WriteLine($"缓存命中: {summary.CacheHitCount}");
            Console.WriteLine($"翻译调用: {summary.ProviderCallCount}");
            Console.WriteLine($"DLL 字面量: {summary.DllLiteralCount}");
            Console.WriteLine($"工程条目: {summary.ReviewEntryCount}");
            Console.WriteLine($"工程记录: {summary.ReviewFilePath ?? "未生成"}");
            Console.WriteLine($"Runtime 映射: {summary.RuntimeMapPath ?? "未生成"}");

            return 0;
        }
        catch (Exception exception)
        {
            Console.Error.WriteLine($"执行失败: {exception.Message}");
            return 1;
        }
    }

    private static (string command, Dictionary<string, string> map) ParseCommand(string[] args)
    {
        if (args.Length == 0)
        {
            return ("help", new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase));
        }

        var command = args[0].Trim().ToLowerInvariant();
        if (command is "-h" or "--help" or "help")
        {
            return ("help", new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase));
        }

        if (command is not ("scan" or "translate" or "package"))
        {
            throw new ArgumentException($"未知命令：{command}（可用：scan / translate / package）");
        }

        var map = BuildArgumentMap(args.Skip(1).ToArray());
        return (command, map);
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

    private static TranslationRunOptions BuildOptions(string command, IReadOnlyDictionary<string, string> map)
    {
        var modPath = GetRequired(map, "mod");
        var outputPath = map.TryGetValue("output", out var output) && !string.IsNullOrWhiteSpace(output)
            ? output
            : string.Empty;

        var mode = map.TryGetValue("mode", out var modeValue) && !string.IsNullOrWhiteSpace(modeValue)
            ? modeValue
            : "external";
        if (!mode.Equals("external", StringComparison.OrdinalIgnoreCase) &&
            !mode.Equals("overlay", StringComparison.OrdinalIgnoreCase))
        {
            throw new ArgumentException("参数 --mode 仅支持 external 或 overlay");
        }

        var targetLanguage = map.TryGetValue("target", out var target) && !string.IsNullOrWhiteSpace(target)
            ? target
            : "zh-CN";
        var scanDll = map.TryGetValue("scan-dll", out var scanDllText) && IsTrueFlag(scanDllText);

        var projectPath = map.TryGetValue("project", out var projectValue) && !string.IsNullOrWhiteSpace(projectValue)
            ? projectValue
            : null;
        var cachePath = map.TryGetValue("cache", out var cacheValue) && !string.IsNullOrWhiteSpace(cacheValue)
            ? cacheValue
            : null;

        var glossary = map.TryGetValue("glossary", out var glossaryPath) && !string.IsNullOrWhiteSpace(glossaryPath)
            ? glossaryPath
            : Path.Combine(Environment.CurrentDirectory, "glossary", "default_glossary.txt");

        var style = map.TryGetValue("style", out var styleValue) && !string.IsNullOrWhiteSpace(styleValue)
            ? styleValue
            : "按《骑马与砍杀2》本地化规范翻译并保持占位符安全。";

        var maxConcurrency = map.TryGetValue("concurrency", out var concurrencyText) &&
                             int.TryParse(concurrencyText, out var parsedConcurrency)
            ? parsedConcurrency
            : 6;
        var providers = map.TryGetValue("providers", out var providerChain) && !string.IsNullOrWhiteSpace(providerChain)
            ? providerChain.Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries)
            : ["google_free", "fallback"];

        if (command is "scan" or "package")
        {
            providers = ["fallback"];
            maxConcurrency = 1;
        }

        return new TranslationRunOptions
        {
            ModPath = modPath,
            OutputPath = outputPath,
            Mode = mode,
            StyleProfile = style,
            TargetLanguage = targetLanguage,
            GlossaryFilePath = glossary,
            CacheDbPath = cachePath,
            ReviewFilePath = projectPath,
            MaxConcurrency = maxConcurrency,
            ProviderChain = providers,
            ScanDll = scanDll
        };
    }

    private static string GetRequired(IReadOnlyDictionary<string, string> map, string key)
    {
        if (!map.TryGetValue(key, out var value) || string.IsNullOrWhiteSpace(value))
        {
            throw new ArgumentException($"缺少参数 --{key} <值>");
        }

        return value;
    }

    private static bool IsTrueFlag(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return true;
        }

        return value.Equals("1", StringComparison.OrdinalIgnoreCase) ||
               value.Equals("true", StringComparison.OrdinalIgnoreCase) ||
               value.Equals("yes", StringComparison.OrdinalIgnoreCase);
    }

    private static void PrintUsage()
    {
        Console.WriteLine(
            """
            用法：
              mbcns scan --mod <Mod目录> [--output <输出目录>] [--target zh-CN] [--scan-dll true|false] [--project <工程记录路径>]
              mbcns translate --mod <Mod目录> [--output <输出目录>] [--target zh-CN] [--scan-dll true|false] [--project <工程记录路径>] [--cache <缓存db>] [--glossary <术语表>]
                           [--style <风格>] [--providers google_free,fallback] [--concurrency 6]
              mbcns package --mod <Mod目录> [--output <输出目录>] [--mode external|overlay] [--target zh-CN] [--scan-dll true|false] [--project <工程记录路径>] [--cache <缓存db>]

            说明：
              - scan 仅生成/更新工程记录，不进行翻译。
              - translate 会对工程记录中“空译文”进行机翻填充，并将结果写回工程记录；请手工校对后再运行 package。
              - package 会读取工程记录与缓存，生成依赖原 Mod 的外挂汉化 Mod（必要时包含运行时注入 DLL）。
              - 工程记录与缓存默认持久化在工具目录下的 data/ 目录中；外置包（external）默认输出到原 Mod 同级目录，可用 --output 覆盖。
            """);
    }
}
