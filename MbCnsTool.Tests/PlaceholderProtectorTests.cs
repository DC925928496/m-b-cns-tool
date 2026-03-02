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
}
