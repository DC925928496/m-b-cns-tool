using System.Globalization;
using System.Diagnostics;
using System.IO;
using System.Text;
using System.Text.RegularExpressions;
using System.Windows;
using System.Windows.Input;
using MbCnsTool.Core;
using MbCnsTool.Core.Models;
using MbCnsTool.Core.Services;
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
    private bool _canFinalize;
    private string? _lastReviewFilePath;
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
        FinalizeButton.IsEnabled = false;
        AppendLog("界面已初始化。请填写 Mod 路径后点击“开始翻译”。");
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

    private void OnOpenGlossaryFolder(object sender, RoutedEventArgs e)
    {
        var glossaryDirectory = Path.GetDirectoryName(GlossaryFilePath);
        if (string.IsNullOrWhiteSpace(glossaryDirectory))
        {
            AppendLog("术语目录路径无效。");
            MessageBox.Show(this, "术语目录路径无效。", "路径错误", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        Directory.CreateDirectory(glossaryDirectory);

        try
        {
            Process.Start(new ProcessStartInfo
            {
                FileName = glossaryDirectory,
                UseShellExecute = true
            });
            AppendLog($"已打开术语目录：{glossaryDirectory}");
        }
        catch (Exception exception)
        {
            AppendLog($"打开术语目录失败：{exception.Message}");
            MessageBox.Show(this, $"打开术语目录失败：{exception.Message}", "操作失败", MessageBoxButton.OK, MessageBoxImage.Error);
        }
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

    private async void OnEditReview(object sender, RoutedEventArgs e)
    {
        var reviewPath = ResolveReviewFilePathFromCurrentInput();
        if (reviewPath is null)
        {
            MessageBox.Show(this, "请先填写有效的 Mod 路径和输出目录。", "参数错误", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        try
        {
            var opened = await OpenReviewEditorAsync(reviewPath, CancellationToken.None);
            if (!opened)
            {
                MessageBox.Show(this, "未找到可编辑的翻译对比文件，请先执行“开始翻译”。", "提示", MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }

            _canFinalize = true;
            FinalizeButton.IsEnabled = !_isRunning;
        }
        catch (Exception exception)
        {
            AppendLog($"打开翻译对比失败：{exception.Message}");
            MessageBox.Show(this, exception.Message, "操作失败", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private async void OnStartTranslate(object sender, RoutedEventArgs e)
    {
        if (_isRunning)
        {
            _currentCancellation?.Cancel();
            AppendLog("收到中断请求，正在安全停止。");
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
        _canFinalize = false;
        _lastReviewFilePath = null;
        _currentCancellation = new CancellationTokenSource();
        StartButton.IsEnabled = false;
        StartButton.Content = "中断任务";
        FinalizeButton.IsEnabled = false;
        Mouse.OverrideCursor = Cursors.Wait;
        ResetStageProgress();
        AppendLog("翻译阶段开始。");

        try
        {
            var options = BuildOptions(maxConcurrency);
            AppendLog($"执行参数：模式={GetModeDisplayName(options.Mode)}，风格={options.StyleProfile}，并发={options.MaxConcurrency}，引擎链路={string.Join(" -> ", options.ProviderChain)}");

            var pipeline = new LocalizationPipeline();
            var progress = new Progress<string>(ReportProgress);
            var summary = await Task.Run(async () =>
                await pipeline.RunTranslationStageAsync(options, _currentCancellation?.Token ?? CancellationToken.None, progress));

            SetProgress(ScanProgressBar, ScanProgressText, 100);
            SetProgress(TextProgressBar, TextProgressText, 100);
            SetProgress(DllProgressBar, DllProgressText, 100);
            SetProgress(PackageProgressBar, PackageProgressText, 0);

            AppendLog("翻译阶段完成。");
            AppendLog($"Mod根目录: {summary.ModuleRootPath}");
            AppendLog($"输出目录: {summary.OutputPath}");
            AppendLog($"文本总量: {summary.TotalTextCount}");
            AppendLog($"缓存命中: {summary.CacheHitCount}");
            AppendLog($"翻译调用: {summary.ProviderCallCount}");
            AppendLog($"DLL字符串: {summary.DllLiteralCount}");
            AppendLog($"对比条目: {summary.ReviewEntryCount}");
            AppendLog($"对比文件: {summary.ReviewFilePath ?? "未生成"}");
            AppendLog($"Runtime映射: {summary.RuntimeMapPath ?? "未生成"}");
            _lastReviewFilePath = summary.ReviewFilePath;
            if (!string.IsNullOrWhiteSpace(_lastReviewFilePath))
            {
                _canFinalize = await OpenReviewEditorAsync(_lastReviewFilePath, _currentCancellation?.Token ?? CancellationToken.None);
            }

            if (_canFinalize)
            {
                AppendLog("请确认译文后点击“最终打包”。");
            }

            MessageBox.Show(this, "翻译阶段已完成，请确认译文后再执行最终打包。", "完成", MessageBoxButton.OK, MessageBoxImage.Information);
        }
        catch (OperationCanceledException)
        {
            AppendLog("任务已中断。可重新执行当前阶段。");
            MessageBox.Show(this, "任务已中断。", "提示", MessageBoxButton.OK, MessageBoxImage.Information);
        }
        catch (Exception exception)
        {
            AppendLog($"执行失败: {exception.Message}");
            MessageBox.Show(this, exception.Message, "执行失败", MessageBoxButton.OK, MessageBoxImage.Error);
        }
        finally
        {
            _isRunning = false;
            _currentCancellation?.Dispose();
            _currentCancellation = null;
            StartButton.IsEnabled = true;
            StartButton.Content = "开始翻译";
            FinalizeButton.IsEnabled = _canFinalize;
            Mouse.OverrideCursor = null;
        }
    }

    private async void OnFinalizePackage(object sender, RoutedEventArgs e)
    {
        if (_isRunning)
        {
            return;
        }

        if (!_canFinalize)
        {
            MessageBox.Show(this, "请先完成翻译并确认译文后再打包。", "提示", MessageBoxButton.OK, MessageBoxImage.Information);
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
        _currentCancellation = new CancellationTokenSource();
        StartButton.IsEnabled = false;
        FinalizeButton.IsEnabled = false;
        StartButton.Content = "中断任务";
        Mouse.OverrideCursor = Cursors.Wait;
        SetProgress(PackageProgressBar, PackageProgressText, 1);
        AppendLog("最终打包开始。");

        try
        {
            var options = BuildOptions(maxConcurrency);
            var pipeline = new LocalizationPipeline();
            var progress = new Progress<string>(ReportProgress);
            var summary = await Task.Run(async () =>
                await pipeline.RunPackageStageAsync(options, _currentCancellation?.Token ?? CancellationToken.None, progress));

            SetProgress(PackageProgressBar, PackageProgressText, 100);
            AppendLog("最终打包完成。");
            AppendLog($"输出目录: {summary.OutputPath}");
            AppendLog($"Runtime映射: {summary.RuntimeMapPath ?? "未生成"}");
            MessageBox.Show(this, "最终打包完成。", "完成", MessageBoxButton.OK, MessageBoxImage.Information);
        }
        catch (OperationCanceledException)
        {
            AppendLog("任务已中断。可重新执行最终打包。");
            MessageBox.Show(this, "任务已中断。", "提示", MessageBoxButton.OK, MessageBoxImage.Information);
        }
        catch (Exception exception)
        {
            AppendLog($"执行失败: {exception.Message}");
            MessageBox.Show(this, exception.Message, "执行失败", MessageBoxButton.OK, MessageBoxImage.Error);
        }
        finally
        {
            _isRunning = false;
            _currentCancellation?.Dispose();
            _currentCancellation = null;
            StartButton.IsEnabled = true;
            StartButton.Content = "开始翻译";
            FinalizeButton.IsEnabled = _canFinalize;
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
            ReviewFilePath = _lastReviewFilePath,
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
    }

    private string? ResolveReviewFilePathFromCurrentInput()
    {
        if (!string.IsNullOrWhiteSpace(_lastReviewFilePath))
        {
            return _lastReviewFilePath;
        }

        var modPath = ModPathTextBox.Text.Trim();
        var outputPath = OutputPathTextBox.Text.Trim();
        if (string.IsNullOrWhiteSpace(modPath) ||
            string.IsNullOrWhiteSpace(outputPath) ||
            !Directory.Exists(modPath))
        {
            return null;
        }

        return TranslationReviewService.ResolveDefaultPath(outputPath, modPath);
    }

    private async Task<bool> OpenReviewEditorAsync(string reviewPath, CancellationToken cancellationToken)
    {
        var reviewService = new TranslationReviewService();
        var snapshot = await reviewService.TryLoadAsync(reviewPath, cancellationToken);
        if (snapshot is null || snapshot.Entries.Count == 0)
        {
            AppendLog("未找到可编辑翻译对比数据。");
            return false;
        }

        var dialog = new TranslationReviewWindow(snapshot)
        {
            Owner = this
        };
        var confirmed = dialog.ShowDialog() == true;
        if (!confirmed)
        {
            AppendLog("翻译对比未保存，继续保留原缓存内容。");
            return false;
        }

        await reviewService.SaveAsync(reviewPath, dialog.Snapshot, cancellationToken);
        await using var cache = await TranslationCache.OpenAsync(CacheDbPath, cancellationToken);
        var updated = await reviewService.ApplySnapshotToCacheAsync(dialog.Snapshot, cache, cancellationToken);
        AppendLog($"翻译对比已保存并写回缓存：{updated} 条。");
        _lastReviewFilePath = reviewPath;
        return true;
    }

    private string GlossaryFilePath => Path.Combine(Environment.CurrentDirectory, "glossary", "default_glossary.txt");

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
