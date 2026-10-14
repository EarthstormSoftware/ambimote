using System.Diagnostics;

using AmbiMote.Core.Engine;

using Microsoft.UI.Dispatching;

namespace AmbiMote.App;

/// <summary>
/// Looks at the window in front twice a second, while any automatic mode is set, and switches the mode as the
/// <see cref="AutoModeSwitcher"/> decides. That is a few cheap calls into Windows each time, so it costs nothing a game
/// would notice.
/// </summary>
internal sealed class AutoModeWatcher : IDisposable
{
    private readonly SettingsStore _store;
    private readonly ScreenStateWatcher _screens;
    private readonly DispatcherQueueTimer _timer;
    private readonly AutoModeSwitcher _switcher = new();
    private readonly Stopwatch _clock = Stopwatch.StartNew();
    private string? _reason;

    public AutoModeWatcher(SettingsStore store, ScreenStateWatcher screens, DispatcherQueue dispatcher)
    {
        _store = store;
        _screens = screens;
        _timer = dispatcher.CreateTimer();
        _timer.Interval = TimeSpan.FromMilliseconds(500);
        _timer.Tick += (_, _) => Check();
        _store.Changed += (_, _) => Update();
        Update();
    }

    /// <summary><see cref="Reason"/> changed.</summary>
    public event EventHandler? Changed;

    /// <summary>What the mode was switched for, such as <c>game.exe</c>, while a rule is in charge; otherwise null.</summary>
    public string? Reason => _switcher.Reason;

    /// <summary>Goes back to the user's own mode, so that is the one saved when AmbiMote exits.</summary>
    public void Release()
    {
        _timer.Stop();
        if (_switcher.Release(_store.Current.Lighting.Mode) is { } mode)
        {
            _store.Update(l => l with { Mode = mode });
        }

        RaiseIfChanged();
    }

    public void Dispose() => _timer.Stop();

    private void Update()
    {
        if (_store.Current.AutoMode.IsActive)
        {
            _timer.Start();
        }
        else if (_timer.IsRunning)
        {
            Release();
        }
    }

    private void Check()
    {
        // The lock screen fills the screen, but the lights are off then anyway.
        if (_screens.IsLocked)
        {
            return;
        }

        var settings = _store.Current;
        if (_switcher.Update(_clock.Elapsed.TotalSeconds, ForegroundWindows.Read(), settings.Lighting.Mode, settings.AutoMode) is { } mode)
        {
            _store.Update(l => l with { Mode = mode });
        }

        RaiseIfChanged();
    }

    private void RaiseIfChanged()
    {
        if (_reason != _switcher.Reason)
        {
            _reason = _switcher.Reason;
            Changed?.Invoke(this, EventArgs.Empty);
        }
    }
}
