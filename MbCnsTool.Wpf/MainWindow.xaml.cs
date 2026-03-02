using System.Globalization;
using System.IO;
using System.Text;
using System.Text.RegularExpressions;
using System.Windows;
using System.Windows.Input;
using MbCnsTool.Core;
using MbCnsTool.Core.Models;
using MbCnsTool.Wpf.Models;
using Cursors = System.Windows.Input.Cursors;
using FolderBrowserDialog = System.Windows.Forms.FolderBrowserDialog;
using MessageBox = System.Windows.MessageBox;

namespace MbCnsTool.Wpf;

/// <summary>
/// 主窗口。
/// </summary>
public partial class MainWindow : Window
{
    private const string CustomProviderKey = "custom_openai";
    private const string DefaultStylePrompt = "请用骑马与砍杀2的中世纪风格进行翻译";
    private const int DefaultConcurrency = 6;
    private bool _isRunning;
    private bool _canResume;
    private CancellationTokenSource? _currentCancellation;
    private CustomOpenAiProviderOptions? _customProviderOptions;
    private readonly List<EngineOption> _engineOptions =
    [
        new() { ProviderKey = "google_free", DisplayName = "谷歌免费接口（默认）" }
    ];

    /// <summary>
    /// 初始化窗口。
    /// </summary>
    public MainWindow()
    {
        InitializeComponent();
        InitializeDefaults();
    }

    private void InitializeDefaults()
    {
        ModeComboBox.SelectedIndex = 0;
        StyleComboBox.SelectedIndex = 0;
        StyleComboBox.Text = DefaultStylePrompt;
        ConcurrencyTextBox.Text = DefaultConcurrency.ToString(CultureInfo.InvariantCulture);
        EngineComboBox.ItemsSource = _engineOptions;
        EngineComboBox.SelectedIndex = 0;

        var current = Environment.CurrentDirectory;
        OutputPathTextBox.Text = Path.Combine(current, "artifacts");
        EnsureFixedFiles();
        UpdateModeHint();
        ResetStageProgress();
        AppendLog("界面已初始化。请填写 Mod 路径后点击“开始汉化”。");
    }

    private void OnPickModDirectory(object sender, RoutedEventArgs e)
    {
        var folder = OpenDirectory("请选择需要汉化的 Mod 目录");
        if (folder is null)
        {
            return;
        }

        ModPathTextBox.Text = folder;
    }

    private void OnPickOutputDirectory(object sender, RoutedEventArgs e)
    {
        var folder = OpenDirectory("请选择汉化包输出目录");
        if (folder is null)
        {
            return;
        }

        OutputPathTextBox.Text = folder;
    }

    private void OnModeSelectionChanged(object sender, System.Windows.Controls.SelectionChangedEventArgs e)
    {
        UpdateModeHint();
    }

    private void UpdateModeHint()
    {
        var mode = ResolveMode();
        ModeHintTextBlock.Text = mode == "overlay"
            ? "覆盖原文件：直接改写原 Mod 文件。适合个人自用，回滚需手动备份。"
            : "外置汉化包：生成独立 *_CNs 目录，不修改原 Mod。便于分发和管理。";
    }

    private void OnEditGlossary(object sender, RoutedEventArgs e)
    {
        var editor = new GlossaryEditorWindow(GlossaryFilePath, GlossaryTemplatePath)
        {
            Owner = this
        };
        _ = editor.ShowDialog();
        AppendLog("术语表编辑窗口已关闭。");
    }

    private void OnOpenCustomProvider(object sender, RoutedEventArgs e)
    {
        var dialog = new CustomProviderWindow(_customProviderOptions)
        {
            Owner = this
        };
        if (dialog.ShowDialog() != true || dialog.ProviderOptions is null)
        {
            return;
        }

        _customProviderOptions = dialog.ProviderOptions;
        EnsureCustomEngineOption();
        var customOption = _engineOptions.First(option => option.ProviderKey == CustomProviderKey);
        EngineComboBox.SelectedItem = customOption;
        AppendLog($"已更新自定义引擎：{_customProviderOptions.DisplayName}");
    }

