namespace MbCnsTool.Core.Models;

/// <summary>
/// 自定义 OpenAI 兼容引擎配置。
/// </summary>
public sealed class CustomOpenAiProviderOptions
{
    /// <summary>
    /// 提供方键名。
    /// </summary>
    public string ProviderKey { get; init; } = "custom_openai";

    /// <summary>
    /// 显示名称。
    /// </summary>
    public string DisplayName { get; init; } = "自定义OpenAI";

    /// <summary>
    /// API Key。
    /// </summary>
    public required string ApiKey { get; init; }

    /// <summary>
    /// 基础 URL。
    /// </summary>
    public required string BaseUrl { get; init; }

    /// <summary>
    /// 模型名。
    /// </summary>
    public required string Model { get; init; }
}
