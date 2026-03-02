namespace MbCnsTool.Wpf.Models;

/// <summary>
/// 引擎选项。
/// </summary>
public sealed class EngineOption
{
    /// <summary>
    /// 引擎键名。
    /// </summary>
    public required string ProviderKey { get; init; }

    /// <summary>
    /// 展示名称。
    /// </summary>
    public required string DisplayName { get; init; }

    /// <summary>
    /// 是否为自定义引擎占位项。
    /// </summary>
    public bool IsCustom { get; init; }

    /// <inheritdoc />
    public override string ToString()
    {
        return DisplayName;
    }
}
