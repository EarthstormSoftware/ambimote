using System.Diagnostics;
using System.Globalization;

using AmbiMote.Core;
using AmbiMote.Core.Config;
using AmbiMote.Core.Engine;
using AmbiMote.Core.Lighting;
using AmbiMote.Core.Processing;
using AmbiMote.Core.Settings;

using Microsoft.UI;
using Microsoft.UI.Dispatching;
using Microsoft.UI.Input;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Controls.Primitives;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Media.Imaging;
using Microsoft.UI.Xaml.Shapes;

using Windows.Graphics;
using VirtualKey = Windows.System.VirtualKey;
using Windows.UI.Core;

namespace AmbiMote.App;

/// <summary>The settings window: choose the mode, adjust it, and set how AmbiMote starts.</summary>
public sealed partial class MainWindow : Window
{
    /// <summary>The window's size in device-independent pixels: a fixed width, and a height that fits the page.</summary>
    private const double WindowWidth = 600, CompactPaneWidth = 48, MinHeight = 400;

    private static readonly string[] s_swatchNames = ["red", "orange", "yellow", "green", "cyan", "blue", "purple", "magenta", "pink", "white"];

    private static readonly (string Name, int Kelvin)[] s_whites = [("Candle", 1900), ("Warm", 2700), ("Neutral", 4000), ("Daylight", 6500)];

    /// <summary>The modes a rule can switch to, in the order the lists show them.</summary>
    private static readonly LightingMode[] s_ruleModes = [LightingMode.Light, LightingMode.Colour, LightingMode.Effect, LightingMode.Ambient, LightingMode.Off];

    private static readonly (HotkeyAction Action, string Label)[] s_hotkeyLabels =
    [
        (HotkeyAction.Toggle, "Lights on or off"),
        (HotkeyAction.NextMode, "Next mode"),
        (HotkeyAction.BrightnessUp, "Brighter"),
        (HotkeyAction.BrightnessDown, "Dimmer"),
    ];

    private static readonly Dictionary<EffectKind, string> s_effectHelp = new()
    {
        [EffectKind.Rainbow] = "A rainbow flowing around the monitors",
        [EffectKind.Breathe] = "The colour slowly brightening and dimming",
        [EffectKind.Candle] = "Each LED flickering gently, like a candle",
        [EffectKind.Chase] = "A dot running around the monitors, to check the mapping",
    };

    private readonly SettingsStore _store;
    private readonly StartupRegistration _startup;
    private readonly List<(Ellipse Swatch, Rgb Colour)> _swatches = [];
    private readonly Dictionary<LightingMode, FrameworkElement> _modePanels;
    private bool _updating;
    private bool _placed;
    private bool _showingSettings;
    private readonly LightingEngine _engine;
    private readonly StickPreview _preview;
    private readonly DispatcherQueueTimer _previewTimer;
    private readonly HotkeyManager _hotkeys;
    private readonly AutoModeWatcher _autoMode;
    private IReadOnlyList<AppModeRule>? _shownRules;
    private readonly Dictionary<HotkeyAction, Button> _hotkeyButtons = [];

    /// <summary>The shortcut being recorded, if any: its button is waiting for keys.</summary>
    private HotkeyAction? _recording;

    /// <summary>The key that finished a recording, whose release must not click the button and start again.</summary>
    private VirtualKey? _recordedKey;

