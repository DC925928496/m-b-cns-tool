using MbCnsTool.Core.Abstractions;

namespace MbCnsTool.Core.Services.Providers;

/// <summary>
/// 翻译提供方工厂。
/// </summary>
public sealed class TranslationProviderFactory
{
    private readonly HttpClient _httpClient;

    /// <summary>
    /// 初始化工厂。
    /// </summary>
    public TranslationProviderFactory(HttpClient httpClient)
    {
        _httpClient = httpClient;
    }

    /// <summary>
    /// 创建所有内置翻译器。
    /// </summary>
    public IReadOnlyDictionary<string, ITranslationProvider> CreateAll()
    {
        var providers = new Dictionary<string, ITranslationProvider>(StringComparer.OrdinalIgnoreCase)
        {
            ["google_free"] = new GoogleFreeTranslator(_httpClient),
            ["fallback"] = new FallbackEchoTranslator()
        };

        return providers;
    }
}
