namespace AmbiMote.Core.Processing;

/// <summary>
/// Turns colours sampled from the screen into LED colours. Screen values are gamma-encoded but LED brightness is linear,
/// so without gamma correction dark greys glow a visible white; averaging an edge also pulls colours towards grey,
/// which the saturation boost undoes.
/// </summary>
public sealed class ColourProcessor
{
    /// <summary>The gamma of a typical screen (sRGB is close to 2.2).</summary>
    public const double DefaultGamma = 2.2;

    /// <summary>A gentle boost, like Ambilight's, to make up for averaging.</summary>
    public const double DefaultSaturation = 1.3;

    private readonly byte[] _gamma = new byte[256];

    /// <param name="gamma">1 sends screen values unchanged; higher makes darker colours dimmer.</param>
    /// <param name="saturation">1 leaves colours as they are, 0 makes them grey, more than 1 makes them stronger.</param>
    public ColourProcessor(double gamma = DefaultGamma, double saturation = DefaultSaturation)
    {
        if (gamma is < 0.5 or > 4 || double.IsNaN(gamma))
        {
            throw new ArgumentOutOfRangeException(nameof(gamma), gamma, "Gamma must be between 0.5 and 4.");
        }

        if (saturation is < 0 or > 3 || double.IsNaN(saturation))
        {
            throw new ArgumentOutOfRangeException(nameof(saturation), saturation, "Saturation must be between 0 and 3.");
        }

        Gamma = gamma;
        Saturation = saturation;
        for (int i = 0; i < 256; i++)
        {
            _gamma[i] = (byte)Math.Round(255 * Math.Pow(i / 255.0, gamma));
        }
    }

    /// <summary>Leaves colours unchanged.</summary>
    public static ColourProcessor None { get; } = new(1, 1);

    public double Gamma { get; }

    public double Saturation { get; }

    public Rgb Process(Rgb colour)
    {
        if (Saturation != 1)
        {
            // Move each component away from (or towards) the colour's grey level, in screen values.
            double grey = (0.2126 * colour.R) + (0.7152 * colour.G) + (0.0722 * colour.B);
            colour = new Rgb(Saturate(colour.R, grey), Saturate(colour.G, grey), Saturate(colour.B, grey));
        }

        return new Rgb(_gamma[colour.R], _gamma[colour.G], _gamma[colour.B]);
    }

    public void Process(Span<Rgb> colours)
    {
        for (int i = 0; i < colours.Length; i++)
        {
            colours[i] = Process(colours[i]);
        }
    }

    private byte Saturate(byte value, double grey) => (byte)Math.Round(Math.Clamp(grey + ((value - grey) * Saturation), 0, 255));
}
