using MbCnsTool.Core.Services;

namespace MbCnsTool.Tests;

/// <summary>
/// 编码归一化测试。
/// </summary>
public sealed class TextEncodingNormalizerTests
{
    [Fact]
    public void NormalizeMojibake_Should_Fix_Utf8_1252_Garbled_Text()
    {
        const string source = "â€” Dismiss Soldiers â€”";

        var normalized = TextEncodingNormalizer.NormalizeMojibake(source);

        Assert.Equal("— Dismiss Soldiers —", normalized);
    }

    [Fact]
    public void NormalizeMojibake_Should_Keep_Normal_Text()
    {
        const string source = "{=enl_retinue_select_type_prompt}Select a soldier type to begin.";

        var normalized = TextEncodingNormalizer.NormalizeMojibake(source);

        Assert.Equal(source, normalized);
    }
}
