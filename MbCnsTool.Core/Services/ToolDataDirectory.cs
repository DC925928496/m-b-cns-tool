namespace MbCnsTool.Core.Services;

/// <summary>
/// 工具数据目录解析器。
/// </summary>
public static class ToolDataDirectory
{
    /// <summary>
    /// 返回工具数据目录（默认位于程序目录下的 <c>data</c>），并确保目录存在。
    /// </summary>
    public static string Resolve()
    {
        var baseDir = AppContext.BaseDirectory;
        if (string.IsNullOrWhiteSpace(baseDir))
        {
            throw new InvalidOperationException("无法解析工具目录（AppContext.BaseDirectory 为空）。");
        }

        var dataRoot = Path.Combine(baseDir, "data");
        Directory.CreateDirectory(dataRoot);
        return dataRoot;
    }
}

