using AmbiMote.Core.Processing;

namespace AmbiMote.Core.Tests.Processing;

public class ColourProcessorTests
{
    [Fact]
    public void None_LeavesColoursUnchanged()
    {
        var colour = new Rgb(12, 128, 250);

        Assert.Equal(colour, ColourProcessor.None.Process(colour));
    }

    [Theory]
    [InlineData(0, 0)]
    [InlineData(48, 6)]
    [InlineData(128, 56)]
    [InlineData(255, 255)]
    public void Gamma_DimsDarkerValuesMore(byte screen, byte led)
    {
        var processor = new ColourProcessor(gamma: 2.2, saturation: 1);

        Assert.Equal(new Rgb(led, led, led), processor.Process(new Rgb(screen, screen, screen)));
    }

    [Fact]
    public void Saturation_LeavesGreyAlone()
    {
        var processor = new ColourProcessor(gamma: 1, saturation: 2);

        Assert.Equal(new Rgb(100, 100, 100), processor.Process(new Rgb(100, 100, 100)));
    }

    [Fact]
    public void Saturation_PushesColoursAwayFromGrey()
    {
        var processor = new ColourProcessor(gamma: 1, saturation: 1.5);

        var result = processor.Process(new Rgb(150, 100, 100));

        Assert.True(result.R > 150, $"Red should go up, was {result.R}.");
        Assert.True(result.G < 100 && result.B < 100, $"Green and blue should go down, were {result.G} and {result.B}.");
    }

    [Fact]
    public void Saturation_Zero_MakesGrey()
    {
        var processor = new ColourProcessor(gamma: 1, saturation: 0);

        var result = processor.Process(new Rgb(255, 0, 0));

        Assert.Equal(result.R, result.G);
        Assert.Equal(result.G, result.B);
    }

    [Fact]
    public void Saturation_ClampsAtTheEnds()
    {
        var processor = new ColourProcessor(gamma: 1, saturation: 3);

        Assert.Equal(new Rgb(255, 0, 0), processor.Process(new Rgb(250, 10, 10)));
    }

    [Theory]
    [InlineData(0.4, 1)]
    [InlineData(4.5, 1)]
    [InlineData(2.2, -0.1)]
    [InlineData(2.2, 3.5)]
    [InlineData(double.NaN, 1)]
    public void Constructor_RejectsOutOfRangeSettings(double gamma, double saturation)
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => new ColourProcessor(gamma, saturation));
    }
}