    private void EnsureCustomEngineOption()
    {
        var existing = _engineOptions.FirstOrDefault(option => option.ProviderKey == CustomProviderKey);
        if (existing is not null)
        {
            _engineOptions.Remove(existing);
        }

        _engineOptions.Add(new EngineOption
        {
            ProviderKey = CustomProviderKey,
            DisplayName = $"{_customProviderOptions?.DisplayName ?? "自定义OpenAI"}（自定义）",
            IsCustom = true
        });

        EngineComboBox.ItemsSource = null;
        EngineComboBox.ItemsSource = _engineOptions;
    }

    private async void OnStartTranslate(object sender, RoutedEventArgs e)
    {
        if (_isRunning)
        {
            _currentCancellation?.Cancel();
            AppendLog("收到中断请求，正在安全停止。再次点击可继续执行。");
            return;
        }

        var modPath = ModPathTextBox.Text.Trim();
        if (string.IsNullOrWhiteSpace(modPath) || !Directory.Exists(modPath))
        {
            MessageBox.Show(this, "请先填写有效的 Mod 路径。", "参数错误", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        var outputPath = OutputPathTextBox.Text.Trim();
        if (string.IsNullOrWhiteSpace(outputPath))
        {
            MessageBox.Show(this, "请先填写输出目录。", "参数错误", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        if (!TryResolveConcurrency(out var maxConcurrency))
        {
            MessageBox.Show(this, "并发数请输入 1-32 的整数。", "参数错误", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        _isRunning = true;
        _canResume = false;
        _currentCancellation = new CancellationTokenSource();
        StartButton.IsEnabled = false;
        StartButton.Content = "中断任务";
        Mouse.OverrideCursor = Cursors.Wait;
        ResetStageProgress();
        AppendLog("任务开始。");

        try
        {
            var options = BuildOptions(maxConcurrency);
            AppendLog($"执行参数：模式={GetModeDisplayName(options.Mode)}，风格={options.StyleProfile}，并发={options.MaxConcurrency}，引擎链路={string.Join(" -> ", options.ProviderChain)}");

            var pipeline = new LocalizationPipeline();
            var progress = new Progress<string>(ReportProgress);
            var summary = await Task.Run(async () =>
                await pipeline.RunAsync(options, _currentCancellation?.Token ?? CancellationToken.None, progress));

            SetProgress(ScanProgressBar, ScanProgressText, 100);
            SetProgress(TextProgressBar, TextProgressText, 100);
            SetProgress(DllProgressBar, DllProgressText, 100);
            SetProgress(PackageProgressBar, PackageProgressText, 100);

            AppendLog("执行完成。");
            AppendLog($"Mod根目录: {summary.ModuleRootPath}");
            AppendLog($"输出目录: {summary.OutputPath}");
            AppendLog($"文本总量: {summary.TotalTextCount}");
            AppendLog($"缓存命中: {summary.CacheHitCount}");
            AppendLog($"翻译调用: {summary.ProviderCallCount}");
            AppendLog($"DLL字符串: {summary.DllLiteralCount}");
            AppendLog($"Runtime映射: {summary.RuntimeMapPath ?? "未生成"}");
            _canResume = false;
            MessageBox.Show(this, "汉化执行完成。", "完成", MessageBoxButton.OK, MessageBoxImage.Information);
        }
        catch (OperationCanceledException)
        {
            _canResume = true;
            AppendLog("任务已中断。点击“继续汉化”可从缓存断点继续。");
            MessageBox.Show(this, "任务已中断，可继续执行。", "提示", MessageBoxButton.OK, MessageBoxImage.Information);
        }
        catch (Exception exception)
        {
            _canResume = true;
            AppendLog($"执行失败: {exception.Message}");
            AppendLog("可点击“继续汉化”从缓存断点继续。");
            MessageBox.Show(this, exception.Message, "执行失败", MessageBoxButton.OK, MessageBoxImage.Error);
        }
        finally
        {
            _isRunning = false;
            _currentCancellation?.Dispose();
            _currentCancellation = null;
            StartButton.IsEnabled = true;
            StartButton.Content = _canResume ? "继续汉化" : "开始汉化";
            Mouse.OverrideCursor = null;
        }
    }

    private TranslationRunOptions BuildOptions(int maxConcurrency)
    {
        var style = StyleComboBox.Text.Trim();
        var mode = ResolveMode();
        var selectedEngine = EngineComboBox.SelectedItem as EngineOption ?? _engineOptions[0];

        return new TranslationRunOptions
        {
            ModPath = ModPathTextBox.Text.Trim(),
            OutputPath = OutputPathTextBox.Text.Trim(),
            Mode = mode,
            StyleProfile = string.IsNullOrWhiteSpace(style) ? DefaultStylePrompt : style,
            TargetLanguage = "zh-CN",
            GlossaryFilePath = GlossaryFilePath,
            CacheDbPath = CacheDbPath,
            MaxConcurrency = maxConcurrency,
            ProviderChain = BuildProviderChain(selectedEngine),
            CustomOpenAiProvider = selectedEngine.ProviderKey == CustomProviderKey ? _customProviderOptions : null
        };
    }

    private bool TryResolveConcurrency(out int maxConcurrency)
    {
        var text = ConcurrencyTextBox.Text.Trim();
        if (string.IsNullOrWhiteSpace(text))
        {
            maxConcurrency = DefaultConcurrency;
            ConcurrencyTextBox.Text = DefaultConcurrency.ToString(CultureInfo.InvariantCulture);
            return true;
        }

        if (!int.TryParse(text, NumberStyles.Integer, CultureInfo.InvariantCulture, out maxConcurrency))
        {
            return false;
        }

        if (maxConcurrency is < 1 or > 32)
        {
            return false;
        }

        return true;
    }

    private IReadOnlyList<string> BuildProviderChain(EngineOption selectedEngine)
    {
        if (selectedEngine.ProviderKey == CustomProviderKey)
        {
            if (_customProviderOptions is null)
            {
                throw new InvalidOperationException("请先点击“自定义 AI”完成 API 配置。");
            }

            return [_customProviderOptions.ProviderKey, "google_free", "fallback"];
        }

        return ["google_free", "fallback"];
    }

    private void ReportProgress(string message)
    {
        AppendLog(message);
        UpdateStageProgress(message);
    }

    private void UpdateStageProgress(string message)
    {
        if (message.Contains("开始扫描", StringComparison.Ordinal))
        {
            SetProgress(ScanProgressBar, ScanProgressText, 10);
            return;
        }

        if (message.StartsWith("扫描完成", StringComparison.Ordinal))
        {
            SetProgress(ScanProgressBar, ScanProgressText, 100);
            return;
        }

        if (message.Contains("开始翻译文本内容", StringComparison.Ordinal))
        {
            SetProgress(TextProgressBar, TextProgressText, 1);
            return;
        }

        var textMatch = TextProgressRegex().Match(message);
        if (textMatch.Success)
        {
            var percent = CalculatePercent(textMatch.Groups[1].Value, textMatch.Groups[2].Value);
            SetProgress(TextProgressBar, TextProgressText, percent);
            return;
        }

        if (message.Contains("开始翻译 DLL", StringComparison.Ordinal))
        {
            SetProgress(TextProgressBar, TextProgressText, 100);
            SetProgress(DllProgressBar, DllProgressText, 1);
            return;
        }

        var dllMatch = DllProgressRegex().Match(message);
        if (dllMatch.Success)
        {
            var percent = CalculatePercent(dllMatch.Groups[1].Value, dllMatch.Groups[2].Value);
            SetProgress(DllProgressBar, DllProgressText, percent);
            return;
        }

        if (message.Contains("开始写入文件并打包", StringComparison.Ordinal))
        {
            SetProgress(DllProgressBar, DllProgressText, 100);
            SetProgress(PackageProgressBar, PackageProgressText, 20);
            return;
        }

        if (message.Contains("打包完成", StringComparison.Ordinal))
        {
            SetProgress(PackageProgressBar, PackageProgressText, 100);
        }
    }

    private static int CalculatePercent(string currentText, string totalText)
    {
        if (!int.TryParse(currentText, NumberStyles.Integer, CultureInfo.InvariantCulture, out var current) ||
            !int.TryParse(totalText, NumberStyles.Integer, CultureInfo.InvariantCulture, out var total) ||
            total <= 0)
        {
            return 0;
        }

        var percent = (int)Math.Round(current * 100d / total, MidpointRounding.AwayFromZero);
        return Math.Clamp(percent, 0, 100);
    }

    private void ResetStageProgress()
    {
        SetProgress(ScanProgressBar, ScanProgressText, 0);
        SetProgress(TextProgressBar, TextProgressText, 0);
        SetProgress(DllProgressBar, DllProgressText, 0);
        SetProgress(PackageProgressBar, PackageProgressText, 0);
    }

    private static void SetProgress(System.Windows.Controls.ProgressBar progressBar, System.Windows.Controls.TextBlock progressText, int value)
    {
        progressBar.Value = value;
        progressText.Text = $"{value}%";
    }

    private string ResolveMode()
    {
        var selected = ModeComboBox.SelectedItem as System.Windows.Controls.ComboBoxItem;
        var mode = selected?.Tag?.ToString();
        return string.IsNullOrWhiteSpace(mode) ? "external" : mode;
    }

    private static string GetModeDisplayName(string mode)
    {
        return mode == "overlay" ? "覆盖原文件（仅自用）" : "外置汉化包（推荐）";
    }

    private static string? OpenDirectory(string title)
    {
        using var dialog = new FolderBrowserDialog
        {
            Description = title,
            UseDescriptionForTitle = true,
            ShowNewFolderButton = true
        };
        var result = dialog.ShowDialog();
        return result == System.Windows.Forms.DialogResult.OK ? dialog.SelectedPath : null;
    }

    private void EnsureFixedFiles()
    {
        Directory.CreateDirectory(Path.GetDirectoryName(CacheDbPath) ?? ".");
        Directory.CreateDirectory(Path.GetDirectoryName(GlossaryFilePath) ?? ".");
        Directory.CreateDirectory(Path.GetDirectoryName(GlossaryTemplatePath) ?? ".");

        if (!File.Exists(GlossaryFilePath))
        {
            File.WriteAllLines(GlossaryFilePath,
            [
                "# 骑砍2常用术语",
                "Denar=第纳尔",
                "Bannerlord=霸主",
                "Quartermaster=军需官"
            ], new UTF8Encoding(false));
        }

        if (!File.Exists(GlossaryTemplatePath))
        {
            File.WriteAllText(
                GlossaryTemplatePath,
                """
                {
                  "entries": [
                    { "source": "Denar", "target": "第纳尔" },
                    { "source": "Bannerlord", "target": "霸主" },
                    { "source": "Quartermaster", "target": "军需官" }
                  ]
                }
                """,
                new UTF8Encoding(false));
        }
    }

    private string GlossaryFilePath => Path.Combine(Environment.CurrentDirectory, "glossary", "default_glossary.txt");

    private string GlossaryTemplatePath => Path.Combine(Environment.CurrentDirectory, "glossary", "glossary.template.json");

    private string CacheDbPath => Path.Combine(Environment.CurrentDirectory, "artifacts", "cache", "translation_cache.db");

    private void AppendLog(string message)
    {
        var line = $"[{DateTime.Now:HH:mm:ss}] {message}";
        if (LogTextBox.Text.Length == 0)
        {
            LogTextBox.Text = line;
            return;
        }

        LogTextBox.AppendText(Environment.NewLine + line);
        LogTextBox.ScrollToEnd();
    }

    [GeneratedRegex(@"文本翻译进度：(\d+)/(\d+)")]
    private static partial Regex TextProgressRegex();

    [GeneratedRegex(@"DLL 翻译进度：(\d+)/(\d+)")]
    private static partial Regex DllProgressRegex();
}
