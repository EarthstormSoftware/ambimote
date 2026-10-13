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
