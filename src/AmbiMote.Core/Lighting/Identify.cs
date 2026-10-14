namespace AmbiMote.Core.Lighting;

/// <summary>
/// How a stick is lit to show which host and channel it is on: pixel 0 white, and the rest the channel's colour, fading
/// out along the stick so its direction shows too.
/// </summary>
public static class Identify
{
    private static readonly (Rgb Colour, string Name)[] s_channels =
    [
        (new(255, 0, 0), "red"),
        (new(0, 255, 0), "green"),
        (new(0, 0, 255), "blue"),
        (new(255, 160, 0), "orange"),
    ];

    /// <summary>The colour for <paramref name="channel"/>, from 1.</summary>
    public static Rgb ChannelColour(int channel) => s_channels[(channel - 1) % s_channels.Length].Colour;

    /// <summary>The name of <see cref="ChannelColour"/>, such as "red".</summary>
    public static string ChannelColourName(int channel) => s_channels[(channel - 1) % s_channels.Length].Name;

    /// <summary>Fills <paramref name="pixels"/> with the identify pattern for <paramref name="channel"/>.</summary>
    public static void Light(Span<Rgb> pixels, int channel)
    {
        if (pixels.IsEmpty)
        {
            return;
        }

        var colour = ChannelColour(channel);
        pixels[0] = Rgb.White;
        for (int i = 1; i < pixels.Length; i++)
        {
            pixels[i] = colour.Scale(1.0 - (0.85 * (i - 1) / Math.Max(1, pixels.Length - 2)));
        }
    }
}
