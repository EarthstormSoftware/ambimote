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
    /// What to multiply every component by. <paramref name="brightness"/> is a fraction of the brightest the budget
    /// allows: 1 is as bright as the pixels can be within <paramref name="maxMilliamps"/>, 0.5 half that. So the whole
    /// brightness range does something, even when the budget only allows a fraction of full brightness.
    /// </summary>
    public static double Factor(IEnumerable<Rgb[]?> channels, double brightness, double? maxMilliamps)
    {
        brightness = Math.Clamp(brightness, 0.0, 1.0);
        if (maxMilliamps is not { } budget)
        {
            return brightness;
        }

        double estimate = EstimateMilliamps(channels);
        return estimate <= budget ? brightness : brightness * Math.Max(budget, 0.0) / estimate;
    }

    /// <summary>
    /// Scales every pixel by <see cref="Factor"/>, rounding down when the budget is what limits it, so the result
    /// never goes over. Returns new arrays; the input is not changed.
    /// </summary>
    public static Rgb[]?[] Apply(IReadOnlyList<Rgb[]?> channels, double brightness, double? maxMilliamps)
    {
        ArgumentNullException.ThrowIfNull(channels);
        double factor = Factor(channels, brightness, maxMilliamps);
        bool limited = factor < Math.Clamp(brightness, 0.0, 1.0);
        return [.. channels.Select(c => c?.Select(p => limited ? ScaleDown(p, factor) : p.Scale(factor)).ToArray())];
    }

    private static Rgb ScaleDown(Rgb pixel, double factor) =>
        new((byte)(pixel.R * factor), (byte)(pixel.G * factor), (byte)(pixel.B * factor));
}
