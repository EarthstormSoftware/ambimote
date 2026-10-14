using AmbiMote.Core.Mote;

namespace AmbiMote.Core.Tests.Mote;

public class PowerLimitTests
{
    private static Rgb[]?[] Sticks(Rgb colour, int count = 4) =>
        Enumerable.Range(0, count).Select(_ => Enumerable.Repeat(colour, 16).ToArray()).ToArray<Rgb[]?>();

    [Fact]
    public void EstimateMilliamps_IsSixtyMilliampsPerWhitePixel()
    {
        Assert.Equal(64 * 60.0, PowerLimit.EstimateMilliamps(Sticks(Rgb.White)), precision: 6);
    }

    [Fact]
    public void Apply_ScalesByBrightness()
    {
        var result = PowerLimit.Apply(Sticks(Rgb.White, 1), 0.5, maxMilliamps: null);

        Assert.All(result[0]!, p => Assert.Equal(new Rgb(128, 128, 128), p));
    }

    [Fact]
    public void Apply_ScalesDownToStayWithinTheBudget()
    {
        var result = PowerLimit.Apply(Sticks(Rgb.White), 1.0, maxMilliamps: 400);

        Assert.InRange(PowerLimit.EstimateMilliamps(result), 380, 400);
    }

    [Fact]
    public void Apply_BrightnessIsAFractionOfWhatTheBudgetAllows()
    {
        var full = PowerLimit.Apply(Sticks(Rgb.White), 1.0, maxMilliamps: 400);
        var half = PowerLimit.Apply(Sticks(Rgb.White), 0.5, maxMilliamps: 400);

        Assert.InRange(PowerLimit.EstimateMilliamps(half), 190, 200);
        Assert.True(half[0]![0].R < full[0]![0].R);
    }

    [Fact]
    public void Apply_LeavesFramesWithinTheBudgetAlone()
    {
        var input = Sticks(new Rgb(10, 0, 0));

        var result = PowerLimit.Apply(input, 1.0, maxMilliamps: 400);

        Assert.Equal(input[0], result[0]);
    }

    [Fact]
    public void Apply_KeepsUnconfiguredChannelsAndDoesNotChangeTheInput()
    {
        Rgb[]?[] input = [[Rgb.White], null];

        var result = PowerLimit.Apply(input, 0.5, maxMilliamps: null);

        Assert.Null(result[1]);
        Assert.Equal(Rgb.White, input[0]![0]);
    }
}
