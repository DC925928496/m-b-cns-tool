using System.ComponentModel;
using System.Runtime.CompilerServices;
using MbCnsTool.Core.Models;

namespace MbCnsTool.Wpf.ViewModels;

/// <summary>
/// DataGrid 行模型（可编辑译文）。
/// </summary>
public sealed class ProjectEntryRow : INotifyPropertyChanged
{
    private string _targetText = string.Empty;

    public required TranslationProjectEntryKind EntryKind { get; init; }

    public required string SourceFile { get; init; }

    public string DisplaySourceFile { get; set; } = string.Empty;

    public required string Id { get; init; }

    public required string SourceText { get; init; }

    public required string SourceTextBase64 { get; init; }

    public string TargetText
    {
        get => _targetText;
        set
        {
            if (string.Equals(_targetText, value, StringComparison.Ordinal))
            {
                return;
            }

            _targetText = value ?? string.Empty;
            OnPropertyChanged();
        }
    }

    public static ProjectEntryRow FromEntry(TranslationProjectEntry entry)
    {
        return new ProjectEntryRow
        {
            EntryKind = entry.EntryKind,
            SourceFile = entry.SourceFile,
            DisplaySourceFile = entry.SourceFile,
            Id = entry.Id,
            SourceText = entry.SourceText,
            SourceTextBase64 = entry.SourceTextBase64,
            TargetText = entry.TargetText ?? string.Empty
        };
    }

    public string BuildKey()
    {
        if (EntryKind == TranslationProjectEntryKind.LanguageString)
        {
            return $"{EntryKind}|{SourceFile}|{Id}|{SourceTextBase64}";
        }

        return $"{EntryKind}|{SourceFile}|{Id}";
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    private void OnPropertyChanged([CallerMemberName] string? name = null)
    {
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
    }
}
