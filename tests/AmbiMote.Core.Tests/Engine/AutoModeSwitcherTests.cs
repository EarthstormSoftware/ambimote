using AmbiMote.Core.Engine;
using AmbiMote.Core.Settings;

namespace AmbiMote.Core.Tests.Engine;

public sealed class AutoModeSwitcherTests
{
    private static readonly ForegroundWindow s_browser = new("browser.exe", FullScreen: false);
    private static readonly ForegroundWindow s_video = new("browser.exe", FullScreen: true);
    private static readonly ForegroundWindow s_game = new("game.exe", FullScreen: true);
    private static readonly ForegroundWindow s_editor = new("editor.exe", FullScreen: false);

    private static readonly AutoModeSettings s_settings = new AutoModeSettings
    {
        FullScreen = LightingMode.Ambient,
        Apps = [new AppModeRule("game.exe", LightingMode.Effect), new AppModeRule("editor.exe", LightingMode.Off)],
    }.Normalised();

    private readonly AutoModeSwitcher _switcher = new();
    private LightingMode _mode = LightingMode.Light;
    private double _now;

    /// <summary>Shows the window for long enough to settle, applying whatever the switcher decides.</summary>
    private LightingMode Show(ForegroundWindow? window, AutoModeSettings? settings = null)
    {
        foreach (double step in new[] { 0, AutoModeSwitcher.SettleSeconds })
        {
            _now += step;
            if (_switcher.Update(_now, window, _mode, settings ?? s_settings) is { } mode)
            {
                _mode = mode;
            }
        }

        return _mode;
    }

    [Fact]
    public void FullScreen_SwitchesAndGoesBackAfterwards()
    {
        Assert.Equal(LightingMode.Ambient, Show(s_video));
        Assert.Equal("a full-screen window", _switcher.Reason);
        Assert.Equal(LightingMode.Light, Show(s_browser));
        Assert.Null(_switcher.Reason);
    }

    [Fact]
    public void Switch_WaitsForTheWindowToSettle()
    {
        Assert.Null(_switcher.Update(0, s_video, _mode, s_settings));
        Assert.Null(_switcher.Update(1, s_video, _mode, s_settings));
        Assert.Equal(LightingMode.Ambient, _switcher.Update(AutoModeSwitcher.SettleSeconds, s_video, _mode, s_settings));
    }

    [Fact]
    public void QuickAltTab_ChangesNothing()
    {
        Show(s_video);

        Assert.Null(_switcher.Update(_now + 0.5, s_browser, _mode, s_settings));
        Assert.Null(_switcher.Update(_now + 1, s_video, _mode, s_settings));
        Assert.Null(_switcher.Update(_now + 5, s_video, _mode, s_settings));
        Assert.Equal(LightingMode.Ambient, _mode);
    }

    [Fact]
    public void AppRules_WinOverFullScreen()
    {
        Assert.Equal(LightingMode.Effect, Show(s_game));
        Assert.Equal("game.exe", _switcher.Reason);
        Assert.Equal(LightingMode.Off, Show(s_editor));
        Assert.Equal(LightingMode.Light, Show(s_browser));
    }

    [Fact]
    public void AppRules_IgnoreCase()
    {
        Assert.Equal(LightingMode.Effect, Show(new ForegroundWindow("GAME.EXE", FullScreen: false)));
    }

    [Fact]
    public void UnknownWindow_IsIgnored()
    {
        Show(s_video);

        // Such as AmbiMote's own window, opened to change something while a video plays.
        Assert.Equal(LightingMode.Ambient, Show(null));
        Assert.Equal(LightingMode.Light, Show(s_browser));
    }

    [Fact]
    public void TheUsersChoice_WinsAndIsKept()
    {
        Show(s_video);
        _mode = LightingMode.Colour;

        Assert.Equal(LightingMode.Colour, Show(s_video));
        Assert.Equal(LightingMode.Colour, Show(s_browser));
    }

