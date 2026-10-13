using AmbiMote.Core.Lighting;

namespace AmbiMote.Core.Tests.Lighting;

public class ColoursTests
{
    [Theory]
    [InlineData("red", 255, 0, 0)]
    [InlineData("WHITE", 255, 255, 255)]
    [InlineData(" blue ", 0, 0, 255)]
    [InlineData("ff8000", 255, 128, 0)]
    [InlineData("#00ff80", 0, 255, 128)]
    public void TryParse_NamesAndHex(string text, byte r, byte g, byte b)
    {
        Assert.True(Colours.TryParse(text, out var colour));
        Assert.Equal(new Rgb(r, g, b), colour);
    }

    [Theory]
    [InlineData("warm", 2700)]
    [InlineData("neutral", 4000)]
    [InlineData("daylight", 6500)]
    [InlineData("3000k", 3000)]
    [InlineData("3000K", 3000)]
    public void TryParse_ColourTemperatures(string text, int kelvin)
    {
        Assert.True(Colours.TryParse(text, out var colour));
        Assert.Equal(Rgb.FromKelvin(kelvin), colour);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("chartreuse")]
    [InlineData("500k")]
    [InlineData("50000k")]
    [InlineData("-3000k")]
    [InlineData("k")]
    [InlineData("ff80")]
    public void TryParse_RejectsOtherText(string? text)
    {
        Assert.False(Colours.TryParse(text, out _));
    }

    [Fact]
    public void Names_AllParse()
    {
        Assert.All(Colours.Names, static name => Assert.True(Colours.TryParse(name, out _)));
    }
}
