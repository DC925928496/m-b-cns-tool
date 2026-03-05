using System.Collections.ObjectModel;
using System.IO;
using System.Windows;
using MbCnsTool.Core;
using MbCnsTool.Core.Models;
using MbCnsTool.Core.Services;
using MbCnsTool.Wpf.ViewModels;

namespace MbCnsTool.Wpf;

/// <summary>
/// 主窗口：扫描、机翻、校对与打包。
/// </summary>
public partial class MainWindow : Window
{
    private readonly ObservableCollection<ProjectEntryRow> _rows = [];

    private string? _projectPath;
    private TranslationProject? _project;
    private bool _isBusy;

    public MainWindow()
    {
        InitializeComponent();
        EntriesDataGrid.ItemsSource = _rows;
        AppendLog("就绪。请选择 Mod 根目录，然后点击“扫描”。");
        AppendLog("说明：工程记录与缓存固定保存在工具目录 data/；外置汉化包默认输出到原 Mod 同级目录。");
    }

    private void OnPickModDirectory(object sender, RoutedEventArgs e)
    {
        using var dialog = new System.Windows.Forms.FolderBrowserDialog
        {
            Description = "选择需要汉化的 Mod 根目录（包含 SubModule.xml 的目录，或其父目录）",
            UseDescriptionForTitle = true,
            ShowNewFolderButton = false
        };

        if (dialog.ShowDialog() == System.Windows.Forms.DialogResult.OK)
        {
            ModPathTextBox.Text = dialog.SelectedPath;
            TryAutoFillOutputPath(dialog.SelectedPath);
        }
    }

    private void OnPickOutputDirectory(object sender, RoutedEventArgs e)
    {
        using var dialog = new System.Windows.Forms.FolderBrowserDialog
        {
            Description = "选择外挂汉化 Mod 输出目录（默认：原 Mod 同级；工程记录与缓存固定保存在工具目录 data/）",
            UseDescriptionForTitle = true,
            ShowNewFolderButton = true
        };

        if (dialog.ShowDialog() == System.Windows.Forms.DialogResult.OK)
        {
            OutputPathTextBox.Text = dialog.SelectedPath;
        }
    }

    private async void OnScan(object sender, RoutedEventArgs e)
    {
        if (_isBusy)
        {
            return;
        }

        var options = BuildBaseOptions();
        if (options is null)
        {
            return;
        }

        await RunAsync("扫描", async progress =>
        {
            var pipeline = new LocalizationPipeline();
            var summary = await pipeline.RunScanStageAsync(options, CancellationToken.None, progress);
            _projectPath = summary.ReviewFilePath;
            AppendLog($"模块根目录：{summary.ModuleRootPath}");
            AppendLog($"数据目录（工具目录）：{summary.OutputPath}");
            AppendLog($"工程记录：{_projectPath}");
            AppendLog("说明：翻译目标为 ModuleData/Languages 下语言 XML（排除 CNs/CNt）；其它 XML/JSON/DLL 仅用于收集缺失 {=id} 引用并补全到主语言文件。");
            await LoadProjectAsync();
        });
    }

