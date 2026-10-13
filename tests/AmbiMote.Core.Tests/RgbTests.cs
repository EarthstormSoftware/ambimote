using AmbiMote.Core;

namespace AmbiMote.Core.Tests;

public class RgbTests
{
    [Theory]
    [InlineData("ff8000")]
    [InlineData("#FF8000")]
    [InlineData(" ff8000 ")]
    public void TryParse_ReadsHexColours(string text)
    {
        Assert.True(Rgb.TryParse(text, out var colour));
        Assert.Equal(new Rgb(255, 128, 0), colour);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("fff")]
    [InlineData("gg0000")]
    [InlineData("#ff80001")]
    public void TryParse_RejectsAnythingElse(string? text)
    {
        Assert.False(Rgb.TryParse(text, out _));
    }

    [Fact]
    public void Scale_RoundsAndClamps()
    {
        Assert.Equal(new Rgb(128, 64, 0), new Rgb(255, 128, 0).Scale(0.5));
        Assert.Equal(new Rgb(255, 128, 0), new Rgb(255, 128, 0).Scale(2.0));
        Assert.Equal(Rgb.Black, new Rgb(255, 128, 0).Scale(-1.0));
    }

    [Fact]
    public void ToString_IsHex()
    {
        Assert.Equal("#ff8000", new Rgb(255, 128, 0).ToString());
    }
}
