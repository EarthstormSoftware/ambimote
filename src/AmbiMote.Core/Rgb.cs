using System.Globalization;

namespace AmbiMote.Core;

/// <summary>An 8-bit RGB colour.</summary>
public readonly record struct Rgb(byte R, byte G, byte B)
{
    public static readonly Rgb Black = new(0, 0, 0);

    public static readonly Rgb White = new(255, 255, 255);

    /// <summary>Multiplies each component by <paramref name="factor"/>, clamped to 0..1.</summary>
    public Rgb Scale(double factor)
    {
        factor = Math.Clamp(factor, 0.0, 1.0);
        return new Rgb(ScaleComponent(R, factor), ScaleComponent(G, factor), ScaleComponent(B, factor));
    }

    /// <summary>A colour from hue (0 to 1, wrapping), saturation and value (0 to 1).</summary>
    public static Rgb FromHsv(double hue, double saturation = 1.0, double value = 1.0)
    {
        hue = (hue - Math.Floor(hue)) * 6;
        saturation = Math.Clamp(saturation, 0.0, 1.0);
        value = Math.Clamp(value, 0.0, 1.0);
        int sector = (int)hue % 6;
        double f = hue - Math.Floor(hue);
        double p = value * (1 - saturation);
        double q = value * (1 - (saturation * f));
        double t = value * (1 - (saturation * (1 - f)));
        var (r, g, b) = sector switch
        {
            0 => (value, t, p),
            1 => (q, value, p),
            2 => (p, value, t),
            3 => (p, q, value),
            4 => (t, p, value),
            _ => (value, p, q),
        };
        return new Rgb(ToByte(r), ToByte(g), ToByte(b));
    }

    /// <summary>
    /// The colour of white light at a colour temperature, such as 2700 K for a warm bulb or 6500 K for daylight.
    /// Uses Tanner Helland's fit of the black-body curve, which is close enough for lighting.
    /// </summary>
    public static Rgb FromKelvin(double kelvin)
    {
        double t = Math.Clamp(kelvin, 1000, 40000) / 100;
        double r = t <= 66 ? 255 : 329.698727446 * Math.Pow(t - 60, -0.1332047592);
        double g = t <= 66 ? (99.4708025861 * Math.Log(t)) - 161.1195681661 : 288.1221695283 * Math.Pow(t - 60, -0.0755148492);
        double b = t >= 66 ? 255 : t <= 19 ? 0 : (138.5177312231 * Math.Log(t - 10)) - 305.0447927307;
        return new Rgb(ClampByte(r), ClampByte(g), ClampByte(b));
    }

    /// <summary>Parses a hex colour such as <c>ff8000</c> or <c>#FF8000</c>.</summary>
    public static bool TryParse(string? text, out Rgb colour)
    {
        colour = Black;
        if (text is null)
        {
            return false;
        }

        var hex = text.AsSpan().Trim();
        if (hex.StartsWith("#"))
        {
            hex = hex[1..];
        }

        if (hex.Length != 6
            || !uint.TryParse(hex, NumberStyles.AllowHexSpecifier, CultureInfo.InvariantCulture, out uint value))
        {
            return false;
        }

        colour = new Rgb((byte)(value >> 16), (byte)(value >> 8), (byte)value);
        return true;
    }

    public override string ToString() => $"#{R:x2}{G:x2}{B:x2}";

    private static byte ScaleComponent(byte value, double factor) => (byte)Math.Round(value * factor);

    private static byte ToByte(double fraction) => (byte)Math.Round(fraction * 255);

    private static byte ClampByte(double value) => (byte)Math.Round(Math.Clamp(value, 0, 255));
}
