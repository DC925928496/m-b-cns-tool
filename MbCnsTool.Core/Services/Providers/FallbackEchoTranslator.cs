using MbCnsTool.Core.Abstractions;
using MbCnsTool.Core.Models;

namespace MbCnsTool.Core.Services.Providers;

/// <summary>
/// 最后兜底提供方，不依赖网络。
/// </summary>
public sealed class FallbackEchoTranslator : ITranslationProvider
{
    /// <inheritdoc />
    public string Name => "fallback";

    /// <inheritdoc />
    public bool IsAvailable => true;

    /// <inheritdoc />
    public Task<string?> TranslateAsync(TranslationProviderRequest request, CancellationToken cancellationToken)
    {
        return Task.FromResult<string?>($"[待人工润色]{request.Text}");
    }
}
