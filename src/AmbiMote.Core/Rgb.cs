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
}
