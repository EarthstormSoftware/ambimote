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
        _store.Changed += (_, _) =>
        {
            _engine.Apply(_store.Current.Lighting);
            UpdateTray();
        };
        _engine.StatusChanged += (_, status) => _dispatcher.TryEnqueue(() => OnStatusChanged(status));
        _showRegistration = ThreadPool.RegisterWaitForSingleObject(
            _showRequests, (_, _) => _dispatcher.TryEnqueue(ShowWindow), null, Timeout.Infinite, executeOnlyOnce: false);

        _engine.Start();
        UpdateTray();
        if (_store.LoadProblem is { } problem)
        {
            _tray.ShowWarning("AmbiMote", problem);
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
            _window = new MainWindow(_store, new StartupRegistration(), _engine, _iconOn);
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
        _engine.Dispose();
        _store.Flush();
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

    private void UpdateTray()
    {
        var status = _engine.Status;
        _tray.SetIcon(status.State == EngineState.Running ? _iconOn : _iconOff);
        string text = $"AmbiMote: {ModeName(_store.Current.Lighting.Mode)}" + (status.State == EngineState.Off ? "" : $"\n{status.Message}");
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
