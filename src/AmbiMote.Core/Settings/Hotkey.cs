using System.Globalization;

namespace AmbiMote.Core.Settings;

/// <summary>Modifier keys, with the values Windows' <c>RegisterHotKey</c> uses.</summary>
[Flags]
public enum HotkeyModifiers
{
    None = 0,
    Alt = 1,
    Ctrl = 2,
    Shift = 4,
    Win = 8,
}

/// <summary>A keyboard shortcut such as <c>Ctrl+Alt+Shift+L</c>, as Windows' virtual-key code and modifiers.</summary>
public readonly record struct Hotkey
{
    private static readonly Dictionary<int, string> s_keyNames = BuildKeyNames();
    private static readonly Dictionary<string, int> s_keyCodes = s_keyNames.ToDictionary(k => k.Value, k => k.Key, StringComparer.OrdinalIgnoreCase);

    private Hotkey(HotkeyModifiers modifiers, int key)
    {
        Modifiers = modifiers;
        Key = key;
    }

    public HotkeyModifiers Modifiers { get; }

    /// <summary>The Windows virtual-key code.</summary>
    public int Key { get; }

    /// <summary>
    /// Makes a shortcut from keys pressed together. A shortcut needs Ctrl, Alt or Win, so it cannot take over typing,
    /// except F13 to F24, which keyboards only have for macros. Shift alone is not enough: Shift+A is a capital A.
    /// </summary>
    public static bool TryCreate(HotkeyModifiers modifiers, int key, out Hotkey hotkey)
    {
        hotkey = new Hotkey(modifiers, key);
        bool known = s_keyNames.ContainsKey(key) && (modifiers & ~(HotkeyModifiers.Alt | HotkeyModifiers.Ctrl | HotkeyModifiers.Shift | HotkeyModifiers.Win)) == 0;
        bool macroKey = key is >= 0x7C and <= 0x87;
        return known && (macroKey || (modifiers & (HotkeyModifiers.Ctrl | HotkeyModifiers.Alt | HotkeyModifiers.Win)) != 0);
    }

    /// <summary>Parses a shortcut such as <c>Ctrl+Alt+Shift+L</c> or <c>win + f9</c>.</summary>
    public static bool TryParse(string? text, out Hotkey hotkey)
    {
        hotkey = default;
        string[] parts = (text ?? "").Split('+', StringSplitOptions.TrimEntries);
        if (parts.Length == 0 || parts.Any(string.IsNullOrEmpty))
        {
            return false;
        }

        var modifiers = HotkeyModifiers.None;
        foreach (string part in parts[..^1])
        {
            var modifier = part.ToUpperInvariant() switch
            {
                "CTRL" or "CONTROL" => HotkeyModifiers.Ctrl,
                "ALT" => HotkeyModifiers.Alt,
                "SHIFT" => HotkeyModifiers.Shift,
                "WIN" or "WINDOWS" => HotkeyModifiers.Win,
                _ => HotkeyModifiers.None,
            };
            if (modifier == HotkeyModifiers.None || modifiers.HasFlag(modifier))
            {
                return false;
            }

            modifiers |= modifier;
        }

        return s_keyCodes.TryGetValue(parts[^1], out int key) && TryCreate(modifiers, key, out hotkey);
    }

    /// <summary>The name <see cref="TryParse"/> reads, with the modifiers in Windows' order: Ctrl, Alt, Shift, Win.</summary>
    public override string ToString()
    {
        var parts = new List<string>(5);
        foreach (var modifier in new[] { HotkeyModifiers.Ctrl, HotkeyModifiers.Alt, HotkeyModifiers.Shift, HotkeyModifiers.Win })
        {
            if (Modifiers.HasFlag(modifier))
            {
                parts.Add(modifier.ToString());
            }
        }

        parts.Add(s_keyNames.TryGetValue(Key, out string? name) ? name : $"Key{Key.ToString(CultureInfo.InvariantCulture)}");
        return string.Join('+', parts);
    }

    private static Dictionary<int, string> BuildKeyNames()
    {
        var names = new Dictionary<int, string>
        {
            [0x08] = "Backspace", [0x13] = "Pause", [0x20] = "Space",
            [0x21] = "PageUp", [0x22] = "PageDown", [0x23] = "End", [0x24] = "Home",
            [0x25] = "Left", [0x26] = "Up", [0x27] = "Right", [0x28] = "Down",
            [0x2D] = "Insert", [0x2E] = "Delete", [0x91] = "ScrollLock",
            [0x6A] = "NumMultiply", [0x6B] = "NumPlus", [0x6D] = "NumMinus", [0x6E] = "NumDecimal", [0x6F] = "NumDivide",
            [0xBA] = "Semicolon", [0xBB] = "Plus", [0xBC] = "Comma", [0xBD] = "Minus", [0xBE] = "Period",
            [0xBF] = "Slash", [0xC0] = "Backquote", [0xDB] = "OpenBracket", [0xDC] = "Backslash", [0xDD] = "CloseBracket",
            [0xDE] = "Quote",
        };
        for (char c = 'A'; c <= 'Z'; c++)
        {
            names[c] = c.ToString();
        }

        for (int digit = 0; digit <= 9; digit++)
        {
            names[0x30 + digit] = digit.ToString(CultureInfo.InvariantCulture);
            names[0x60 + digit] = $"Num{digit}";
        }

        for (int f = 1; f <= 24; f++)
        {
            names[0x6F + f] = $"F{f}";
        }

        return names;
    }
}