    internal MainWindow(SettingsStore store, StartupRegistration startup, LightingEngine engine, HotkeyManager hotkeys, AutoModeWatcher autoMode, string iconPath)
    {
        _store = store;
        _engine = engine;
        _startup = startup;
        _hotkeys = hotkeys;
        _autoMode = autoMode;

        // Loading the XAML sets each slider's minimum, which raises ValueChanged; ignore that until the settings are shown.
        _updating = true;
        InitializeComponent();

        ExtendsContentIntoTitleBar = true;
        SetTitleBar(AppTitleBar);
        AppWindow.SetIcon(iconPath);
        TitleIcon.ImageSource = new BitmapImage(new Uri(iconPath));
        if (AppWindow.Presenter is OverlappedPresenter presenter)
        {
            presenter.IsResizable = false;
            presenter.IsMaximizable = false;
        }

        ConfigPath.Text = AmbiMoteConfig.DefaultPath;
        AboutIcon.Source = new BitmapImage(new Uri(iconPath));
        AboutVersion.Text = $"AmbiMote {typeof(MainWindow).Assembly.GetName().Version?.ToString(3)}";
        _modePanels = new()
        {
            [LightingMode.Light] = LightPanel,
            [LightingMode.Colour] = ColourPanel,
            [LightingMode.Effect] = EffectPanel,
            [LightingMode.Ambient] = AmbientPanel,
        };
        foreach (var effect in Enum.GetValues<EffectKind>())
        {
            EffectBox.Items.Add(effect.ToString());
        }

        BuildWhitePresets();
        BuildSwatches(ColourSwatches);
        BuildSwatches(EffectSwatches);
        BuildHotkeyRows();
        FullScreenModeBox.Items.Add("Don't change");
        foreach (var mode in s_ruleModes)
        {
            FullScreenModeBox.Items.Add(App.ModeName(mode));
        }


        UpdateControls();
        ShowStatus(engine.Status);
        _preview = new StickPreview(PreviewCanvas);
        _previewTimer = DispatcherQueue.CreateTimer();
        _previewTimer.Interval = TimeSpan.FromSeconds(1.0 / 30);
        _previewTimer.Tick += (_, _) => _preview.Show(_engine.Preview);
        _store.Changed += (_, _) => UpdateControls();
        _hotkeys.Changed += (_, _) => ShowHotkeyProblems();
        _autoMode.Changed += (_, _) => UpdateControls();
        Activated += (_, e) =>
        {
            if (e.WindowActivationState == WindowActivationState.Deactivated)
            {
                StopRecording();
            }
        };
        AppWindow.Changed += AppWindow_Changed;
        Root.Loaded += (_, _) => FitHeight();
    }

    public event EventHandler? ReconnectRequested;

    public void ShowAndActivate()
    {
        if (AppWindow.Presenter is OverlappedPresenter { State: OverlappedPresenterState.Minimized } presenter)
        {
            presenter.Restore();
        }

        if (!_placed)
        {
            FitHeight();
            var area = DisplayArea.GetFromWindowId(AppWindow.Id, DisplayAreaFallback.Primary).WorkArea;
            AppWindow.Move(new PointInt32(area.X + ((area.Width - AppWindow.Size.Width) / 2), area.Y + ((area.Height - AppWindow.Size.Height) / 3)));
            _placed = true;
        }

        AppWindow.Show();
        SetPreviewRunning(true);
        Activate();
    }

    public void ShowStatus(EngineStatus status)
    {
        StatusDot.Fill = ThemeBrush(status.State switch
        {
            EngineState.Running or EngineState.Identifying => "SystemFillColorSuccessBrush",
            EngineState.Waiting => "SystemFillColorCautionBrush",
            EngineState.Error => "SystemFillColorCriticalBrush",
            _ => "SystemFillColorNeutralBrush",
        });
        StatusText.Text = status.State == EngineState.Off ? "The sticks are off" : status.Message;
        ProblemsButton.Visibility = status.Problems.Count > 0 ? Visibility.Visible : Visibility.Collapsed;
        ProblemsCount.Text = status.Problems.Count == 1 ? "1 problem" : $"{status.Problems.Count} problems";
        ProblemsText.Text = string.Join(Environment.NewLine + Environment.NewLine, status.Problems);
        IdentifyButton.Content = status.State == EngineState.Identifying ? "Stop" : "Identify";
        IdentifyButton.IsEnabled = status.State != EngineState.Paused;
    }

    private static Brush ThemeBrush(string key) => (Brush)Application.Current.Resources[key];

    private static SolidColorBrush ToBrush(Rgb colour) => new(ColorHelper.FromArgb(255, colour.R, colour.G, colour.B));

    private static Rgb ToRgb(Windows.UI.Color colour) => new(colour.R, colour.G, colour.B);

    private static string Number(double value, string format) => value.ToString(format, CultureInfo.CurrentCulture);

