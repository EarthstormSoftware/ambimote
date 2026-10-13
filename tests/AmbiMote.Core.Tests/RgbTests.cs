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

    [Theory]
    [InlineData(0.0, 1.0, 1.0, 255, 0, 0)]
    [InlineData(1.0 / 3, 1.0, 1.0, 0, 255, 0)]
    [InlineData(2.0 / 3, 1.0, 1.0, 0, 0, 255)]
    [InlineData(1.0 / 12, 1.0, 1.0, 255, 128, 0)]
    [InlineData(1.25, 1.0, 1.0, 128, 255, 0)]
    [InlineData(-0.25, 1.0, 1.0, 128, 0, 255)]
    [InlineData(0.5, 0.0, 0.5, 128, 128, 128)]
    public void FromHsv_ConvertsAndWrapsHue(double hue, double saturation, double value, byte r, byte g, byte b)
    {
        Assert.Equal(new Rgb(r, g, b), Rgb.FromHsv(hue, saturation, value));
    }

    [Theory]
    [InlineData(1900, 255, 132, 0)]
    [InlineData(2700, 255, 167, 87)]
    [InlineData(6500, 255, 254, 250)]
    [InlineData(6600, 255, 255, 255)]
    [InlineData(10000, 202, 218, 255)]
    public void FromKelvin_FollowsTheBlackBodyCurve(double kelvin, byte r, byte g, byte b)
    {
        Assert.Equal(new Rgb(r, g, b), Rgb.FromKelvin(kelvin));
    }

    [Fact]
    public void FromKelvin_WarmerIsRedder()
    {
        var warm = Rgb.FromKelvin(2700);
        var cool = Rgb.FromKelvin(6500);

        Assert.True(warm.B < cool.B && warm.G < cool.G);
    }
}
