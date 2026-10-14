using AmbiMote.Core.Control;
using AmbiMote.Core.Engine;
using AmbiMote.Core.Settings;

using Microsoft.UI.Dispatching;
using Microsoft.UI.Xaml;

namespace AmbiMote.App;

/// <summary>
/// The running app: the lighting engine, the tray icon and its menu, and the settings window. The window is hidden,
/// not closed, when it goes to the tray, and is only created when it is first shown.
/// </summary>
public partial class App : Application
{
    private static readonly int[] s_brightnessSteps = [10, 25, 40, 60, 80, 100];

    private readonly EventWaitHandle _showRequests;
    private readonly string _iconOn = Path.Combine(AppContext.BaseDirectory, "Assets", "ambimote.ico");
    private readonly string _iconOff = Path.Combine(AppContext.BaseDirectory, "Assets", "ambimote-off.ico");
    private DispatcherQueue _dispatcher = null!;
    private SettingsStore _store = null!;
    private LightingEngine _engine = null!;
    private TrayIcon _tray = null!;
    private ScreenStateWatcher _screens = null!;
    private ControlServer _control = null!;
    private HotkeyManager _hotkeys = null!;
    private AutoModeWatcher _autoMode = null!;
    private RegisteredWaitHandle? _showRegistration;
    private MainWindow? _window;
    private bool _exiting;

    public App(EventWaitHandle showRequests)
    {
        _showRequests = showRequests;
        InitializeComponent();
    }

    internal static string ModeName(LightingMode mode) => mode switch
    {
        LightingMode.Off => "Off",
        LightingMode.Light => "Light",
        LightingMode.Colour => "Colour",
        LightingMode.Effect => "Effect",
        _ => "Ambient",
    };

    protected override void OnLaunched(LaunchActivatedEventArgs args)
    {
        // Keep running in the tray with no window open.
        DispatcherShutdownMode = DispatcherShutdownMode.OnExplicitShutdown;
        _dispatcher = DispatcherQueue.GetForCurrentThread();
        _store = new SettingsStore(AppSettings.DefaultPath, _dispatcher);
        _engine = new LightingEngine(new WindowsLightingHardware(), _store.Current.Lighting);

        _tray = new TrayIcon { MenuRequested = BuildMenu };
        _tray.Clicked += (_, _) => ShowWindow();
        _screens = new ScreenStateWatcher();
        _screens.Changed += (_, _) => UpdatePaused();
        _autoMode = new AutoModeWatcher(_store, _screens, _dispatcher);
        _autoMode.Changed += (_, _) => UpdateTray();
        _hotkeys = new HotkeyManager();
        _hotkeys.Pressed += (_, action) => Run(HotkeySettings.CommandFor(action));
        _hotkeys.Apply(_store.Current.Hotkeys);
        _store.Changed += (_, _) =>
        {
            _engine.Apply(_store.Current.Lighting);
            _hotkeys.Apply(_store.Current.Hotkeys);
            UpdatePaused();
            UpdateTray();
        };
        _engine.StatusChanged += (_, status) => _dispatcher.TryEnqueue(() => OnStatusChanged(status));
        _showRegistration = ThreadPool.RegisterWaitForSingleObject(
            _showRequests, (_, _) => _dispatcher.TryEnqueue(ShowWindow), null, Timeout.Infinite, executeOnlyOnce: false);

        UpdatePaused();
        _engine.Start();
        _control = new ControlServer(ControlPipe.DefaultName, HandleControlRequest);
        _control.Start();
        UpdateTray();
        if (_store.LoadProblem is { } problem)
        {
            _tray.ShowWarning("AmbiMote", problem);
        }

        if (_hotkeys.Problems.Count > 0)
        {
            _tray.ShowWarning("AmbiMote shortcuts", string.Join(" ", _hotkeys.Problems) + " Shortcuts can be changed in the settings.");
        }

        if (!_store.Current.StartMinimised)
        {
            ShowWindow();
        }
    }

    private void ShowWindow()
    {
        if (_exiting)
        {
            return;
        }

        if (_window is null)
        {
            _window = new MainWindow(_store, new StartupRegistration(), _engine, _hotkeys, _autoMode, _iconOn);
            _window.ReconnectRequested += (_, _) => _engine.Reconnect();
            _window.AppWindow.Closing += (_, e) =>
            {
                if (_exiting)
                {
                    return;
                }

                // With minimise to tray, closing the window also leaves AmbiMote running; Exit is in the tray menu.
                e.Cancel = true;
                if (_store.Current.MinimiseToTray)
                {
                    _window.AppWindow.Hide();
                }
                else
                {
                    Exit();
                }
            };
        }

        _window.ShowAndActivate();
    }

