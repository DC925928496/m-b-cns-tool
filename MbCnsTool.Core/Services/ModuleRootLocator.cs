namespace MbCnsTool.Core.Services;

/// <summary>
/// Mod 模块根目录定位器（用于在传入父目录时定位唯一的 <c>SubModule.xml</c>）。
/// </summary>
public static class ModuleRootLocator
{
    /// <summary>
    /// 定位模块根目录（包含 <c>SubModule.xml</c> 的目录）。
    /// </summary>
    public static string Resolve(string inputPath)
    {
        if (string.IsNullOrWhiteSpace(inputPath))
        {
            throw new ArgumentException("Mod 路径为空。", nameof(inputPath));
        }

        if (!Directory.Exists(inputPath))
        {
            throw new DirectoryNotFoundException($"未找到 Mod 路径：{inputPath}");
        }

        if (File.Exists(Path.Combine(inputPath, "SubModule.xml")))
        {
            return inputPath;
        }

        var children = Directory
            .EnumerateDirectories(inputPath)
            .Where(directory => File.Exists(Path.Combine(directory, "SubModule.xml")))
            .ToArray();
        if (children.Length == 1)
        {
            return children[0];
        }

        throw new InvalidOperationException($"无法定位唯一 Mod 根目录：{inputPath}");
    }
}

