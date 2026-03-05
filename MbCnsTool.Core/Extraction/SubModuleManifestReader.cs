using System.Xml.Linq;

namespace MbCnsTool.Core.Extraction;

/// <summary>
/// SubModule.xml 读取工具（用于解析 DLL 声明清单）。
/// </summary>
internal static class SubModuleManifestReader
{
    public static string[] ResolveDeclaredDllNames(string moduleRootPath)
    {
        var path = Path.Combine(moduleRootPath, "SubModule.xml");
        if (!File.Exists(path))
        {
            return [];
        }

        try
        {
            var document = XDocument.Load(path, LoadOptions.PreserveWhitespace);
            return document
                .Descendants()
                .Where(element => element.Name.LocalName.Equals("DLLName", StringComparison.OrdinalIgnoreCase))
                .Select(element => element.Attributes().FirstOrDefault(attribute =>
                        attribute.Name.LocalName.Equals("value", StringComparison.OrdinalIgnoreCase) ||
                        attribute.Name.LocalName.Equals("Value", StringComparison.OrdinalIgnoreCase))
                    ?.Value)
                .Where(value => !string.IsNullOrWhiteSpace(value))
                .Select(value => value!.Trim())
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToArray();
        }
        catch
        {
            return [];
        }
    }
}

