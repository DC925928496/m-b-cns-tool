using MbCnsTool.Core.Models;

namespace MbCnsTool.Core.Abstractions;

/// <summary>
/// 翻译提供方接口。
/// </summary>
public interface ITranslationProvider
{
    /// <summary>
    /// 提供方名称。
    /// </summary>
    string Name { get; }

    /// <summary>
    /// 当前环境是否可用。
    /// </summary>
    bool IsAvailable { get; }

    /// <summary>
    /// 翻译单条文本。
    /// </summary>
    Task<string?> TranslateAsync(TranslationProviderRequest request, CancellationToken cancellationToken);
}
