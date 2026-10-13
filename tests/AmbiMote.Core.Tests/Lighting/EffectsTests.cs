using AmbiMote.Core.Lighting;

namespace AmbiMote.Core.Tests.Lighting;

public class EffectsTests
{
    private static double[] EvenPositions(int count) => [.. Enumerable.Range(0, count).Select(i => (double)i / count)];

    [Fact]
    public void Rainbow_SpreadsHuesAroundTheSetup()
    {
        var pixels = new Rgb[6];

        new RainbowEffect().Render(0, EvenPositions(6), pixels);

        Assert.Equal(
            [new(255, 0, 0), new(255, 255, 0), new(0, 255, 0), new(0, 255, 255), new(0, 0, 255), new(255, 0, 255)],
            pixels);
    }

    [Fact]
    public void Rainbow_MovesWithTime()
    {
        var start = new Rgb[6];
        var later = new Rgb[6];
        var positions = EvenPositions(6);
        var rainbow = new RainbowEffect(periodSeconds: 6);

        rainbow.Render(0, positions, start);
        rainbow.Render(1, positions, later);

        // After a sixth of the period each colour has moved one pixel along.
        Assert.Equal(start[..5], later[1..]);
    }

    [Theory]
    [InlineData(0, BreatheEffect.Low)]
    [InlineData(3, 1.0)]
    [InlineData(6, BreatheEffect.Low)]
    public void Breathe_GoesFromDimToBrightAndBack(double seconds, double level)
    {
        var pixels = new Rgb[3];

        new BreatheEffect(new Rgb(200, 100, 0), periodSeconds: 6).Render(seconds, EvenPositions(3), pixels);

        Assert.All(pixels, p => Assert.Equal(new Rgb(200, 100, 0).Scale(level), p));
    }

    [Fact]
    public void Chase_LightsTheHeadBrightestAndFadesBehindIt()
    {
        var pixels = new Rgb[8];

        // Half way round: the head is on pixel 4.
        new ChaseEffect(Rgb.White, periodSeconds: 8, tail: 2).Render(4, EvenPositions(8), pixels);

        Assert.Equal(Rgb.White, pixels[4]);
        Assert.True(pixels[3].R is > 0 and < 255);
        Assert.True(pixels[2].R < pixels[3].R);
        Assert.Equal(Rgb.Black, pixels[1]);
        Assert.Equal(Rgb.Black, pixels[5]);
    }

    [Fact]
    public void Chase_TailWrapsAroundTheEnd()
    {
        var pixels = new Rgb[8];

        new ChaseEffect(Rgb.White, periodSeconds: 8, tail: 2).Render(0, EvenPositions(8), pixels);

        Assert.Equal(Rgb.White, pixels[0]);
        Assert.NotEqual(Rgb.Black, pixels[7]);
        Assert.Equal(Rgb.Black, pixels[1]);
    }

    [Fact]
    public void Candle_StaysWithinItsRangeAndFlickers()
    {
        var colour = Rgb.FromKelvin(1900);
        var candle = new CandleEffect(colour, new Random(1));
        var positions = EvenPositions(16);
        var pixels = new Rgb[16];
        var reds = new HashSet<byte>();

        for (int frame = 0; frame < 300; frame++)
        {
            candle.Render(frame / 30.0, positions, pixels);
            foreach (var p in pixels)
            {
                Assert.InRange(p.R, (byte)Math.Floor(colour.R * CandleEffect.Low), colour.R);
                Assert.True(p.G <= colour.G && p.B <= colour.B);
                reds.Add(p.R);
            }
        }

        Assert.True(reds.Count > 10, "The candle should flicker.");
    }
}
