using MbCnsTool.Core.Services;

namespace MbCnsTool.Tests;

/// <summary>
/// 文本过滤规则测试。
/// </summary>
public sealed class TextRulesTests
{
    [Theory]
    [InlineData("Cancel")]
    [InlineData("OK")]
    [InlineData("Sword")]
    [InlineData("Longsword")]
    public void IsTranslatableString_EnglishWord_Should_Be_True(string text)
    {
        Assert.True(TextRules.IsTranslatableString(text));
    }

    [Theory]
    [InlineData("weapon_sword")]
    [InlineData("foo/bar")]
    [InlineData("a-b")]
    [InlineData("a.b")]
    public void IsTranslatableString_LooksLikeIdentifier_Should_Be_False(string text)
    {
        Assert.False(TextRules.IsTranslatableString(text));
    }
}

