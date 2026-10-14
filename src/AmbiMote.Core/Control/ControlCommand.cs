using System.Globalization;

using AmbiMote.Core.Engine;
using AmbiMote.Core.Lighting;
using AmbiMote.Core.Settings;

namespace AmbiMote.Core.Control;

/// <summary>What a <see cref="ControlCommand"/> does.</summary>
public enum ControlVerb
{
    /// <summary>Change nothing; just report the state.</summary>
    Status,

    /// <summary>Turn the lights on in the last mode.</summary>
    On,

    Off,

    /// <summary>Turn the lights off if they are on, and back on if they are off.</summary>
    Toggle,

    /// <summary>The next mode: Light, Colour, Effect, Ambient and round again. Turns the lights on if they are off.</summary>
    Next,

    /// <summary>Light mode, optionally at a colour temperature.</summary>
    Light,

    /// <summary>Colour mode, optionally in a new colour.</summary>
    Colour,

    /// <summary>Effect mode, optionally with a different effect.</summary>
    Effect,

    Ambient,

    /// <summary>Set the brightness, or change it by a step.</summary>
    Brightness,

    /// <summary>Light each stick in turn. See <see cref="LightingEngine.Identify"/>.</summary>
    Identify,

    /// <summary>Reload the config and reopen the Mote hosts.</summary>
    Reconnect,

    /// <summary>Show the app's window.</summary>
    Show,
}

/// <summary>
/// One request to the running app, from the <c>ambimote app</c> command, a script or a hotkey. Sent through
/// <see cref="ControlPipe"/> as one line of words, such as <c>light 2700</c> or <c>brightness +10</c>.
/// </summary>
public sealed record ControlCommand
{
    /// <summary>The words <see cref="TryParse"/> understands, for help text and errors.</summary>
    public const string Usage =
        "status, on, off, toggle, next, light [kelvin], colour [colour], effect [rainbow|breathe|candle|chase], ambient, "
        + "brightness <percent|+step|-step>, identify, reconnect, show";

    /// <summary>The lowest brightness, in percent, that stepping down reaches.</summary>
    public const int BrightnessStepFloor = 5;

    private static readonly Dictionary<string, int> s_whites = new(StringComparer.OrdinalIgnoreCase)
    {
        ["candle"] = 1900,
        ["warm"] = 2700,
        ["neutral"] = 4000,
        ["daylight"] = 6500,
    };

    private static readonly LightingMode[] s_cycle = [LightingMode.Light, LightingMode.Colour, LightingMode.Effect, LightingMode.Ambient];

    private ControlCommand(ControlVerb verb) => Verb = verb;

    public ControlVerb Verb { get; private init; }

    /// <summary>For <see cref="ControlVerb.Light"/>, the colour temperature, if one was given.</summary>
    public int? Kelvin { get; private init; }

    /// <summary>For <see cref="ControlVerb.Colour"/>, the colour, if one was given.</summary>
    public Rgb? Colour { get; private init; }

    /// <summary>For <see cref="ControlVerb.Effect"/>, the effect, if one was given.</summary>
    public EffectKind? Effect { get; private init; }

    /// <summary>For <see cref="ControlVerb.Brightness"/>, the brightness in percent, or the step if <see cref="IsStep"/>.</summary>
    public int Percent { get; private init; }

    /// <summary>Whether <see cref="Percent"/> is added to the brightness rather than replacing it.</summary>
    public bool IsStep { get; private init; }

    /// <summary>Whether running the command changes the settings, as opposed to just reporting or acting on the engine.</summary>
    public bool ChangesSettings => Verb is not (ControlVerb.Status or ControlVerb.Identify or ControlVerb.Reconnect or ControlVerb.Show);

    public static ControlCommand Create(ControlVerb verb) => new(verb);

    /// <summary>Changes the brightness by <paramref name="step"/> percent, up or down.</summary>
    public static ControlCommand BrightnessStep(int step) => new(ControlVerb.Brightness) { Percent = step, IsStep = true };

    /// <summary>Parses a line such as <c>light 2700</c>. Words are case-insensitive.</summary>
    public static bool TryParse(string? line, out ControlCommand command, out string error)
    {
        command = new ControlCommand(ControlVerb.Status);
        error = "";
        string[] words = (line ?? "").Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        if (words.Length == 0 || !TryParseName(words[0], out ControlVerb verb))
        {
            error = words.Length == 0 ? $"No command. Use one of: {Usage}." : $"Unknown command '{words[0]}'. Use one of: {Usage}.";
            return false;
        }

        string? argument = words.Length > 1 ? string.Join(' ', words.Skip(1)) : null;
        bool takesArgument = verb is ControlVerb.Light or ControlVerb.Colour or ControlVerb.Effect or ControlVerb.Brightness;
        if (argument is not null && !takesArgument)
        {
            error = $"'{words[0]}' takes nothing after it.";
            return false;
        }

        command = new ControlCommand(verb);
        switch (verb)
        {
            case ControlVerb.Light when argument is not null:
                if (!TryParseKelvin(argument, out int kelvin))
                {
                    error = $"Unknown colour temperature '{argument}'. Use {LightingSettings.MinKelvin} to {LightingSettings.MaxKelvin}, "
                        + "such as 2700 or 2700k, or candle, warm, neutral or daylight.";
                    return false;
                }

                command = command with { Kelvin = kelvin };
                break;

            case ControlVerb.Colour when argument is not null:
                if (!Colours.TryParse(argument, out var colour))
                {
                    error = $"Unknown colour '{argument}'. Use a name such as warm or red, a temperature such as 3000k, or hex such as ff8000.";
                    return false;
                }

                command = command with { Colour = colour };
                break;

            case ControlVerb.Effect when argument is not null:
                if (!TryParseName(argument, out EffectKind effect))
                {
                    error = $"Unknown effect '{argument}'. Use rainbow, breathe, candle or chase.";
                    return false;
                }

                command = command with { Effect = effect };
                break;

            case ControlVerb.Brightness:
                if (argument is null || !TryParsePercent(argument, out int percent, out bool isStep))
                {
                    error = "Give the brightness in percent, such as 60, or a step up or down, such as +10 or -10.";
                    return false;
                }

                command = command with { Percent = percent, IsStep = isStep };
                break;
        }

        return true;
    }