    [Fact]
    public void TheUsersChoice_DuringOneRule_IsWhatTheNextGoesBackTo()
    {
        Show(s_video);
        _mode = LightingMode.Colour;

        Assert.Equal(LightingMode.Effect, Show(s_game));
        Assert.Equal(LightingMode.Colour, Show(s_browser));
    }

    [Fact]
    public void LightsTheUserTurnedOff_StayOff()
    {
        _mode = LightingMode.Off;

        Assert.Equal(LightingMode.Off, Show(s_video));
        Assert.Null(_switcher.Reason);
        Assert.Equal(LightingMode.Off, Show(s_browser));
    }

    [Fact]
    public void LightsTurnedOffDuringARule_StayOffForTheNextRule()
    {
        Show(s_video);
        _mode = LightingMode.Off;

        Assert.Equal(LightingMode.Off, Show(s_game));
        Assert.Equal(LightingMode.Off, Show(s_browser));
    }

    [Fact]
    public void LightsTurnedOnDuringARule_PickUpTheRule()
    {
        _mode = LightingMode.Off;
        Show(s_video);
        _mode = LightingMode.Light;

        Assert.Equal(LightingMode.Ambient, Show(s_video));
        Assert.Equal(LightingMode.Light, Show(s_browser));
    }

    [Fact]
    public void ARuleThatTurnsTheLightsOff_TurnsThemBackOn()
    {
        Assert.Equal(LightingMode.Off, Show(s_editor));
        Assert.Equal(LightingMode.Ambient, Show(s_video));
        Assert.Equal(LightingMode.Light, Show(s_browser));
    }

    [Fact]
    public void FullScreenNotSet_LeavesTheModeAlone()
    {
        Assert.Equal(LightingMode.Light, Show(s_video, s_settings with { FullScreen = null }));
    }

    [Fact]
    public void Release_GoesBackUnlessTheUserChangedIt()
    {
        Show(s_video);
        Assert.Equal(LightingMode.Light, _switcher.Release(_mode));
        Assert.Null(_switcher.Reason);

        Show(s_browser);
        Show(s_video);
        Assert.Null(_switcher.Release(LightingMode.Colour));
        Assert.Null(_switcher.Release(LightingMode.Ambient));
    }

    [Fact]
    public void Settings_TidyRulesAndCompareByContent()
    {
        var settings = new AutoModeSettings
        {
            Apps = [new AppModeRule(@"C:\Games\Game", LightingMode.Effect), new AppModeRule("game.exe", LightingMode.Light), new AppModeRule(" ", LightingMode.Light)],
        }.Normalised();

        Assert.Equal([new AppModeRule("Game.exe", LightingMode.Effect)], settings.Apps);
        Assert.Equal(settings, settings.Normalised());
        Assert.True(settings.IsActive);
        Assert.False(new AutoModeSettings { FullScreen = null }.IsActive);
    }

    [Fact]
    public void WithApp_ChangesARuleInPlaceOrAddsOne()
    {
        var settings = s_settings.WithApp("GAME.exe", LightingMode.Light).WithApp("player", LightingMode.Ambient);

        Assert.Equal(
            [new AppModeRule("game.exe", LightingMode.Light), new AppModeRule("editor.exe", LightingMode.Off), new AppModeRule("player.exe", LightingMode.Ambient)],
            settings.Apps);
        Assert.Equal([new AppModeRule("game.exe", LightingMode.Light), new AppModeRule("player.exe", LightingMode.Ambient)], settings.WithoutApp("editor.exe").Apps);
    }

    [Fact]
    public void AppSettings_SavesRulesAndDontChange()
    {
        var settings = new AppSettings { AutoMode = s_settings with { FullScreen = null } }.Normalised();

        var loaded = AppSettings.Parse(settings.ToJson());

        Assert.Equal(settings, loaded);
        Assert.Null(loaded.AutoMode.FullScreen);
        Assert.Equal(LightingMode.Ambient, AppSettings.Parse("{}").AutoMode.FullScreen);
        Assert.Empty(AppSettings.Parse("""{ "autoMode": { "apps": null } }""").AutoMode.Apps);
    }
}