    private new void Exit()
    {
        if (_exiting)
        {
            return;
        }

        // Turn the lights off and save before the app ends.
        _exiting = true;
        _showRegistration?.Unregister(null);

        // Not waited for: a request in flight is waiting for this thread, and the pipe closes with the process anyway.
        _ = _control.DisposeAsync().AsTask();
        _autoMode.Release();
        _autoMode.Dispose();
        _engine.Dispose();
        _store.Flush();
        _hotkeys.Dispose();
        _screens.Dispose();
        _tray.Dispose();
        _window?.Close();
        base.Exit();
    }

    private IReadOnlyList<TrayMenuItem> BuildMenu()
    {
        var lighting = _store.Current.Lighting;
        int brightness = (int)Math.Round(lighting.Brightness * 100);
        int nearest = s_brightnessSteps.MinBy(step => Math.Abs(step - brightness));
        var items = new List<TrayMenuItem> { new("Open AmbiMote", ShowWindow, IsDefault: true), TrayMenuItem.Separator };
        foreach (var mode in Enum.GetValues<LightingMode>())
        {
            items.Add(new TrayMenuItem(ModeName(mode), () => _store.Update(l => l with { Mode = mode }), Checked: mode == lighting.Mode));
        }

        items.Add(TrayMenuItem.Separator);
        items.Add(new TrayMenuItem(
            $"Brightness ({brightness}%)",
            Enabled: lighting.Mode != LightingMode.Off,
            Children: [.. s_brightnessSteps.Select(step => new TrayMenuItem(
                $"{step}%", () => _store.Update(l => l with { Brightness = step / 100.0 }), Checked: step == nearest))]));
        items.Add(new TrayMenuItem("Reconnect", _engine.Reconnect));
        items.Add(TrayMenuItem.Separator);
        items.Add(new TrayMenuItem("Exit", Exit));
        return items;
    }

    /// <summary>Runs a request from the control pipe on the UI thread, where the settings are changed.</summary>
    private Task<string> HandleControlRequest(string line)
    {
        var reply = new TaskCompletionSource<string>(TaskCreationOptions.RunContinuationsAsynchronously);
        if (!_dispatcher.TryEnqueue(() =>
        {
            if (!ControlCommand.TryParse(line, out var command, out string error))
            {
                reply.SetResult(ControlReply.Failed(error).ToJson());
                return;
            }

            Run(command);
            reply.SetResult(ControlReply.From(_store.Current, _engine.Status).ToJson());
        }))
        {
            reply.SetResult(ControlReply.Failed("AmbiMote is closing.").ToJson());
        }

        return reply.Task;
    }

    /// <summary>Does what a request from the control pipe or a hotkey asks.</summary>
    private void Run(ControlCommand command)
    {
        if (_exiting)
        {
            return;
        }

        switch (command.Verb)
        {
            case ControlVerb.Identify:
                _engine.Identify();
                break;
            case ControlVerb.Reconnect:
                _engine.Reconnect();
                break;
            case ControlVerb.Show:
                ShowWindow();
                break;
            default:
                _store.Update(command.ApplyTo);
                break;
        }
    }

    /// <summary>Lights off while nobody can see the screens, if the user wants that.</summary>
    private void UpdatePaused() =>
        _engine.Paused = _store.Current.PauseWhenAway && (_screens.IsLocked || _screens.AreScreensOff);

    private void UpdateTray()
    {
        var status = _engine.Status;
        _tray.SetIcon(status.State is EngineState.Running or EngineState.Identifying ? _iconOn : _iconOff);
        string mode = ModeName(_store.Current.Lighting.Mode) + (_autoMode.Reason is { } reason ? $" (automatic, for {reason})" : "");
        string text = $"AmbiMote: {mode}" + (status.State == EngineState.Off ? "" : $"\n{status.Message}");
        _tray.SetTooltip(text.Length <= 127 ? text : string.Concat(text.AsSpan(0, 126), "…"));
    }

    private void OnStatusChanged(EngineStatus status)
    {
        if (_exiting)
        {
            return;
        }

        _window?.ShowStatus(status);
        UpdateTray();
    }
}