    /// <summary>The settings after the command. Commands that only report or act on the engine leave them as they are.</summary>
    public AppSettings ApplyTo(AppSettings settings)
    {
        ArgumentNullException.ThrowIfNull(settings);
        var lighting = settings.Lighting;
        var next = Verb switch
        {
            ControlVerb.On => settings.WithLightsOn(true),
            ControlVerb.Off => settings.WithLightsOn(false),
            ControlVerb.Toggle => settings.WithLightsOn(lighting.Mode == LightingMode.Off),
            ControlVerb.Next => WithMode(settings, lighting.Mode == LightingMode.Off
                ? settings.LastMode
                : s_cycle[(Array.IndexOf(s_cycle, lighting.Mode) + 1) % s_cycle.Length]),
            ControlVerb.Light => WithMode(settings, LightingMode.Light, l => l with { Kelvin = Kelvin ?? l.Kelvin }),
            ControlVerb.Colour => WithMode(settings, LightingMode.Colour, l => l with { Colour = Colour ?? l.Colour }),
            ControlVerb.Effect => WithMode(settings, LightingMode.Effect, l => l with { Effect = Effect ?? l.Effect }),
            ControlVerb.Ambient => WithMode(settings, LightingMode.Ambient),
            ControlVerb.Brightness => settings with { Lighting = lighting with { Brightness = NewBrightness(lighting.Brightness) } },
            _ => settings,
        };
        return next.Normalised();
    }

    /// <summary>The command as <see cref="TryParse"/> reads it.</summary>
    public override string ToString()
    {
        string verb = Verb.ToString().ToLowerInvariant();
        return Verb switch
        {
            ControlVerb.Light when Kelvin is { } kelvin => $"{verb} {kelvin.ToString(CultureInfo.InvariantCulture)}",
            ControlVerb.Colour when Colour is { } colour => $"{verb} {colour}",
            ControlVerb.Effect when Effect is { } effect => $"{verb} {effect.ToString().ToLowerInvariant()}",
            ControlVerb.Brightness => $"{verb} {(IsStep ? Percent.ToString("+0;-0", CultureInfo.InvariantCulture) : Percent.ToString(CultureInfo.InvariantCulture))}",
            _ => verb,
        };
    }

    private static AppSettings WithMode(AppSettings settings, LightingMode mode, Func<LightingSettings, LightingSettings>? change = null) =>
        settings with { Lighting = (change?.Invoke(settings.Lighting) ?? settings.Lighting) with { Mode = mode } };

    /// <summary>Matches a word to an enum value by name only, so numbers and lists such as <c>on,off</c> are not accepted.</summary>
    private static bool TryParseName<T>(string word, out T value)
        where T : struct, Enum
    {
        string? name = Enum.GetNames<T>().FirstOrDefault(n => n.Equals(word, StringComparison.OrdinalIgnoreCase));
        value = name is null ? default : Enum.Parse<T>(name);
        return name is not null;
    }

    /// <summary>
    /// The brightness after this command. Steps stop at <see cref="BrightnessStepFloor"/> on the way down, so stepping
    /// down never leaves the lights on but dark, and they snap to whole steps so +10 from 37% gives 40%.
    /// </summary>
    private double NewBrightness(double current)
    {
        if (!IsStep)
        {
            return Percent / 100.0;
        }

        int now = (int)Math.Round(current * 100);
        int step = Math.Abs(Percent);
        int next = Percent > 0
            ? (now / step * step) + step
            : ((now + step - 1) / step * step) - step;
        return Math.Clamp(next, Math.Min(now, BrightnessStepFloor), 100) / 100.0;
    }

    private static bool TryParseKelvin(string text, out int kelvin)
    {
        if (s_whites.TryGetValue(text, out kelvin))
        {
            return true;
        }

        var digits = text.AsSpan();
        if (digits.EndsWith("k", StringComparison.OrdinalIgnoreCase))
        {
            digits = digits[..^1];
        }

        return int.TryParse(digits, NumberStyles.None, CultureInfo.InvariantCulture, out kelvin)
            && kelvin is >= LightingSettings.MinKelvin and <= LightingSettings.MaxKelvin;
    }

    private static bool TryParsePercent(string text, out int percent, out bool isStep)
    {
        isStep = text.StartsWith('+') || text.StartsWith('-');
        var digits = text.AsSpan(isStep ? 1 : 0).TrimEnd('%');
        if (!int.TryParse(digits, NumberStyles.None, CultureInfo.InvariantCulture, out percent)
            || percent > 100 || (isStep && percent == 0))
        {
            return false;
        }

        if (text.StartsWith('-'))
        {
            percent = -percent;
        }

        return true;
    }
}