    private async void OnTranslate(object sender, RoutedEventArgs e)
    {
        if (_isBusy)
        {
            return;
        }

        if (!await EnsureProjectLoadedAsync())
        {
            System.Windows.MessageBox.Show(this, "请先执行“扫描”生成工程记录。", "提示", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        await SaveProjectAsync();

        var options = BuildBaseOptions(
            providerChain: ["google_free", "fallback"],
            maxConcurrency: 6);
        if (options is null)
        {
            return;
        }

        await RunAsync("翻译", async progress =>
        {
            var pipeline = new LocalizationPipeline();
            var summary = await pipeline.RunTranslationStageAsync(options, CancellationToken.None, progress);
            _projectPath = summary.ReviewFilePath;
            AppendLog($"数据目录（工具目录）：{summary.OutputPath}");
            AppendLog($"翻译阶段完成。工程记录：{_projectPath}");
            await LoadProjectAsync();
        });
    }

    private async void OnSaveProject(object sender, RoutedEventArgs e)
    {
        if (_isBusy)
        {
            return;
        }

        if (!await EnsureProjectLoadedAsync())
        {
            System.Windows.MessageBox.Show(this, "当前未加载工程记录。请先执行“扫描”。", "提示", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        await RunAsync("保存", async _ =>
        {
            await SaveProjectAsync();
            AppendLog("已保存工程记录。");
        });
    }

    private async void OnPackage(object sender, RoutedEventArgs e)
    {
        if (_isBusy)
        {
            return;
        }

        if (!await EnsureProjectLoadedAsync())
        {
            System.Windows.MessageBox.Show(this, "请先执行“扫描/翻译”生成工程记录。", "提示", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        await SaveProjectAsync();

        var options = BuildBaseOptions();
        if (options is null)
        {
            return;
        }

        await RunAsync("打包", async progress =>
        {
            var pipeline = new LocalizationPipeline();
            var summary = await pipeline.RunPackageStageAsync(options, CancellationToken.None, progress);
            AppendLog($"打包完成：{summary.OutputPath}");
            AppendLog($"Runtime 映射：{summary.RuntimeMapPath ?? "未生成"}");
        });
    }

    private TranslationRunOptions? BuildBaseOptions(
        IReadOnlyList<string>? providerChain = null,
        int? maxConcurrency = null)
    {
        var modPath = ModPathTextBox.Text.Trim();
        if (string.IsNullOrWhiteSpace(modPath) || !Directory.Exists(modPath))
        {
            System.Windows.MessageBox.Show(this, "请填写有效的 Mod 路径。", "参数错误", MessageBoxButton.OK, MessageBoxImage.Warning);
            return null;
        }

        var outputPath = OutputPathTextBox.Text?.Trim() ?? string.Empty;

        return new TranslationRunOptions
        {
            ModPath = modPath,
            OutputPath = outputPath,
            Mode = "external",
            TargetLanguage = "zh-CN",
            StyleProfile = "按《骑马与砍杀2》本地化规范翻译并保持占位符安全。",
            GlossaryFilePath = Path.Combine(Environment.CurrentDirectory, "glossary", "default_glossary.txt"),
            CacheDbPath = null,
            ReviewFilePath = _projectPath,
            ProviderChain = providerChain ?? ["fallback"],
            MaxConcurrency = maxConcurrency ?? 1,
            ScanDll = ScanDllCheckBox.IsChecked == true
        };
    }

    private void TryAutoFillOutputPath(string inputPath)
    {
        try
        {
            var moduleRoot = ModuleRootLocator.Resolve(inputPath);
            var parent = Directory.GetParent(moduleRoot)?.FullName;
            if (!string.IsNullOrWhiteSpace(parent))
            {
                OutputPathTextBox.Text = parent;
            }
        }
        catch
        {
            // 无法定位唯一模块根目录时不自动填充，避免误导。
        }
    }

    private async Task<bool> EnsureProjectLoadedAsync()
    {
        if (_project is not null && !string.IsNullOrWhiteSpace(_projectPath))
        {
            return true;
        }

        await LoadProjectAsync();
        return _project is not null && !string.IsNullOrWhiteSpace(_projectPath);
    }

    private async Task LoadProjectAsync()
    {
        if (string.IsNullOrWhiteSpace(_projectPath))
        {
            return;
        }

        var service = new TranslationProjectService();
        _project = await service.TryLoadAsync(_projectPath, CancellationToken.None);
        if (_project is null)
        {
            AppendLog($"未找到工程记录：{_projectPath}");
            TranslateButton.IsEnabled = false;
            SaveButton.IsEnabled = false;
            PackageButton.IsEnabled = false;
            return;
        }

        RenderProject(_project);
        TranslateButton.IsEnabled = true;
        SaveButton.IsEnabled = true;
        PackageButton.IsEnabled = true;
    }

    private void RenderProject(TranslationProject project)
    {
        _rows.Clear();
        foreach (var entry in project.Entries.OrderBy(e => e.SourceFile, StringComparer.OrdinalIgnoreCase).ThenBy(e => e.Id, StringComparer.Ordinal))
        {
            var row = ProjectEntryRow.FromEntry(entry);
            row.DisplaySourceFile = BuildDisplaySourceFile(project, row);
            _rows.Add(row);
        }

        AppendLog($"已加载工程记录：{_projectPath}（条目 {project.Entries.Count}）");
    }

    private static string BuildDisplaySourceFile(TranslationProject project, ProjectEntryRow row)
    {
        if (row.EntryKind != TranslationProjectEntryKind.LanguageString)
        {
            return row.SourceFile;
        }

        if (string.IsNullOrWhiteSpace(project.ModuleRootPath) || string.IsNullOrWhiteSpace(row.SourceFile))
        {
            return row.SourceFile;
        }

        if (row.SourceFile.Contains("(+", StringComparison.Ordinal))
        {
            return row.SourceFile;
        }

        try
        {
            var fullPath = Path.Combine(project.ModuleRootPath, row.SourceFile);
            if (File.Exists(fullPath))
            {
                return row.SourceFile;
            }

            if (row.SourceFile.Replace('\\', '/').EndsWith("moduledata/languages/std_module_strings_xml.xml", StringComparison.OrdinalIgnoreCase))
            {
                return $"（将生成）{row.SourceFile}";
            }

            return $"（将生成）{row.SourceFile}";
        }
        catch
        {
            return row.SourceFile;
        }
    }

    private async Task SaveProjectAsync()
    {
        if (_project is null || string.IsNullOrWhiteSpace(_projectPath))
        {
            return;
        }

        var map = _rows
            .GroupBy(row => row.BuildKey(), StringComparer.Ordinal)
            .ToDictionary(group => group.Key, group => group.Last().TargetText, StringComparer.Ordinal);
        foreach (var entry in _project.Entries)
        {
            var key = entry.EntryKind == TranslationProjectEntryKind.LanguageString
                ? $"{entry.EntryKind}|{entry.SourceFile}|{entry.Id}|{entry.SourceTextBase64}"
                : $"{entry.EntryKind}|{entry.SourceFile}|{entry.Id}";
            if (map.TryGetValue(key, out var target))
            {
                entry.TargetText = target;
            }
        }

        var service = new TranslationProjectService();
        await service.SaveAsync(_projectPath, _project, CancellationToken.None);
    }

    private async Task RunAsync(string name, Func<IProgress<string>, Task> action)
    {
        SetBusy(true);
        AppendLog($"[{name}] 开始。");
        try
        {
            var progress = new Progress<string>(AppendLog);
            await action(progress);
            AppendLog($"[{name}] 完成。");
        }
        catch (Exception exception)
        {
            AppendLog($"[{name}] 失败：{exception.Message}");
            System.Windows.MessageBox.Show(this, exception.Message, "操作失败", MessageBoxButton.OK, MessageBoxImage.Error);
        }
        finally
        {
            SetBusy(false);
        }
    }

    private void SetBusy(bool busy)
    {
        _isBusy = busy;
        ScanButton.IsEnabled = !busy;
        TranslateButton.IsEnabled = !busy && _project is not null;
        SaveButton.IsEnabled = !busy && _project is not null;
        PackageButton.IsEnabled = !busy && _project is not null;
    }

    private void AppendLog(string message)
    {
        LogTextBox.AppendText($"[{DateTime.Now:HH:mm:ss}] {message}{Environment.NewLine}");
        LogTextBox.ScrollToEnd();
    }
}
