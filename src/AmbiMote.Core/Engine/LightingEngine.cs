using System.Diagnostics;

using AmbiMote.Core.Capture;
using AmbiMote.Core.Config;
using AmbiMote.Core.Lighting;
using AmbiMote.Core.Mote;
using AmbiMote.Core.Processing;
using AmbiMote.Core.Sampling;

using SharpGen.Runtime;

namespace AmbiMote.Core.Engine;

public enum EngineState
{
    /// <summary>The mode is off.</summary>
    Off,

    /// <summary>The sticks are showing the mode.</summary>
    Running,

    /// <summary>Waiting for a Mote host to be plugged in or freed; trying again every few seconds.</summary>
    Waiting,

    /// <summary>Something needs fixing, such as the config file, before the mode can run.</summary>
    Error,
}

/// <summary>What the engine is doing, for the window and the tray icon.</summary>
/// <param name="Message">One line for the user.</param>
/// <param name="Problems">Why some sticks cannot be used, such as a host in a different USB port.</param>
public sealed record EngineStatus(EngineState State, string Message, IReadOnlyList<string> Problems)
{
    public static EngineStatus Off { get; } = new(EngineState.Off, "Off", []);

    public bool Equals(EngineStatus? other) =>
        other is not null && State == other.State && Message == other.Message && Problems.SequenceEqual(other.Problems);

    public override int GetHashCode() => HashCode.Combine(State, Message, Problems.Count);
}

/// <summary>
/// Runs the chosen <see cref="LightingMode"/> on a background thread: opens the Mote hosts, shows the mode, and opens
/// them again if they are unplugged. Change what it shows with <see cref="Apply"/>; the sticks are turned off on
/// <see cref="Dispose"/>.
/// </summary>
public sealed class LightingEngine : IDisposable
{
    /// <summary>Ambient updates per second: as often as screen capture is worth doing.</summary>
    public const int FramesPerSecond = 30;

    /// <summary>Effect updates per second: more, because effects are cheap and slow fades show steps at 30.</summary>
    public const int EffectFramesPerSecond = 60;

    internal static readonly TimeSpan FrameTime = TimeSpan.FromSeconds(1.0 / FramesPerSecond);
    internal static readonly TimeSpan EffectFrameTime = TimeSpan.FromSeconds(1.0 / EffectFramesPerSecond);
    internal static readonly TimeSpan SteadyCheck = TimeSpan.FromSeconds(1);
    internal const double RetrySeconds = 3;

    private readonly ILightingHardware _hardware;
    private readonly AutoResetEvent _wake = new(false);
    private readonly CancellationTokenSource _stop = new();
    private LightingSettings _settings;
    private EngineStatus _status = EngineStatus.Off;
    private Thread? _thread;
    private Session? _session;
    private double _retryAt = double.NegativeInfinity;
    private volatile bool _reconnect;
    private bool _disposed;
    private LightingPreview? _preview;
    private volatile bool _previewEnabled;

    public LightingEngine(ILightingHardware hardware, LightingSettings settings)
    {
        ArgumentNullException.ThrowIfNull(hardware);
        ArgumentNullException.ThrowIfNull(settings);
        _hardware = hardware;
        _settings = settings.Normalised();
    }

    /// <summary>Raised on the engine's thread when <see cref="Status"/> changes.</summary>
    public event EventHandler<EngineStatus>? StatusChanged;

    public LightingSettings Settings => Volatile.Read(ref _settings);

    public EngineStatus Status => Volatile.Read(ref _status);

    /// <summary>What the sticks are showing, while <see cref="PreviewEnabled"/>; dark once they are off.</summary>
    public LightingPreview? Preview => Volatile.Read(ref _preview);

    /// <summary>
    /// Whether to keep <see cref="Preview"/> up to date. Only while something shows it, so nothing extra is done while
    /// the window is closed.
    /// </summary>
    public bool PreviewEnabled
    {
        get => _previewEnabled;
        set
        {
            _previewEnabled = value;
            _wake.Set();
        }
    }

