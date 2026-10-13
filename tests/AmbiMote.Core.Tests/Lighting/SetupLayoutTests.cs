using AmbiMote.Core.Capture;
using AmbiMote.Core.Lighting;
using AmbiMote.Core.Sampling;

namespace AmbiMote.Core.Tests.Lighting;

public class SetupLayoutTests
{
    private static readonly DisplayOutput s_left = new("L", -1920, 0, 1920, 1080, false, "gpu");
    private static readonly DisplayOutput s_centre = new("C", 0, 0, 2560, 1440, false, "gpu");
    private static readonly DisplayOutput s_right = new("R", 2560, 0, 1920, 1080, false, "gpu");

    /// <summary>
    /// One Mote on the left half: channel 1 runs left from the middle of the centre monitor, channels 2 and 3 run
    /// left across the left monitor, and channel 4 runs down its left edge.
    /// </summary>
    private static readonly PlacedStick[] s_leftHalf =
    [
        new(s_centre, ScreenEdge.Top, 0.5, 0, 16),
        new(s_left, ScreenEdge.Top, 1, 0.5, 16),
        new(s_left, ScreenEdge.Top, 0.5, 0, 16),
        new(s_left, ScreenEdge.Left, 0, 1, 16),
    ];

    [Fact]
    public void Positions_RunClockwiseFromTheBottomLeft()
    {
        var positions = SetupLayout.Positions(s_leftHalf);

        // These sticks run anticlockwise from the middle, so walking the pixels backwards goes clockwise.
        Assert.Equal(0, positions[^1]);
        for (int i = positions.Length - 1; i > 0; i--)
        {
            Assert.True(positions[i - 1] > positions[i], $"Pixel {i - 1} should come after pixel {i}.");
        }

        Assert.True(positions[0] < 1);
    }

    [Fact]
    public void Positions_KeepGapsBetweenMonitors()
    {
        // Two sticks along the tops of monitors with a gap between them.
        var gapped = new DisplayOutput("G", 3000, 0, 1000, 1000, false, "gpu");
        var positions = SetupLayout.Positions(
        [
            new(new DisplayOutput("A", 0, 0, 1000, 1000, false, "gpu"), ScreenEdge.Top, 0, 1, 2),
            new(gapped, ScreenEdge.Top, 0, 1, 2),
        ]);

        double step = positions[1] - positions[0];
        double gap = positions[2] - positions[1];
        Assert.True(gap > 3 * step, $"The gap ({gap}) should be much bigger than a step ({step}).");
    }

    [Fact]
    public void Positions_BothHalvesMeetInTheMiddle()
    {
        PlacedStick[] rightHalf =
        [
            new(s_centre, ScreenEdge.Top, 0.5, 1, 16),
            new(s_right, ScreenEdge.Top, 0, 0.5, 16),
            new(s_right, ScreenEdge.Top, 0.5, 1, 16),
            new(s_right, ScreenEdge.Right, 0, 1, 16),
        ];

        var positions = SetupLayout.Positions([.. s_leftHalf, .. rightHalf]);

        // The last pixel of the left half's first stick and the first pixel of the right half sit either side of the middle.
        double leftOfMiddle = positions[0];
        double rightOfMiddle = positions[64];
        Assert.True(rightOfMiddle > leftOfMiddle);
        Assert.True(positions[64..].Zip(positions[65..]).All(static p => p.Second > p.First), "The right half runs clockwise.");
        Assert.True(positions[..64].All(p => p < rightOfMiddle), "The left half comes before the right half.");
    }

    [Fact]
    public void Positions_NoSticks_IsEmpty()
    {
        Assert.Empty(SetupLayout.Positions([]));
    }
}
