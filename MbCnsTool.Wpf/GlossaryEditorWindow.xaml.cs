using System.Collections.ObjectModel;
using System.Diagnostics;
using System.IO;
using System.Text;
using System.Text.Json;
using System.Windows;
using MessageBox = System.Windows.MessageBox;
using OpenFileDialog = Microsoft.Win32.OpenFileDialog;

namespace MbCnsTool.Wpf;

/// <summary>
/// 术语表可视化编辑窗口。
/// </summary>
public partial class GlossaryEditorWindow : Window
{
    private readonly string _glossaryPath;
    private readonly string _templatePath;
    private readonly ObservableCollection<GlossaryEntry> _entries = [];

    /// <summary>
    /// 初始化术语编辑窗口。
    /// </summary>
    public GlossaryEditorWindow(string glossaryPath, string templatePath)
    {
        InitializeComponent();
        _glossaryPath = glossaryPath;
        _templatePath = templatePath;
        PathTextBlock.Text = $"固定术语表路径：{_glossaryPath}";
        TemplatePathTextBlock.Text = $"JSON 模板路径：{_templatePath}";
        EnsureGlossaryExists();
        LoadEntries();
        GlossaryDataGrid.ItemsSource = _entries;
    }

    private void EnsureGlossaryExists()
    {
        var directory = Path.GetDirectoryName(_glossaryPath);
        if (!string.IsNullOrWhiteSpace(directory))
        {
            Directory.CreateDirectory(directory);
        }

        if (File.Exists(_glossaryPath))
        {
            return;
        }

        File.WriteAllLines(_glossaryPath,
        [
            "# 骑砍2常用术语",
            "Denar=第纳尔",
            "Bannerlord=霸主"
        ], new UTF8Encoding(false));
    }

    private void LoadEntries()
    {
        _entries.Clear();
        var lines = File.ReadAllLines(_glossaryPath);
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

            _entries.Add(new GlossaryEntry
            {
                Source = split[0],
                Target = split[1]
            });
        }
    }

    private void OnImportJsonClick(object sender, RoutedEventArgs e)
    {
        var dialog = new OpenFileDialog
        {
            Title = "导入术语 JSON 文件",
            Filter = "JSON 文件|*.json|所有文件|*.*",
            CheckFileExists = true
        };

        if (dialog.ShowDialog(this) != true)
        {
            return;
        }

        try
        {
            var imported = ParseGlossaryJson(dialog.FileName);
            var mergedCount = MergeImportedEntries(imported);
            MessageBox.Show(this, $"导入完成：共合并 {mergedCount} 条（自动去重）。", "导入成功", MessageBoxButton.OK, MessageBoxImage.Information);
        }
        catch (Exception exception)
        {
            MessageBox.Show(this, $"导入失败：{exception.Message}", "导入失败", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private void OnOpenTemplateClick(object sender, RoutedEventArgs e)
    {
        if (!File.Exists(_templatePath))
        {
            MessageBox.Show(this, "模板文件不存在。", "提示", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        Process.Start(new ProcessStartInfo
        {
            FileName = _templatePath,
            UseShellExecute = true
        });
    }

    private IReadOnlyList<GlossaryEntry> ParseGlossaryJson(string filePath)
    {
        var text = File.ReadAllText(filePath);
        using var document = JsonDocument.Parse(text);
        var result = new List<GlossaryEntry>();

        if (document.RootElement.ValueKind == JsonValueKind.Object &&
            document.RootElement.TryGetProperty("entries", out var entriesNode) &&
            entriesNode.ValueKind == JsonValueKind.Array)
        {
            result.AddRange(ParseEntryArray(entriesNode));
            return result;
        }

        if (document.RootElement.ValueKind == JsonValueKind.Array)
        {
            result.AddRange(ParseEntryArray(document.RootElement));
            return result;
        }

        throw new InvalidOperationException("JSON 格式不正确。请使用模板格式。");
    }

    private static IEnumerable<GlossaryEntry> ParseEntryArray(JsonElement arrayNode)
    {
        foreach (var item in arrayNode.EnumerateArray())
        {
            if (item.ValueKind != JsonValueKind.Object)
            {
                continue;
            }

            var source = item.TryGetProperty("source", out var sourceNode) ? sourceNode.GetString() : null;
            var target = item.TryGetProperty("target", out var targetNode) ? targetNode.GetString() : null;
            if (string.IsNullOrWhiteSpace(source) || string.IsNullOrWhiteSpace(target))
            {
                continue;
            }

            yield return new GlossaryEntry
            {
                Source = source.Trim(),
                Target = target.Trim()
            };
        }
    }

    private int MergeImportedEntries(IReadOnlyList<GlossaryEntry> imported)
    {
        var map = _entries
            .Where(entry => !string.IsNullOrWhiteSpace(entry.Source))
            .ToDictionary(entry => entry.Source.Trim(), StringComparer.OrdinalIgnoreCase);

        var changed = 0;
        foreach (var entry in imported)
        {
            if (map.TryGetValue(entry.Source, out var exists))
            {
                if (!string.Equals(exists.Target.Trim(), entry.Target.Trim(), StringComparison.Ordinal))
                {
                    exists.Target = entry.Target.Trim();
                    changed++;
                }

                continue;
            }

            _entries.Add(new GlossaryEntry
            {
                Source = entry.Source.Trim(),
                Target = entry.Target.Trim()
            });
            map[entry.Source.Trim()] = entry;
            changed++;
        }

        return changed;
    }

    private void OnDeleteSelected(object sender, RoutedEventArgs e)
    {
        if (GlossaryDataGrid.SelectedItem is not GlossaryEntry entry)
        {
            return;
        }

        _entries.Remove(entry);
    }

    private void OnSaveClick(object sender, RoutedEventArgs e)
    {
        var lines = _entries
            .Where(entry => !string.IsNullOrWhiteSpace(entry.Source) && !string.IsNullOrWhiteSpace(entry.Target))
            .GroupBy(entry => entry.Source.Trim(), StringComparer.OrdinalIgnoreCase)
            .Select(group => group.Last())
            .Select(entry => $"{entry.Source.Trim()}={entry.Target.Trim()}")
            .ToArray();

        File.WriteAllLines(_glossaryPath, lines, new UTF8Encoding(false));
        MessageBox.Show(this, "术语表已保存。", "完成", MessageBoxButton.OK, MessageBoxImage.Information);
    }

    private void OnCloseClick(object sender, RoutedEventArgs e)
    {
        Close();
    }

    /// <summary>
    /// 术语项。
    /// </summary>
    public sealed class GlossaryEntry
    {
        /// <summary>
        /// 原文。
        /// </summary>
        public string Source { get; set; } = string.Empty;

        /// <summary>
        /// 译文。
        /// </summary>
        public string Target { get; set; } = string.Empty;
    }
}