    /// <summary>Starts the background thread.</summary>
    public void Start()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        if (_thread is not null)
        {
            return;
        }

        _thread = new Thread(Run) { IsBackground = true, Name = "AmbiMote lighting" };
        _thread.Start();
    }

    /// <summary>Shows <paramref name="settings"/> from the next frame. Out-of-range values are clamped.</summary>
    public void Apply(LightingSettings settings)
    {
        ArgumentNullException.ThrowIfNull(settings);
        Volatile.Write(ref _settings, settings.Normalised());
        _wake.Set();
    }

    /// <summary>Closes the Mote hosts, reloads the config and opens them again, without waiting for the next retry.</summary>
    public void Reconnect()
    {
        _reconnect = true;
        _wake.Set();
    }

    /// <summary>Stops the background thread and turns the sticks off.</summary>
    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        _stop.Cancel();
        if (_thread is null || !_thread.Join(TimeSpan.FromSeconds(3)))
        {
            CloseSession(turnOff: true);
        }

        _stop.Dispose();
        _wake.Dispose();
    }

    /// <summary>
    /// Does one step of work at <paramref name="now"/> seconds on the engine's clock, and returns how long to wait
    /// before the next, unless <see cref="Apply"/> or <see cref="Reconnect"/> wakes it sooner.
    /// </summary>
    internal TimeSpan Tick(double now)
    {
        var settings = Settings;
        if (_reconnect)
        {
            _reconnect = false;
            CloseSession(turnOff: false);
            _retryAt = double.NegativeInfinity;
        }

        if (settings.Mode == LightingMode.Off)
        {
            CloseSession(turnOff: true);
            SetStatus(EngineStatus.Off);
            return Timeout.InfiniteTimeSpan;
        }

        try
        {
            // A host that was missing (often the second one, still starting up at boot) may have arrived.
            if (_session is { MissingHosts: true } && now >= _retryAt)
            {
                _retryAt = now + RetrySeconds;
                if (_hardware.FindHosts().Count > _session.HostCount)
                {
                    CloseSession(turnOff: false);
                    _retryAt = double.NegativeInfinity;
                }
            }

            if (_session is null)
            {
                if (now < _retryAt)
                {
                    return TimeSpan.FromSeconds(_retryAt - now);
                }

                _retryAt = now + RetrySeconds;
                _session = TryOpenSession();
                if (_session is null)
                {
                    return TimeSpan.FromSeconds(RetrySeconds);
                }
            }

            var (animated, status) = _session.Update(settings, now);
            SetStatus(status);
            if (_previewEnabled)
            {
                Volatile.Write(ref _preview, _session.Snapshot());
            }

            return !animated ? SteadyCheck : settings.Mode == LightingMode.Effect ? EffectFrameTime : FrameTime;
        }
        catch (Exception ex) when (IsHardwareFailure(ex))
        {
            CloseSession(turnOff: false);
            SetStatus(new EngineStatus(EngineState.Waiting, "Lost the connection to the sticks. Trying again.", [ex.Message]));
            _retryAt = now + RetrySeconds;
            return TimeSpan.FromSeconds(RetrySeconds);
        }
    }

    private static bool IsHardwareFailure(Exception ex) =>
        ex is IOException or UnauthorizedAccessException or TimeoutException or SharpGenException or NotSupportedException
            or InvalidOperationException;

    private static string Count(int count, string thing) => $"{count} {thing}{(count == 1 ? "" : "s")}";

    private void Run()
    {
        var clock = Stopwatch.StartNew();
        using var timer = new HighResolutionTimer();
        WaitHandle[] untimed = [_wake, _stop.Token.WaitHandle];
        WaitHandle[] timed = [_wake, _stop.Token.WaitHandle, timer];
        while (!_stop.IsCancellationRequested)
        {
            var started = clock.Elapsed;
            TimeSpan wait;
            try
            {
                wait = Tick(started.TotalSeconds);
            }
            catch (Exception ex)
            {
                // Keep the app alive whatever happens; the next attempt starts from scratch.
                CloseSession(turnOff: false);
                SetStatus(new EngineStatus(EngineState.Error, "Something went wrong. Trying again.", [ex.Message]));
                wait = TimeSpan.FromSeconds(RetrySeconds);
            }

            if (wait == Timeout.InfiniteTimeSpan)
            {
                WaitHandle.WaitAny(untimed);
                continue;
            }

            // Count the wait from the start of this frame, so frames stay evenly spaced however long the work took.
            timer.Start(wait - (clock.Elapsed - started));
            WaitHandle.WaitAny(timed);
        }

        CloseSession(turnOff: true);
    }

    private Session? TryOpenSession()
    {
        AmbiMoteConfig? config;
        try
        {
            config = _hardware.LoadConfig();
        }
        catch (Exception ex) when (ex is InvalidDataException or IOException or UnauthorizedAccessException)
        {
            SetStatus(new EngineStatus(EngineState.Error, $"The stick mapping (config.json) cannot be used: {ex.Message}", []));
            return null;
        }

        var found = _hardware.FindHosts();
        if (found.Count == 0)
        {
            SetStatus(new EngineStatus(EngineState.Waiting, "No Mote hosts found. Waiting for one to be plugged in.", []));
            return null;
        }

        var hosts = new List<(MoteDeviceInfo Info, MoteHost Host)>();
        MoteDeviceInfo? opening = null;
        try
        {
            foreach (var info in found)
            {
                opening = info;
                var host = _hardware.OpenHost(info);
                hosts.Add((info, host));
                host.ConfigureSticks();
            }

            return new Session(_hardware, hosts, config, _hardware.ListMonitors());
        }
        catch (Exception ex) when (IsHardwareFailure(ex))
        {
            foreach (var (_, host) in hosts)
            {
                host.Dispose();
            }

            string hint = ex is UnauthorizedAccessException ? " Is another program, such as the ambimote command, using it?" : "";
            SetStatus(new EngineStatus(EngineState.Waiting, $"Could not open the Mote host on {opening?.PortName}.{hint}", [ex.Message]));
            return null;
        }
    }

    private void CloseSession(bool turnOff)
    {
        _session?.Close(turnOff);
        if (Preview is { } preview)
        {
            Volatile.Write(ref _preview, preview.Dark());
        }

        _session = null;
    }

    private void SetStatus(EngineStatus status)
    {
        if (Status.Equals(status))
        {
            return;
        }

        Volatile.Write(ref _status, status);
        StatusChanged?.Invoke(this, status);
    }

    /// <summary>Where a stick is, for <see cref="Session.Snapshot"/>.</summary>
    private sealed record LayoutStick(string HostId, MoteHost Host, int Channel, DisplayOutput? Monitor, ScreenEdge Edge, double From, double To);

    /// <summary>The open hosts and what they are showing. Only used on the engine's thread.</summary>
    private sealed class Session
    {
        private readonly ILightingHardware _hardware;
        private readonly List<(MoteDeviceInfo Info, MoteHost Host)> _hosts;
        private readonly StickChain _chain;
        private readonly IReadOnlyList<ResolvedStick> _mapped;
        private readonly bool _hasConfig;
        private readonly List<string> _problems;
        private readonly List<IScreenCapture> _captures = [];
        private readonly List<string> _ambientProblems = [];
        private readonly LayoutStick[] _layout;
        private readonly DisplayOutput[] _layoutMonitors;
        private AmbientMirror? _mirror;
        private LightingSettings? _shown;
        private IEffect? _effect;
        private double _effectTime;
        private double _last = double.NaN;

        public Session(
            ILightingHardware hardware,
            List<(MoteDeviceInfo Info, MoteHost Host)> hosts,
            AmbiMoteConfig? config,
            IReadOnlyList<DisplayOutput> monitors)
        {
            _hardware = hardware;
            _hosts = hosts;
            _hasConfig = config is not null;
            var byInfo = hosts.ToDictionary(static h => h.Info, static h => h.Host);
            if (config is null)
            {
                _mapped = [];
                _problems = [];
            }
            else
            {
                var setup = SetupResolver.Resolve(config, [.. hosts.Select(static h => h.Info)], monitors);
                _mapped = setup.Sticks;
                _problems = [.. setup.Problems];
                MissingHosts = config.Hosts.Any(c =>
                    !hosts.Any(h => string.Equals(h.Info.InstanceId, c.InstanceId, StringComparison.OrdinalIgnoreCase)));
            }

            if (_mapped.Count > 0)
            {
                var placed = _mapped
                    .Select(s => new PlacedStick(s.Monitor, s.Stick.Edge, s.Stick.From, s.Stick.To, byInfo[s.Host].GetPixels(s.Stick.Channel).Length))
                    .ToList();
                _chain = new StickChain(_mapped.Select(s => (byInfo[s.Host], s.Stick.Channel)), SetupLayout.Positions(placed));
                StickCount = _mapped.Count;
                _layout = [.. _mapped.Select(s => new LayoutStick(s.Stick.Host, byInfo[s.Host], s.Stick.Channel, s.Monitor, s.Stick.Edge, s.Stick.From, s.Stick.To))];
                _layoutMonitors = [.. _mapped.Select(static s => s.Monitor).DistinctBy(static m => m.DeviceName, StringComparer.OrdinalIgnoreCase)];
            }
            else
            {
                // Without a usable mapping, lamp modes still work: every channel, in order.
                if (config is not null)
                {
                    _problems.Add("None of the sticks in config.json can be used, so lamp modes light every channel in order.");
                }

                _chain = new StickChain(hosts.SelectMany(static h =>
                    Enumerable.Range(1, MoteProtocol.ChannelCount).Select(channel => (h.Host, channel))));
                StickCount = hosts.Count * MoteProtocol.ChannelCount;
                _layout = [.. hosts.SelectMany(static h => Enumerable.Range(1, MoteProtocol.ChannelCount)
                    .Select(channel => new LayoutStick(h.Info.PortName, h.Host, channel, null, ScreenEdge.Top, 0, 1)))];
                _layoutMonitors = [];
            }
        }

        public int HostCount => _hosts.Count;

        /// <summary>A copy of what every stick is set to, for the window to draw.</summary>
        public LightingPreview Snapshot() => new(
            _layoutMonitors,
            [.. _layout.Select(static s => new PreviewStick(s.HostId, s.Channel, s.Monitor, s.Edge, s.From, s.To, s.Host.GetPixels(s.Channel).ToArray()))]);

        public int StickCount { get; }

        /// <summary>True if the config lists a host that was not plugged in when the session opened.</summary>
        public bool MissingHosts { get; }

        public (bool Animated, EngineStatus Status) Update(LightingSettings settings, double now)
        {
            double elapsed = double.IsNaN(_last) ? 0 : Math.Clamp(now - _last, 0, 1);
            _last = now;
            foreach (var (_, host) in _hosts)
            {
                if (!host.IsOpen)
                {
                    throw new IOException("A Mote host was unplugged.");
                }
            }

            var previous = _shown;
            _shown = settings;
            bool resend = previous is null
                || previous.Mode != settings.Mode
                || previous.Brightness != settings.Brightness
                || previous.MaxMilliamps != settings.MaxMilliamps;
            if (resend)
            {
                foreach (var (_, host) in _hosts)
                {
                    host.Brightness = settings.LedBrightness;
                    host.Dither = true;
                    host.MaxMilliamps = settings.MaxMilliamps;
                }
            }

            if (settings.Mode != LightingMode.Ambient)
            {
                CloseAmbient();
            }

            switch (settings.Mode)
            {
                case LightingMode.Light or LightingMode.Colour:
                    var colour = settings.SteadyColour!.Value;
                    if (resend || previous!.SteadyColour != colour)
                    {
                        _chain.Show(new SolidEffect(colour), 0);
                    }

                    return (false, Running());

                case LightingMode.Effect:
                    if (_effect is null || resend || previous!.Effect != settings.Effect || previous.Colour != settings.Colour)
                    {
                        _effect = settings.CreateEffect();
                    }

                    _effectTime += elapsed * settings.EffectSpeed;
                    _chain.Show(_effect, _effectTime);
                    return (true, Running());

                default:
                    return UpdateAmbient(settings, previous, resend, elapsed);
            }
        }

        public void Close(bool turnOff)
        {
            CloseAmbient();
            foreach (var (_, host) in _hosts)
            {
                try
                {
                    if (turnOff)
                    {
                        host.Off();
                    }
                }
                catch (Exception ex) when (ex is IOException or TimeoutException or InvalidOperationException or UnauthorizedAccessException)
                {
                    // The host may have been unplugged; there is nothing left to turn off.
                }

                host.Dispose();
            }
        }

        private (bool Animated, EngineStatus Status) UpdateAmbient(LightingSettings settings, LightingSettings? previous, bool resend, double elapsed)
        {
            if (_mirror is null && resend)
            {
                OpenAmbient(settings);
            }

            if (_mirror is null)
            {
                if (resend)
                {
                    foreach (var (_, host) in _hosts)
                    {
                        host.Off();
                    }
                }

                string message = !_hasConfig
                    ? $"Ambient mode needs the stick mapping. Copy config.example.json to {AmbiMoteConfig.DefaultPath} and fill it in."
                    : _mapped.Count == 0
                        ? "Ambient mode is off: none of the sticks in config.json can be used."
                        : "Ambient mode is off: none of the monitors in config.json can be captured.";
                return (false, new EngineStatus(EngineState.Error, message, [.. _problems, .. _ambientProblems]));
            }

            if (previous is not null && (previous.Gamma != settings.Gamma || previous.Saturation != settings.Saturation))
            {
                _mirror.Processor = new ColourProcessor(settings.Gamma, settings.Saturation);
            }

            _mirror.SmoothingSeconds = settings.Smoothing;
            var update = _mirror.Update(elapsed);
            if (resend && update.Sent == 0)
            {
                foreach (var (_, host) in _hosts)
                {
                    host.Show();
                }
            }

            return (true, Running());
        }

        private void OpenAmbient(LightingSettings settings)
        {
            _ambientProblems.Clear();
            var byInfo = _hosts.ToDictionary(static h => h.Info, static h => h.Host);
            var monitors = new List<AmbientMonitor>();
            foreach (var group in _mapped.GroupBy(static s => s.Monitor.DeviceName, StringComparer.OrdinalIgnoreCase))
            {
                IScreenCapture capture;
                try
                {
                    capture = _hardware.OpenCapture(group.Key);
                }
                catch (Exception ex) when (ex is ArgumentException or NotSupportedException or SharpGenException)
                {
                    _ambientProblems.Add($"Could not capture {group.Key}, so its sticks stay off: {ex.Message}");
                    continue;
                }

                _captures.Add(capture);
                monitors.Add(new AmbientMonitor(
                    capture,
                    [.. group.Select(s => new AmbientStick(byInfo[s.Host], s.Stick.Channel, s.Stick.Edge, s.Stick.From, s.Stick.To))]));
            }

            if (monitors.Count > 0)
            {
                // Start dark rather than with the last mode's colours on sticks whose monitor is not captured.
                foreach (var (_, host) in _hosts)
                {
                    host.Clear();
                }

                _mirror = new AmbientMirror(monitors, settings.Smoothing, processor: new ColourProcessor(settings.Gamma, settings.Saturation));
            }
        }

        /// <summary>Releases the screen captures, so nothing is captured while another mode runs.</summary>
        private void CloseAmbient()
        {
            foreach (var capture in _captures)
            {
                capture.Dispose();
            }

            _captures.Clear();
            _mirror = null;
        }

        private EngineStatus Running()
        {
            string where = _mapped.Count > 0 ? "" : " (in channel order: no stick mapping)";
            return new EngineStatus(
                EngineState.Running,
                $"{Count(StickCount, "stick")} on {Count(HostCount, "Mote host")}{where}",
                [.. _problems, .. _ambientProblems]);
        }
    }
}
