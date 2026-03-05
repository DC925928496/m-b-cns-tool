using System.Security.Cryptography;
using System.Text;

namespace MbCnsTool.Core.Services;

/// <summary>
/// 自动 id 生成器（用于 DLL 硬编码条目）。
/// </summary>
public static class AutoIdGenerator
{
    /// <summary>
    /// 基于“所属文件 + 源文”生成稳定 id。
    /// </summary>
    public static string BuildStableId(string sourceFile, string sourceText)
    {
        var material = $"{sourceFile}\n{sourceText}";
        var bytes = Encoding.UTF8.GetBytes(material);
        var hash = SHA256.HashData(bytes);
        var hex = Convert.ToHexString(hash).ToLowerInvariant();
        return $"auto_{hex[..16]}";
    }
}

