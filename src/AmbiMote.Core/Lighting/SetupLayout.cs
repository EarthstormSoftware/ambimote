using AmbiMote.Core.Capture;
using AmbiMote.Core.Sampling;

namespace AmbiMote.Core.Lighting;

/// <summary>One mounted stick, with the monitor it sits behind.</summary>
/// <param name="From">Where pixel 0 sits along the edge, from 0 to 1 (left to right, or top to bottom).</param>
/// <param name="To">Where the last pixel sits along the edge.</param>
public sealed record PlacedStick(DisplayOutput Monitor, ScreenEdge Edge, double From, double To, int PixelCount);

/// <summary>Works out where each LED sits around the whole setup, so effects can run around the monitors in order.</summary>
public static class SetupLayout
{
    /// <summary>
    /// The position of every pixel, stick by stick, from 0 to 1. Positions run clockwise around the outside of all the
    /// monitors together, starting from the bottom left: up the left side, along the top, down the right side and back
    /// along the bottom. Gaps between sticks stay as gaps, so an effect crosses them at the same speed.
    /// </summary>
    public static double[] Positions(IReadOnlyList<PlacedStick> sticks)
    {
        ArgumentNullException.ThrowIfNull(sticks);
        int count = sticks.Sum(static s => s.PixelCount);
        var positions = new double[count];
        if (count == 0)
        {
            return positions;
        }

        double left = sticks.Min(static s => s.Monitor.Left);
        double top = sticks.Min(static s => s.Monitor.Top);
        double right = sticks.Max(static s => s.Monitor.Left + s.Monitor.Width);
        double bottom = sticks.Max(static s => s.Monitor.Top + s.Monitor.Height);
        double width = right - left;
        double height = bottom - top;

        int p = 0;
        foreach (var stick in sticks)
        {
            var m = stick.Monitor;
            for (int i = 0; i < stick.PixelCount; i++)
            {
                double f = stick.From + ((stick.To - stick.From) * (i + 0.5) / stick.PixelCount);
                positions[p++] = stick.Edge switch
                {
                    ScreenEdge.Left => bottom - (m.Top + (f * m.Height)),
                    ScreenEdge.Top => height + (m.Left + (f * m.Width) - left),
                    ScreenEdge.Right => height + width + (m.Top + (f * m.Height) - top),
                    _ => (2 * height) + width + (right - (m.Left + (f * m.Width))),
                };
            }
        }

        // Scale to 0..1, leaving one average gap after the last pixel so a closed loop wraps round evenly.
        double min = positions.Min();
        double span = positions.Max() - min;
        double total = count > 1 ? span * count / (count - 1) : 1;
        for (int i = 0; i < count; i++)
        {
            positions[i] = total > 0 ? (positions[i] - min) / total : 0;
        }

        return positions;
    }
}