    private void AppWindow_Changed(AppWindow sender, AppWindowChangedEventArgs args)
    {
        if (_store.Current.MinimiseToTray && sender.Presenter is OverlappedPresenter { State: OverlappedPresenterState.Minimized })
        {
            sender.Hide();
        }

        if (args.DidVisibilityChange || args.DidPresenterChange)
        {
            SetPreviewRunning(sender.IsVisible && sender.Presenter is not OverlappedPresenter { State: OverlappedPresenterState.Minimized });

            if (!sender.IsVisible)
            {
                StopRecording();
            }
        }
    }

    private void PreviewCanvas_SizeChanged(object sender, SizeChangedEventArgs e) => _preview.Relayout();

    /// <summary>Keeps the stick preview moving only while the window can be seen, so it costs nothing in the tray.</summary>
    private void SetPreviewRunning(bool running)
    {
        _engine.PreviewEnabled = running;
        if (running)
        {
            _previewTimer.Start();
        }
        else
        {
            _previewTimer.Stop();
        }
    }

    /// <summary>Makes the window exactly as tall as its content, so each mode gets the room it needs and no more.</summary>
    private void FitHeight()
    {
        if (Root.XamlRoot is not { } xamlRoot)
        {
            return;
        }

        // The page sits beside the compact menu bar, which needs room for its items and the cog.
        Page.Measure(new Windows.Foundation.Size(WindowWidth - CompactPaneWidth, double.PositiveInfinity));
        StatusBarArea.Measure(new Windows.Foundation.Size(WindowWidth, double.PositiveInfinity));
        double height = Math.Max(MinHeight, AppTitleBar.ActualHeight + Page.DesiredSize.Height + StatusBarArea.DesiredSize.Height);
        double scale = xamlRoot.RasterizationScale;
        AppWindow.ResizeClient(new SizeInt32((int)Math.Ceiling(WindowWidth * scale), (int)Math.Ceiling(height * scale)));
    }

    private void FitHeightSoon() => DispatcherQueue.TryEnqueue(DispatcherQueuePriority.Low, FitHeight);

