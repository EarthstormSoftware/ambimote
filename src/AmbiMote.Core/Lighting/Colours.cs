using System.Globalization;

namespace AmbiMote.Core.Lighting;

/// <summary>Colours by name, colour temperature or hex, for lighting the sticks as ordinary lights.</summary>
public static class Colours
{
    private static readonly Dictionary<string, Rgb> s_named = new(StringComparer.OrdinalIgnoreCase)
    {
        ["red"] = new(255, 0, 0),
        ["orange"] = new(255, 96, 0),
        ["yellow"] = new(255, 200, 0),
        ["green"] = new(0, 255, 0),
        ["cyan"] = new(0, 255, 255),
        ["blue"] = new(0, 0, 255),
        ["purple"] = new(128, 0, 255),
        ["magenta"] = new(255, 0, 255),
        ["pink"] = new(255, 64, 128),
        ["white"] = Rgb.White,
        ["candle"] = Rgb.FromKelvin(1900),
        ["warm"] = Rgb.FromKelvin(2700),
        ["neutral"] = Rgb.FromKelvin(4000),
        ["daylight"] = Rgb.FromKelvin(6500),
    };

    /// <summary>The colour names <see cref="TryParse"/> accepts.</summary>
    public static IEnumerable<string> Names => s_named.Keys;

    /// <summary>
    /// Parses a colour name such as <c>warm</c>, a colour temperature such as <c>3000k</c> (1000 to 40000),
    /// or a hex colour such as <c>ff8000</c>.
    /// </summary>
    public static bool TryParse(string? text, out Rgb colour)
    {
        colour = Rgb.Black;
        text = text?.Trim();
        if (string.IsNullOrEmpty(text))
        {
            return false;
        }

        if (s_named.TryGetValue(text, out colour))
        {
            return true;
        }

        if (text.EndsWith('k') || text.EndsWith('K'))
        {
            if (int.TryParse(text.AsSpan(0, text.Length - 1), NumberStyles.None, CultureInfo.InvariantCulture, out int kelvin)
                && kelvin is >= 1000 and <= 40000)
            {
                colour = Rgb.FromKelvin(kelvin);
                return true;
            }

            return false;
        }

        return Rgb.TryParse(text, out colour);
    }
}
