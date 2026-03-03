using MbCnsTool.Core.Models;

namespace MbCnsTool.Core.Services;

/// <summary>
/// 缓存键构建工具。
/// </summary>
public static class TranslationCacheKeyBuilder
{
    /// <summary>
    /// 生成 DLL 字符串缓存键。
    /// </summary>
    public static string BuildDllKey(string sourceText, TranslationRunOptions options)
    {
        return $"{TextCategory.系统}|{options.StyleProfile}|DLL|{sourceText}";
    }
}
