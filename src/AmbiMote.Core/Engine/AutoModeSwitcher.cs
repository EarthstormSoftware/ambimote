using AmbiMote.Core.Settings;

namespace AmbiMote.Core.Engine;

/// <summary>The window in front: which program it belongs to, and whether it fills its screen.</summary>
/// <param name="App">The program's file name, such as <c>game.exe</c>, or null if Windows would not say.</param>
public readonly record struct ForegroundWindow(string? App, bool FullScreen);

/// <summary>
/// Decides when <see cref="AutoModeSettings"/> should change the mode, given the window in front every so often.
/// A rule only takes over once the same window has stayed in front for <see cref="SettleSeconds"/>, so a quick
/// Alt+Tab does not flash the lights. When the window goes, the mode goes back to what it was, unless the user chose
/// another mode meanwhile, which always wins. Rules never turn the lights on when the user turned them off.
/// </summary>
public sealed class AutoModeSwitcher
{
    public const double SettleSeconds = 2;

    private string? _pendingKey;
    private double _pendingSince;
    private string? _activeKey;
    private LightingMode _restoreTo;
    private LightingMode _applied;

    /// <summary>Why the mode was changed, such as <c>game.exe</c>, while a rule is in charge; otherwise null.</summary>
    public string? Reason { get; private set; }

    /// <summary>
    /// Looks at the window in front and returns the mode to switch to, or null to leave the mode alone.
    /// </summary>
    /// <param name="now">Seconds on any steady clock.</param>
    /// <param name="window">The window in front, or null to ignore this look, such as while AmbiMote's own window is in front.</param>
    /// <param name="current">The mode now, which may be one the user just chose.</param>
    public LightingMode? Update(double now, ForegroundWindow? window, LightingMode current, AutoModeSettings settings)
    {
        ArgumentNullException.ThrowIfNull(settings);
        if (window is not { } foreground)
        {
            return null;
        }

        var (key, mode, reason) = Match(foreground, settings);
        if (key != _pendingKey)
        {
            _pendingKey = key;
            _pendingSince = now;
        }

        if (now - _pendingSince < SettleSeconds || key == _activeKey)
        {
            return null;
        }

        if (key is null)
        {
            return Release(current);
        }

        bool userChose = _activeKey is null || current != _applied;
        if (userChose && current == LightingMode.Off)
        {
            // The user turned the lights off, so leave them off until they turn them on again.
            _activeKey = null;
            Reason = null;
            return null;
        }

        if (userChose)
        {
            _restoreTo = current;
        }

        _activeKey = key;
        _applied = mode;
        Reason = reason;
        return mode != current ? mode : null;
    }

    /// <summary>
    /// Lets go of the rule in charge, if any, and returns the mode to go back to, or null if the user has changed it
    /// since or nothing needs to change. For when AmbiMote exits, so the saved mode is the user's own.
    /// </summary>
    public LightingMode? Release(LightingMode current)
    {
        if (_activeKey is null)
        {
            return null;
        }

        _activeKey = null;
        Reason = null;
        return current == _applied && _restoreTo != current ? _restoreTo : null;
    }

    private static (string? Key, LightingMode Mode, string? Reason) Match(ForegroundWindow window, AutoModeSettings settings)
    {
        if (settings.RuleFor(window.App) is { } rule)
        {
            return ($"app:{rule.App.ToUpperInvariant()}", rule.Mode, rule.App);
        }

        if (window.FullScreen && settings.FullScreen is { } fullScreen)
        {
            return ("fullscreen", fullScreen, "a full-screen window");
        }

        return (null, default, null);
    }
}
