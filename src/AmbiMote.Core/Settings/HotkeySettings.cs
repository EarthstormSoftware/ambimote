using AmbiMote.Core.Control;

namespace AmbiMote.Core.Settings;

/// <summary>What a keyboard shortcut does.</summary>
public enum HotkeyAction
{
    /// <summary>Lights off, or back on in the last mode.</summary>
    Toggle,

    /// <summary>The next mode: Light, Colour, Effect, Ambient and round again.</summary>
    NextMode,

    BrightnessUp,

    BrightnessDown,
}

/// <summary>
/// Keyboard shortcuts that work anywhere in Windows, even in games. Each is written like <c>Ctrl+Alt+Shift+L</c>, or
/// null for none. The defaults use three modifiers, which games and other programs rarely do.
/// </summary>
public sealed record HotkeySettings
{
    /// <summary>How far one press of <see cref="HotkeyAction.BrightnessUp"/> or <see cref="HotkeyAction.BrightnessDown"/> goes, in percent.</summary>
    public const int BrightnessStep = 10;

    public bool Enabled { get; init; } = true;

    public string? Toggle { get; init; } = "Ctrl+Alt+Shift+L";

    public string? NextMode { get; init; } = "Ctrl+Alt+Shift+M";

    public string? BrightnessUp { get; init; } = "Ctrl+Alt+Shift+Up";

    public string? BrightnessDown { get; init; } = "Ctrl+Alt+Shift+Down";

    /// <summary>What each action does, as the command the control pipe would run.</summary>
    public static ControlCommand CommandFor(HotkeyAction action) => action switch
    {
        HotkeyAction.Toggle => ControlCommand.Create(ControlVerb.Toggle),
        HotkeyAction.NextMode => ControlCommand.Create(ControlVerb.Next),
        HotkeyAction.BrightnessUp => ControlCommand.BrightnessStep(BrightnessStep),
        HotkeyAction.BrightnessDown => ControlCommand.BrightnessStep(-BrightnessStep),
        _ => throw new ArgumentOutOfRangeException(nameof(action)),
    };

    /// <summary>Whether holding the keys down should repeat the action, as it does for brightness.</summary>
    public static bool Repeats(HotkeyAction action) => action is HotkeyAction.BrightnessUp or HotkeyAction.BrightnessDown;

    public string? Get(HotkeyAction action) => action switch
    {
        HotkeyAction.Toggle => Toggle,
        HotkeyAction.NextMode => NextMode,
        HotkeyAction.BrightnessUp => BrightnessUp,
        HotkeyAction.BrightnessDown => BrightnessDown,
        _ => throw new ArgumentOutOfRangeException(nameof(action)),
    };

    /// <summary>A copy with <paramref name="action"/> on <paramref name="hotkey"/>, or on none if it is null.</summary>
    public HotkeySettings With(HotkeyAction action, Hotkey? hotkey)
    {
        string? text = hotkey?.ToString();
        return action switch
        {
            HotkeyAction.Toggle => this with { Toggle = text },
            HotkeyAction.NextMode => this with { NextMode = text },
            HotkeyAction.BrightnessUp => this with { BrightnessUp = text },
            HotkeyAction.BrightnessDown => this with { BrightnessDown = text },
            _ => throw new ArgumentOutOfRangeException(nameof(action)),
        };
    }

    /// <summary>The shortcuts that are set, in action order. Empty while <see cref="Enabled"/> is false.</summary>
    public IEnumerable<(HotkeyAction Action, Hotkey Hotkey)> Bindings()
    {
        if (!Enabled)
        {
            yield break;
        }

        foreach (var action in Enum.GetValues<HotkeyAction>())
        {
            if (Hotkey.TryParse(Get(action), out var hotkey))
            {
                yield return (action, hotkey);
            }
        }
    }

    /// <summary>A copy with each shortcut written the standard way, and any that cannot be read set to none.</summary>
    public HotkeySettings Normalised()
    {
        var normalised = this;
        foreach (var action in Enum.GetValues<HotkeyAction>())
        {
            normalised = normalised.With(action, Hotkey.TryParse(Get(action), out var hotkey) ? hotkey : null);
        }

        return normalised;
    }
}
