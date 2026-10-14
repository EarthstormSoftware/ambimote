using AmbiMote.Core.Capture;

namespace AmbiMote.Core.Sampling;

/// <summary>Averages strips along one edge of a captured image into one colour per LED.</summary>
public static class EdgeSampler
{
    /// <summary>
    /// Fills <paramref name="pixels"/> with the colours along <paramref name="edge"/>.
    /// Positions along an edge run left to right (top and bottom edges) or top to bottom (left and right edges),
    /// from 0 to 1. Pixel 0 sits at <paramref name="from"/> and the last pixel at <paramref name="to"/>,
    /// so <paramref name="from"/> greater than <paramref name="to"/> means the stick runs the other way.
    /// </summary>
    /// <param name="depth">How far into the screen to sample, as a fraction of the width or height. At least one pixel is used.</param>
    public static void Sample(BgraImage image, ScreenEdge edge, double from, double to, double depth, Span<Rgb> pixels)
    {
        ArgumentNullException.ThrowIfNull(image);
        ValidateFraction(from, nameof(from));
        ValidateFraction(to, nameof(to));
        if (depth is <= 0 or > 1)
        {
            throw new ArgumentOutOfRangeException(nameof(depth), depth, "Depth must be more than 0 and at most 1.");
        }

        bool horizontal = edge is ScreenEdge.Top or ScreenEdge.Bottom;
        int along = horizontal ? image.Width : image.Height;
        int across = horizontal ? image.Height : image.Width;
        int band = Math.Clamp((int)Math.Round(depth * across), 1, across);
        int bandStart = edge is ScreenEdge.Top or ScreenEdge.Left ? 0 : across - band;

        for (int i = 0; i < pixels.Length; i++)
        {
            double a = from + ((to - from) * i / pixels.Length);
            double b = from + ((to - from) * (i + 1) / pixels.Length);
            int start = Math.Clamp((int)Math.Floor(Math.Min(a, b) * along), 0, along - 1);
            int end = Math.Clamp((int)Math.Ceiling(Math.Max(a, b) * along), start + 1, along);

            pixels[i] = horizontal
                ? Average(image, start, end, bandStart, bandStart + band)
                : Average(image, bandStart, bandStart + band, start, end);
        }
    }

    /// <summary>
    /// Fills <paramref name="pixels"/> for a stick on a monitor beside the captured one, as if the picture carried on
    /// sideways. A stick on that monitor's left or right edge copies the image's <paramref name="side"/> edge at the same
    /// heights. A stick along its top or bottom fades from the image's nearest corner, at the end next to the image, to
    /// the colour of the top (or bottom) half of the <paramref name="side"/> edge at the far end.
    /// </summary>
    /// <param name="side">Which side of the image the stick's monitor is on: <see cref="ScreenEdge.Left"/> or <see cref="ScreenEdge.Right"/>.</param>
    /// <param name="edge">The edge of its own monitor the stick sits on.</param>
    /// <param name="from">Where pixel 0 sits along that edge, as for <see cref="Sample"/>.</param>
    /// <param name="to">Where the last pixel sits along that edge.</param>
    /// <param name="depth">How far into the image to sample, as for <see cref="Sample"/>. The corner is as wide as this too.</param>
    public static void Extend(BgraImage image, ScreenEdge side, ScreenEdge edge, double from, double to, double depth, Span<Rgb> pixels)
    {
        if (side is not (ScreenEdge.Left or ScreenEdge.Right))
        {
            throw new ArgumentOutOfRangeException(nameof(side), side, "The stick's monitor must be to the left or right of the image.");
        }

        if (edge is ScreenEdge.Left or ScreenEdge.Right)
        {
            Sample(image, side, from, to, depth, pixels);
            return;
        }

        ValidateFraction(from, nameof(from));
        ValidateFraction(to, nameof(to));
        Span<Rgb> one = stackalloc Rgb[1];
        bool left = side == ScreenEdge.Left;
        Sample(image, edge, left ? 0 : 1 - depth, left ? depth : 1, depth, one);
        var corner = one[0];
        bool top = edge == ScreenEdge.Top;
        Sample(image, side, top ? 0 : 0.5, top ? 0.5 : 1, depth, one);
        var outer = one[0];

        for (int i = 0; i < pixels.Length; i++)
        {
            double position = from + ((to - from) * (i + 0.5) / pixels.Length);

            // A monitor on the left meets the image at its right end, position 1.
            double away = left ? 1 - position : position;
            pixels[i] = Lerp(corner, outer, away);
        }
    }

    private static Rgb Lerp(Rgb a, Rgb b, double t) => new(
        (byte)Math.Round(a.R + ((b.R - a.R) * t)),
        (byte)Math.Round(a.G + ((b.G - a.G) * t)),
        (byte)Math.Round(a.B + ((b.B - a.B) * t)));

    private static Rgb Average(BgraImage image, int x0, int x1, int y0, int y1)
    {
        long r = 0, g = 0, b = 0;
        var data = image.Pixels;
        for (int y = y0; y < y1; y++)
        {
            int row = y * image.Stride;
            for (int x = x0; x < x1; x++)
            {
                int p = row + (x * 4);
                b += data[p];
                g += data[p + 1];
                r += data[p + 2];
            }
        }

        long count = (long)(x1 - x0) * (y1 - y0);
        return new Rgb(
            (byte)((r + (count / 2)) / count),
            (byte)((g + (count / 2)) / count),
            (byte)((b + (count / 2)) / count));
    }

    private static void ValidateFraction(double value, string name)
    {
        if (value is < 0 or > 1 || double.IsNaN(value))
        {
            throw new ArgumentOutOfRangeException(name, value, "Must be between 0 and 1.");
        }
    }
}
