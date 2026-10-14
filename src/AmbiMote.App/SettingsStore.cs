using AmbiMote.Core.Engine;
using AmbiMote.Core.Settings;

using Microsoft.UI.Dispatching;

namespace AmbiMote.App;

/// <summary>The current settings, shared by the window and the tray menu, saved a moment after they stop changing.</summary>
internal sealed class SettingsStore
{
    private readonly string _path;
    private readonly DispatcherQueueTimer _saveTimer;
    private bool _unsaved;

    public SettingsStore(string path, DispatcherQueue dispatcher)
    {
        _path = path;
        Current = AppSettings.Load(path, out string? problem);
        LoadProblem = problem;
        _saveTimer = dispatcher.CreateTimer();
        _saveTimer.Interval = TimeSpan.FromSeconds(1);
        _saveTimer.IsRepeating = false;
        _saveTimer.Tick += (_, _) => Save();
    }

    /// <summary>Raised on the UI thread after <see cref="Current"/> changes.</summary>
    public event EventHandler? Changed;

    public AppSettings Current { get; private set; }

    /// <summary>Why the saved settings could not be read, if they could not.</summary>
    public string? LoadProblem { get; }

    public void Update(Func<AppSettings, AppSettings> change)
    {
        var next = change(Current);
        next = next.Normalised();
        if (next == Current)
        {
            return;
        }

        Current = next;
        _unsaved = true;
        _saveTimer.Stop();
        _saveTimer.Start();
        Changed?.Invoke(this, EventArgs.Empty);
    }

    public void Update(Func<LightingSettings, LightingSettings> change) => Update(s => s with { Lighting = change(s.Lighting) });

    /// <summary>Saves now if anything is waiting to be saved.</summary>
    public void Flush()
    {
        if (_unsaved)
        {
            Save();
        }
    }

    private void Save()
    {
        _saveTimer.Stop();
        try
        {
            Current.Save(_path);
            _unsaved = false;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // Settings are a convenience: the lights keep working, and the next change tries again.
        }
    }
}
