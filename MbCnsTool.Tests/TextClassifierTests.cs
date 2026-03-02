using MbCnsTool.Core.Models;
using MbCnsTool.Core.Services;

namespace MbCnsTool.Tests;

/// <summary>
/// 文本分类测试。
/// </summary>
public sealed class TextClassifierTests
{
    [Theory]
    [InlineData("ModuleData/Enlisted/Dialogue/qm_dialogue.json", "text", TextCategory.对话)]
    [InlineData("GUI/Prefabs/Equipment/item.xml", "name", TextCategory.物品)]
    [InlineData("GUI/Prefabs/Interface/main.xml", "title", TextCategory.菜单)]
    [InlineData("ModuleData/Events/events.json", "warning_message", TextCategory.系统)]
    public void Classify_Should_Return_Expected_Category(string path, string key, TextCategory expected)
    {
        var classifier = new TextClassifier();
        var category = classifier.Classify(path, key);
        Assert.Equal(expected, category);
    }
}