    private void BuildWhitePresets()
    {
        foreach (var (name, kelvin) in s_whites)
        {
            var content = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8 };
            content.Children.Add(new Ellipse
            {
                Width = 12,
                Height = 12,
                Fill = ToBrush(Rgb.FromKelvin(kelvin)),
                Stroke = ThemeBrush("ControlStrokeColorDefaultBrush"),
                VerticalAlignment = VerticalAlignment.Center,
            });
            content.Children.Add(new TextBlock { Text = name });
            var button = new Button { Content = content };
            button.Click += (_, _) => _store.Update(l => l with { Kelvin = kelvin });
            WhitePresets.Children.Add(button);
        }
    }

    private void BuildSwatches(Panel panel)
    {
        foreach (string name in s_swatchNames)
        {
            Colours.TryParse(name, out var colour);
            var swatch = new Ellipse { Width = 24, Height = 24, Fill = ToBrush(colour) };
            var button = new Button
            {
                Content = swatch,
                Width = 32,
                Height = 32,
                Padding = new Thickness(0),
                CornerRadius = new CornerRadius(16),
                Background = new SolidColorBrush(Colors.Transparent),
                BorderThickness = new Thickness(0),
            };
            ToolTipService.SetToolTip(button, CultureInfo.CurrentCulture.TextInfo.ToTitleCase(name));
            button.Click += (_, _) => _store.Update(l => l with { Colour = colour });
            _swatches.Add((swatch, colour));
            panel.Children.Add(button);
        }
    }

    /// <summary>Shows the current settings in every control, without treating that as the user changing them.</summary>
    private void UpdateControls()
    {
        _updating = true;
        try
        {
            var settings = _store.Current;
            var lighting = settings.Lighting;
            bool on = lighting.Mode != LightingMode.Off;

            // While the lights are off, the page shows the mode they will come back on in.
            var page = settings.LastMode;
            NavView.SelectedItem = _showingSettings
                ? NavView.SettingsItem
                : NavView.MenuItems.OfType<NavigationViewItem>().First(i => (string)i.Tag == page.ToString());
            PageTitle.Text = _showingSettings ? "Settings" : App.ModeName(page);
            CommonPanel.Visibility = _showingSettings ? Visibility.Collapsed : Visibility.Visible;
            SettingsPanel.Visibility = _showingSettings ? Visibility.Visible : Visibility.Collapsed;
            foreach (var (panelMode, panel) in _modePanels)
            {
                panel.Visibility = !_showingSettings && panelMode == page ? Visibility.Visible : Visibility.Collapsed;
            }

            LightsSwitch.IsOn = on;
            string automatic = _autoMode.Reason is { } reason ? $", automatically for {reason}" : "";
            LightsDescription.Text = on ? $"{App.ModeName(lighting.Mode)} mode{automatic}" : $"Off. Turning on goes back to {App.ModeName(page)}.";
            BrightnessSlider.Value = Math.Round(lighting.Brightness * 100);
            BrightnessSlider.IsEnabled = on;
            BrightnessValue.Text = $"{Math.Round(lighting.Brightness * 100)}%";
            PowerSlider.Value = lighting.MaxMilliamps;
            PowerValue.Text = $"{lighting.MaxMilliamps:0} mA";

            KelvinSlider.Value = lighting.Kelvin;
            KelvinValue.Text = $"{lighting.Kelvin} K";

            ColourHex.Text = lighting.Colour.ToString().ToUpperInvariant();
            var selected = ThemeBrush("AccentFillColorDefaultBrush");
            var outline = ThemeBrush("ControlStrongStrokeColorDefaultBrush");
            foreach (var (swatch, colour) in _swatches)
            {
                bool isSelected = colour == lighting.Colour;
                swatch.Stroke = isSelected ? selected : outline;
                swatch.StrokeThickness = isSelected ? 3 : 1;
            }

            EffectBox.SelectedItem = lighting.Effect.ToString();
            EffectHelp.Text = s_effectHelp[lighting.Effect];
            SpeedCard.Visibility = lighting.Effect == EffectKind.Candle ? Visibility.Collapsed : Visibility.Visible;
            SpeedSlider.Value = Math.Round(lighting.EffectSpeed * 100);
            SpeedValue.Text = $"{Number(lighting.EffectSpeed, "0.00")}×";
            EffectColourCard.Visibility = lighting.Effect is EffectKind.Breathe or EffectKind.Chase ? Visibility.Visible : Visibility.Collapsed;

            SmoothingSlider.Value = Math.Round(lighting.Smoothing * 100);
            SmoothingValue.Text = $"{Number(lighting.Smoothing, "0.00")} s";
            SaturationSlider.Value = Math.Round(lighting.Saturation * 100);
            SaturationValue.Text = Number(lighting.Saturation, "0.00");
            GammaSlider.Value = Math.Round(lighting.Gamma * 100);
            GammaValue.Text = Number(lighting.Gamma, "0.00");

            StartWithWindowsSwitch.IsOn = _startup.IsEnabled;
            StartMinimisedSwitch.IsOn = settings.StartMinimised;
            MinimiseToTraySwitch.IsOn = settings.MinimiseToTray;
            PauseWhenAwaySwitch.IsOn = settings.PauseWhenAway;
            FullScreenModeBox.SelectedIndex = settings.AutoMode.FullScreen is { } fullScreen ? Array.IndexOf(s_ruleModes, fullScreen) + 1 : 0;
            ShowAppRules(settings.AutoMode.Apps);
            HotkeysSwitch.IsOn = settings.Hotkeys.Enabled;
            foreach (var (action, button) in _hotkeyButtons)
            {
                button.Content = _recording == action ? "Press the keys..." : settings.Hotkeys.Get(action) ?? "None";
                button.IsEnabled = settings.Hotkeys.Enabled;
            }

            ShowHotkeyProblems();
        }
        finally
        {
            _updating = false;
        }

        FitHeightSoon();
    }

    /// <summary>Choosing a mode turns the lights on in it; the cog shows the settings without changing the lights.</summary>
    private void NavView_ItemInvoked(NavigationView sender, NavigationViewItemInvokedEventArgs args)
    {
        if (args.IsSettingsInvoked)
        {
            _showingSettings = true;
        }
        else if (args.InvokedItemContainer?.Tag is string tag && Enum.TryParse<LightingMode>(tag, out var mode))
        {
            _showingSettings = false;
            _store.Update(l => l with { Mode = mode });
        }

        UpdateControls();
    }

    private void AppTitleBar_PaneToggleRequested(TitleBar sender, object args) => NavView.IsPaneOpen = !NavView.IsPaneOpen;

    private void LightsSwitch_Toggled(object sender, RoutedEventArgs e)
    {
        if (!_updating)
        {
            _store.Update(s => s.WithLightsOn(LightsSwitch.IsOn));
        }
    }

    private void PowerSlider_ValueChanged(object sender, RangeBaseValueChangedEventArgs e)
    {
        if (!_updating)
        {
            _store.Update(l => l with { MaxMilliamps = e.NewValue });
        }
    }

    private void OpenConfigFolder_Click(object sender, RoutedEventArgs e)
    {
        string folder = System.IO.Path.GetDirectoryName(AmbiMoteConfig.DefaultPath)!;
        Directory.CreateDirectory(folder);
        Process.Start(new ProcessStartInfo("explorer.exe", $"\"{folder}\"") { UseShellExecute = true });
    }

    private void Reconnect_Click(object sender, RoutedEventArgs e) => ReconnectRequested?.Invoke(this, EventArgs.Empty);

    private void IdentifyButton_Click(object sender, RoutedEventArgs e)
    {
        if (_engine.Status.State == EngineState.Identifying)
        {
            _engine.StopIdentifying();
        }
        else
        {
            _engine.Identify();
        }
    }

    private void BrightnessSlider_ValueChanged(object sender, RangeBaseValueChangedEventArgs e)
    {
        if (!_updating)
        {
            _store.Update(l => l with { Brightness = e.NewValue / 100 });
        }
    }

    private void KelvinSlider_ValueChanged(object sender, RangeBaseValueChangedEventArgs e)
    {
        if (!_updating)
        {
            _store.Update(l => l with { Kelvin = (int)e.NewValue });
        }
    }

    private void ColourFlyout_Opening(object sender, object e)
    {
        var colour = _store.Current.Lighting.Colour;
        _updating = true;
        ColourPicker.Color = ColorHelper.FromArgb(255, colour.R, colour.G, colour.B);
        _updating = false;
    }

    private void ColourPicker_ColorChanged(ColorPicker sender, ColorChangedEventArgs args)
    {
        if (!_updating)
        {
            _store.Update(l => l with { Colour = ToRgb(args.NewColor) });
        }
    }

    private void EffectBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (!_updating && EffectBox.SelectedItem is string name && Enum.TryParse<EffectKind>(name, out var effect))
        {
            _store.Update(l => l with { Effect = effect });
        }
    }

    private void SpeedSlider_ValueChanged(object sender, RangeBaseValueChangedEventArgs e)
    {
        if (!_updating)
        {
            _store.Update(l => l with { EffectSpeed = e.NewValue / 100 });
        }
    }

    private void SmoothingSlider_ValueChanged(object sender, RangeBaseValueChangedEventArgs e)
    {
        if (!_updating)
        {
            _store.Update(l => l with { Smoothing = e.NewValue / 100 });
        }
    }

    private void SaturationSlider_ValueChanged(object sender, RangeBaseValueChangedEventArgs e)
    {
        if (!_updating)
        {
            _store.Update(l => l with { Saturation = e.NewValue / 100 });
        }
    }

    private void GammaSlider_ValueChanged(object sender, RangeBaseValueChangedEventArgs e)
    {
        if (!_updating)
        {
            _store.Update(l => l with { Gamma = e.NewValue / 100 });
        }
    }

    private void AmbientDefaults_Click(object sender, RoutedEventArgs e)
    {
        var defaults = new LightingSettings();
        _store.Update(l => l with { Smoothing = defaults.Smoothing, Saturation = ColourProcessor.DefaultSaturation, Gamma = ColourProcessor.DefaultGamma });
    }

    private void StartWithWindowsSwitch_Toggled(object sender, RoutedEventArgs e)
    {
        if (_updating)
        {
            return;
        }

        try
        {
            if (StartWithWindowsSwitch.IsOn)
            {
                _startup.Enable(Environment.ProcessPath!);
            }
            else
            {
                _startup.Disable();
            }
        }
        catch (Exception ex) when (ex is UnauthorizedAccessException or IOException or System.Security.SecurityException)
        {
            StatusDot.Fill = ThemeBrush("SystemFillColorCriticalBrush");
            StatusText.Text = $"Could not change the Windows startup setting: {ex.Message}";
        }

        UpdateControls();
    }

    private void StartMinimisedSwitch_Toggled(object sender, RoutedEventArgs e)
    {
        if (!_updating)
        {
            _store.Update(s => s with { StartMinimised = StartMinimisedSwitch.IsOn });
        }
    }

    private void MinimiseToTraySwitch_Toggled(object sender, RoutedEventArgs e)
    {
        if (!_updating)
        {
            _store.Update(s => s with { MinimiseToTray = MinimiseToTraySwitch.IsOn });
        }
    }

    private void PauseWhenAwaySwitch_Toggled(object sender, RoutedEventArgs e)
    {
        if (!_updating)
        {
            _store.Update(s => s with { PauseWhenAway = PauseWhenAwaySwitch.IsOn });
        }
    }

    private void HotkeysSwitch_Toggled(object sender, RoutedEventArgs e)
    {
        if (!_updating)
        {
            _store.Update(s => s with { Hotkeys = s.Hotkeys with { Enabled = HotkeysSwitch.IsOn } });
        }
    }

    private void BuildHotkeyRows()
    {
        foreach (var (action, label) in s_hotkeyLabels)
        {
            var row = new Grid();
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            row.Children.Add(new TextBlock { Text = label, VerticalAlignment = VerticalAlignment.Center });
            var button = new Button { MinWidth = 180, HorizontalContentAlignment = HorizontalAlignment.Center };
            Grid.SetColumn(button, 1);
            button.Click += (_, _) => StartRecording(action);
            button.PreviewKeyDown += (_, e) => RecordKey(action, e);
            button.PreviewKeyUp += (_, e) =>
            {
                if (e.Key == _recordedKey)
                {
                    e.Handled = true;
                    _recordedKey = null;
                }
            };
            button.LostFocus += (_, _) => StopRecording();
            row.Children.Add(button);
            _hotkeyButtons[action] = button;
            HotkeyRows.Children.Add(row);
        }
    }

    private void ShowHotkeyProblems()
    {
        HotkeyProblems.Text = string.Join(Environment.NewLine, _hotkeys.Problems);
        HotkeyProblems.Visibility = _hotkeys.Problems.Count > 0 && _store.Current.Hotkeys.Enabled ? Visibility.Visible : Visibility.Collapsed;
        FitHeightSoon();
    }

    /// <summary>Waits for keys on the shortcut's button. Windows lets go of the shortcuts meanwhile, so they reach it.</summary>
    private void StartRecording(HotkeyAction action)
    {
        _recording = action;
        _hotkeys.Suspended = true;
        UpdateControls();
    }

    private void StopRecording()
    {
        if (_recording is null)
        {
            return;
        }

        _recording = null;
        _hotkeys.Suspended = false;
        UpdateControls();
    }

    private void RecordKey(HotkeyAction action, KeyRoutedEventArgs e)
    {
        if (_recording != action)
        {
            return;
        }

        e.Handled = true;
        var modifiers = CurrentModifiers();
        if (e.Key == VirtualKey.Escape)
        {
            StopRecording();
            return;
        }

        if (e.Key is VirtualKey.Back or VirtualKey.Delete && modifiers == HotkeyModifiers.None)
        {
            SaveRecording(action, null, e.Key);
            return;
        }

        // Wait for the key that goes with the modifiers.
        if (e.Key is VirtualKey.Control or VirtualKey.LeftControl or VirtualKey.RightControl
            or VirtualKey.Menu or VirtualKey.LeftMenu or VirtualKey.RightMenu
            or VirtualKey.Shift or VirtualKey.LeftShift or VirtualKey.RightShift
            or VirtualKey.LeftWindows or VirtualKey.RightWindows)
        {
            return;
        }

        if (!Hotkey.TryCreate(modifiers, (int)e.Key, out var hotkey))
        {
            _hotkeyButtons[action].Content = "Add Ctrl, Alt or Win";
            return;
        }

        SaveRecording(action, hotkey, e.Key);
    }

    private void SaveRecording(HotkeyAction action, Hotkey? hotkey, VirtualKey key)
    {
        _recordedKey = key;
        _recording = null;
        _store.Update(s => s with { Hotkeys = s.Hotkeys.With(action, hotkey) });
        _hotkeys.Suspended = false;
        UpdateControls();
    }

    private static HotkeyModifiers CurrentModifiers()
    {
        static bool IsDown(VirtualKey key) => InputKeyboardSource.GetKeyStateForCurrentThread(key).HasFlag(CoreVirtualKeyStates.Down);

        var modifiers = HotkeyModifiers.None;
        if (IsDown(VirtualKey.Control))
        {
            modifiers |= HotkeyModifiers.Ctrl;
        }

        if (IsDown(VirtualKey.Menu))
        {
            modifiers |= HotkeyModifiers.Alt;
        }

        if (IsDown(VirtualKey.Shift))
        {
            modifiers |= HotkeyModifiers.Shift;
        }

        if (IsDown(VirtualKey.LeftWindows) || IsDown(VirtualKey.RightWindows))
        {
            modifiers |= HotkeyModifiers.Win;
        }

        return modifiers;
    }

    private void FullScreenModeBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (!_updating && FullScreenModeBox.SelectedIndex >= 0)
        {
            LightingMode? mode = FullScreenModeBox.SelectedIndex == 0 ? null : s_ruleModes[FullScreenModeBox.SelectedIndex - 1];
            _store.Update(s => s with { AutoMode = s.AutoMode with { FullScreen = mode } });
        }
    }

    /// <summary>One row per program rule. Only rebuilt when the rules change, so a list being used is not replaced.</summary>
    private void ShowAppRules(IReadOnlyList<AppModeRule> rules)
    {
        if (_shownRules is not null && _shownRules.SequenceEqual(rules))
        {
            return;
        }

        _shownRules = rules;
        AppRuleRows.Children.Clear();
        foreach (var rule in rules)
        {
            var row = new Grid { ColumnSpacing = 8 };
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            row.Children.Add(new TextBlock { Text = rule.App, VerticalAlignment = VerticalAlignment.Center, TextTrimming = TextTrimming.CharacterEllipsis });

            var modeBox = new ComboBox { MinWidth = 150 };
            foreach (var mode in s_ruleModes)
            {
                modeBox.Items.Add(App.ModeName(mode));
            }

            modeBox.SelectedIndex = Array.IndexOf(s_ruleModes, rule.Mode);
            modeBox.SelectionChanged += (_, _) =>
            {
                if (!_updating && modeBox.SelectedIndex >= 0)
                {
                    _store.Update(s => s with { AutoMode = s.AutoMode.WithApp(rule.App, s_ruleModes[modeBox.SelectedIndex]) });
                }
            };
            Grid.SetColumn(modeBox, 1);
            row.Children.Add(modeBox);

            var remove = new Button { Content = new FontIcon { Glyph = "", FontSize = 14 } };
            ToolTipService.SetToolTip(remove, $"Remove {rule.App}");
            remove.Click += (_, _) => _store.Update(s => s with { AutoMode = s.AutoMode.WithoutApp(rule.App) });
            Grid.SetColumn(remove, 2);
            row.Children.Add(remove);
            AppRuleRows.Children.Add(row);
        }

        AppRuleRows.Visibility = rules.Count > 0 ? Visibility.Visible : Visibility.Collapsed;
    }

    /// <summary>Offers the programs with a window open, so the one to add can be picked rather than typed.</summary>
    private void AddAppBox_DropDownOpened(object sender, object e)
    {
        var known = _store.Current.AutoMode.Apps.Select(r => r.App).ToHashSet(StringComparer.OrdinalIgnoreCase);
        AddAppBox.Items.Clear();
        foreach (string app in ForegroundWindows.RunningApps().Where(a => !known.Contains(a)))
        {
            AddAppBox.Items.Add(app);
        }
    }

    private void AddApp_Click(object sender, RoutedEventArgs e)
    {
        string app = AutoModeSettings.NormaliseApp(AddAppBox.Text ?? "");
        if (app.Length == 0)
        {
            return;
        }

        _store.Update(s => s with { AutoMode = s.AutoMode.WithApp(app, LightingMode.Ambient) });
        AddAppBox.Text = "";
        AddAppBox.SelectedIndex = -1;
    }
}
