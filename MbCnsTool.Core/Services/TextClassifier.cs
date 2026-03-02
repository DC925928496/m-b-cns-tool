using MbCnsTool.Core.Models;

namespace MbCnsTool.Core.Services;

/// <summary>
/// 基于文件路径与键名进行文本类型识别。
/// </summary>
public sealed class TextClassifier
{
    /// <summary>
    /// 分类文本。
    /// </summary>
    public TextCategory Classify(string relativePath, string keyName)
    {
        var path = relativePath.Replace('\\', '/').ToLowerInvariant();
        var key = keyName.ToLowerInvariant();

        if (path.Contains("/dialogue/") || key.Contains("dialogue") || key is "text" or "line" && path.Contains("qm_"))
        {
            return TextCategory.对话;
        }

        if (path.Contains("/equipment/") || key.Contains("item") || key.Contains("gear") || key.Contains("weapon"))
        {
            return TextCategory.物品;
        }

        if (path.Contains("/prefabs/") || path.Contains("/gui/") || key.Contains("menu") || key.Contains("title") || key.Contains("label"))
        {
            return TextCategory.菜单;
        }

        if (key.Contains("error") || key.Contains("warning") || key.Contains("tip") || key.Contains("message"))
        {
            return TextCategory.系统;
        }

        return TextCategory.通用;
    }
}
