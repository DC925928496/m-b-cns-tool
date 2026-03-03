using System.Windows;
using MbCnsTool.Core.Models;

namespace MbCnsTool.Wpf;

/// <summary>
/// 翻译对比编辑窗口。
/// </summary>
public partial class TranslationReviewWindow : Window
{
    /// <summary>
    /// 当前编辑快照。
    /// </summary>
    public TranslationReviewSnapshot Snapshot { get; }

    /// <summary>
    /// 编辑条目集合。
    /// </summary>
    public IReadOnlyList<TranslationReviewEntry> Entries => Snapshot.Entries;

    /// <summary>
    /// 初始化窗口。
    /// </summary>
    public TranslationReviewWindow(TranslationReviewSnapshot snapshot)
    {
        Snapshot = snapshot;
        InitializeComponent();
        DataContext = this;
    }

    private void OnSave(object sender, RoutedEventArgs e)
    {
        DialogResult = true;
    }

    private void OnCancel(object sender, RoutedEventArgs e)
    {
        DialogResult = false;
    }
}
