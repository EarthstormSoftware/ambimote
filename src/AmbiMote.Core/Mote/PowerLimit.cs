namespace AmbiMote.Core.Mote;

/// <summary>
/// Keeps a Mote host within what its USB port can supply. A host with four sticks at full white
/// would draw several amps, far more than a USB port provides.
/// </summary>
/// <remarks>
/// The current is an estimate: an APA102 draws roughly 20 mA per colour at full brightness,
/// so about 60 mA for full white. Pimoroni does not publish a figure for the Mote host.
/// </remarks>
public static class PowerLimit
{
    public const double MilliampsPerComponent = 20.0;

    /// <summary>Estimated current in milliamps for the given pixels.</summary>
    public static double EstimateMilliamps(IEnumerable<Rgb[]?> channels)
    {
        ArgumentNullException.ThrowIfNull(channels);
        long total = 0;
        foreach (var pixels in channels)
        {
            if (pixels is null)
            {
                continue;
            }

            foreach (var pixel in pixels)
            {
                total += pixel.R + pixel.G + pixel.B;
            }
        }

        return total / 255.0 * MilliampsPerComponent;
    }

    /// <summary>
    /// Scales every pixel by <paramref name="brightness"/>, then further if the result would draw
    /// more than <paramref name="maxMilliamps"/>. Returns new arrays; the input is not changed.
    /// </summary>
    public static Rgb[]?[] Apply(IReadOnlyList<Rgb[]?> channels, double brightness, double? maxMilliamps)
    {
        ArgumentNullException.ThrowIfNull(channels);
        brightness = Math.Clamp(brightness, 0.0, 1.0);

        var scaled = channels.Select(c => c?.Select(p => p.Scale(brightness)).ToArray()).ToArray();
        if (maxMilliamps is not { } budget)
        {
            return scaled;
        }

        double estimate = EstimateMilliamps(scaled);
        if (estimate <= budget)
        {
            return scaled;
        }

        double factor = Math.Max(budget, 0.0) / estimate;
        return scaled.Select(c => c?.Select(p => ScaleDown(p, factor)).ToArray()).ToArray();
    }

    // Rounds down, so the result never goes over the budget.
    private static Rgb ScaleDown(Rgb pixel, double factor) =>
        new((byte)(pixel.R * factor), (byte)(pixel.G * factor), (byte)(pixel.B * factor));
}
