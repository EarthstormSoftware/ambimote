using AmbiMote.Core.Control;
using AmbiMote.Core.Engine;
using AmbiMote.Core.Settings;

namespace AmbiMote.Core.Tests.Settings;

public sealed class HotkeyTests
{
    [Theory]
    [InlineData("Ctrl+Alt+Shift+L", "Ctrl+Alt+Shift+L")]
    [InlineData("shift + alt + control + l", "Ctrl+Alt+Shift+L")]
    [InlineData("Win+F9", "Win+F9")]
    [InlineData("Ctrl+Alt+Up", "Ctrl+Alt+Up")]
    [InlineData("Alt+Plus", "Alt+Plus")]
    [InlineData("Ctrl+Num5", "Ctrl+Num5")]
    [InlineData("Ctrl+1", "Ctrl+1")]
    [InlineData("F13", "F13")]
    [InlineData("Shift+F24", "Shift+F24")]
    public void TryParse_ReadsShortcutsAndWritesThemTheStandardWay(string text, string expected)
    {
        Assert.True(Hotkey.TryParse(text, out var hotkey));
        Assert.Equal(expected, hotkey.ToString());
        Assert.True(Hotkey.TryParse(hotkey.ToString(), out var again));
        Assert.Equal(hotkey, again);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("L")]
    [InlineData("Shift+L")]
    [InlineData("F5")]
    [InlineData("Ctrl+")]
    [InlineData("Ctrl+Ctrl+L")]
    [InlineData("Hyper+L")]
    [InlineData("Ctrl+Banana")]
    [InlineData("Ctrl+L+Alt")]
    public void TryParse_RejectsShortcutsThatWouldTakeOverTypingOrMakeNoSense(string? text)
    {
        Assert.False(Hotkey.TryParse(text, out _));
    }

    [Fact]
    public void TryCreate_UsesWindowsKeyCodes()
    {
        Assert.True(Hotkey.TryCreate(HotkeyModifiers.Ctrl | HotkeyModifiers.Alt, 0x26, out var up));
        Assert.Equal("Ctrl+Alt+Up", up.ToString());
        Assert.Equal(0x26, up.Key);
        Assert.False(Hotkey.TryCreate(HotkeyModifiers.Ctrl, 0xFF, out _));
    }

    [Fact]
    public void Defaults_AreAllSetAndDifferent()
    {
        var bindings = new HotkeySettings().Bindings().ToList();

        Assert.Equal(Enum.GetValues<HotkeyAction>().Length, bindings.Count);
        Assert.Equal(bindings.Count, bindings.Select(b => b.Hotkey).Distinct().Count());
    }

    [Fact]
    public void Bindings_WhenTurnedOff_AreEmpty()
    {
        Assert.Empty((new HotkeySettings() with { Enabled = false }).Bindings());
    }

    [Fact]
    public void Normalised_TidiesShortcutsAndDropsBadOnes()
    {
        var settings = new HotkeySettings { Toggle = "alt+ctrl+t", NextMode = "nonsense", BrightnessUp = null }.Normalised();

        Assert.Equal("Ctrl+Alt+T", settings.Toggle);
        Assert.Null(settings.NextMode);
        Assert.Null(settings.BrightnessUp);
        Assert.Equal([HotkeyAction.Toggle, HotkeyAction.BrightnessDown], settings.Bindings().Select(b => b.Action));
    }

    [Fact]
    public void With_SetsAndClearsOneShortcut()
    {
        Hotkey.TryParse("Win+F9", out var hotkey);

        var set = new HotkeySettings().With(HotkeyAction.NextMode, hotkey);
        var cleared = set.With(HotkeyAction.NextMode, null);

        Assert.Equal("Win+F9", set.Get(HotkeyAction.NextMode));
        Assert.Null(cleared.Get(HotkeyAction.NextMode));
        Assert.Equal(new HotkeySettings().Toggle, cleared.Toggle);
    }

    [Fact]
    public void CommandFor_DoesWhatTheActionSays()
    {
        var settings = new AppSettings { Lighting = new LightingSettings { Mode = LightingMode.Light, Brightness = 0.4 } }.Normalised();

        Assert.Equal(LightingMode.Off, HotkeySettings.CommandFor(HotkeyAction.Toggle).ApplyTo(settings).Lighting.Mode);
        Assert.Equal(LightingMode.Colour, HotkeySettings.CommandFor(HotkeyAction.NextMode).ApplyTo(settings).Lighting.Mode);
        Assert.Equal(0.5, HotkeySettings.CommandFor(HotkeyAction.BrightnessUp).ApplyTo(settings).Lighting.Brightness, 3);
        Assert.Equal(0.3, HotkeySettings.CommandFor(HotkeyAction.BrightnessDown).ApplyTo(settings).Lighting.Brightness, 3);
        Assert.Equal(ControlVerb.Brightness, HotkeySettings.CommandFor(HotkeyAction.BrightnessDown).Verb);
    }

    [Fact]
    public void AppSettings_KeepsShortcutsAndCopesWithNull()
    {
        var settings = new AppSettings { Hotkeys = new HotkeySettings { Enabled = false, Toggle = "Win+F9", NextMode = null } };

        var loaded = AppSettings.Parse(settings.ToJson());
        var fromNull = AppSettings.Parse("""{ "hotkeys": null }""");

        Assert.Equal(settings.Normalised(), loaded);
        Assert.Null(loaded.Hotkeys.NextMode);
        Assert.Equal(new HotkeySettings(), fromNull.Hotkeys);
    }
}
