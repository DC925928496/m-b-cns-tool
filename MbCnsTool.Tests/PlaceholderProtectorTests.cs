using MbCnsTool.Core.Services;

namespace MbCnsTool.Tests;

/// <summary>
/// 占位符保护测试。
/// </summary>
public sealed class PlaceholderProtectorTests
{
    [Fact]
    public void Protect_And_Restore_Should_Keep_All_Placeholders()
    {
        var protector = new PlaceholderProtector();
        const string source = "Hello {PLAYER_NAME}, rank is <RANK> and %{TOKEN}%";

        var protectedText = protector.Protect(source);
        var translated = "你好 __PH_0__，军衔是 __PH_1__ 并且 __PH_2__";
        var restored = protector.Restore(translated, protectedText.Tokens);

        Assert.Equal("你好 {PLAYER_NAME}，军衔是 <RANK> 并且 %{TOKEN}%", restored);
        Assert.True(protector.IsPlaceholderSafe(source, restored));
    }

    [Fact]
    public void Protect_Should_Keep_DoubleBrace_Block_As_Whole()
    {
        var protector = new PlaceholderProtector();
        const string source = "{{ Mod = {0}, declaringType = {1}, method = {2} }}";

        var protectedText = protector.Protect(source);
        var translated = "译文 __PH_0__";
        var restored = protector.Restore(translated, protectedText.Tokens);

        Assert.Equal("__PH_0__", protectedText.Text);
        Assert.Equal("译文 {{ Mod = {0}, declaringType = {1}, method = {2} }}", restored);
        Assert.True(protector.IsPlaceholderSafe(source, restored));
        Assert.True(protector.IsDoubleBraceBlockSafe(source, restored));
    }

    [Fact]
    public void IsDoubleBraceBlockSafe_Should_Detect_Translated_Block()
    {
        var protector = new PlaceholderProtector();
        const string source = "{{ Mod = {0}, declaringType = {1}, method = {2} }}";
        const string translated = "{{ Mod = {0}，声明类型 = {1}，方法 = {2} }}";

        var safe = protector.IsDoubleBraceBlockSafe(source, translated);

        Assert.False(safe);
    }

    [Fact]
    public void IsPlaceholderSafe_Should_Detect_Changed_TranslationId_Block()
    {
        var protector = new PlaceholderProtector();
        const string source = "{=enl_retinue_select_type_prompt}Select a soldier type to begin.";
        const string translated = "{=enl_雷特inue_select_type_prompt}选择一个士兵类型开始。";

        var safe = protector.IsPlaceholderSafe(source, translated);

        Assert.False(safe);
    }
}
