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

    /// <summary>Off for now, because <see cref="LightingEngine.Paused"/> is set; the mode comes back when it is cleared.</summary>
    Paused,

    /// <summary>Lighting one stick at a time to show which is which. See <see cref="LightingEngine.Identify"/>.</summary>
    Identifying,
}

/// <summary>What the engine is doing, for the window and the tray icon.</summary>
/// <param name="Message">One line for the user.</param>
/// <param name="Problems">Why some sticks cannot be used, such as a host in a different USB port.</param>
public sealed record EngineStatus(EngineState State, string Message, IReadOnlyList<string> Problems)
{
    public static EngineStatus Off { get; } = new(EngineState.Off, "Off", []);

    public static EngineStatus Paused { get; } = new(EngineState.Paused, "Paused while the PC is locked or the screens are off", []);

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

    /// <summary>How long <see cref="Identify"/> lights each stick: long enough to look round the back of the monitors.</summary>
    internal const double IdentifyStepSeconds = 3;

    /// <summary>The least brightness <see cref="Identify"/> uses, so the sticks can be seen even when turned right down.</summary>
    internal const double IdentifyMinBrightness = 0.16;

    private readonly ILightingHardware _hardware;
    private readonly AutoResetEvent _wake = new(false);
    private readonly ManualResetEventSlim _pausedOff = new(false);
    private readonly CancellationTokenSource _stop = new();
    private LightingSettings _settings;
    private EngineStatus _status = EngineStatus.Off;
    private Thread? _thread;
    private Session? _session;
    private double _retryAt = double.NegativeInfinity;
    private volatile bool _reconnect;
    private volatile bool _paused;
    private volatile bool _identifyRequested;
    private volatile bool _stopIdentifying;
    private double _identifyStart = double.NaN;
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

    /// <summary>
    /// Turns the sticks off and closes the Mote hosts while true, such as while the PC is locked, without changing the
    /// settings; the mode comes back when it is false again. Stops <see cref="Identify"/>.
    /// </summary>
    public bool Paused
    {
        get => _paused;
        set
        {
            if (value != _paused)
            {
                _pausedOff.Reset();
            }

            _paused = value;
            _wake.Set();
        }
    }

    /// <summary>
    /// Waits until the sticks are off for <see cref="Paused"/>, for when the PC is about to sleep and will not wait for
    /// the next frame. False if they are not off within <paramref name="timeout"/>, or if not paused.
    /// </summary>
    public bool WaitUntilPaused(TimeSpan timeout) => _paused && _pausedOff.Wait(timeout);

    /// <summary>
    /// Lights every channel of every Mote host in turn for <see cref="IdentifyStepSeconds"/> each, then goes back to
    /// the mode, even if that is off. Starts again from the first stick if it is already running.
    /// </summary>
    public void Identify()
    {
        _identifyRequested = true;
        _wake.Set();
    }

    /// <summary>Stops <see cref="Identify"/> early and goes back to the mode.</summary>
    public void StopIdentifying()
    {
        _stopIdentifying = true;
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
        _pausedOff.Dispose();
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

        if (_stopIdentifying)
        {
            _stopIdentifying = false;
            _identifyRequested = false;
            StopIdentifyingNow();
        }

        if (_paused)
        {
            _identifyRequested = false;
            StopIdentifyingNow();
            TurnOff();
            _pausedOff.Set();
            SetStatus(EngineStatus.Paused);
            return Timeout.InfiniteTimeSpan;
        }

        if (_identifyRequested)
        {
            _identifyRequested = false;
            _identifyStart = now;
        }

        bool identifying = !double.IsNaN(_identifyStart);
        if (settings.Mode == LightingMode.Off && !identifying)
        {
            TurnOff();
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

            if (identifying)
            {
                int step = (int)Math.Floor((now - _identifyStart) / IdentifyStepSeconds);
                if (step < _session.ChannelCount)
                {
                    SetStatus(_session.Identify(step, settings));
                    UpdatePreview();
                    return TimeSpan.FromSeconds(_identifyStart + ((step + 1) * IdentifyStepSeconds) - now);
                }

                StopIdentifyingNow();
                if (settings.Mode == LightingMode.Off)
                {
                    TurnOff();
                    SetStatus(EngineStatus.Off);
                    return Timeout.InfiniteTimeSpan;
                }
            }

            var (animated, status) = _session.Update(settings, now);
            SetStatus(status);
            UpdatePreview();
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

    /// <summary>Turns the sticks off and closes the hosts on purpose, so turning back on opens them without waiting.</summary>
    private void TurnOff()
    {
        CloseSession(turnOff: true);
        _retryAt = double.NegativeInfinity;
    }

    private void UpdatePreview()
    {
        if (_previewEnabled && _session is not null)
        {
            Volatile.Write(ref _preview, _session.Snapshot());
        }
    }

    /// <summary>Ends <see cref="Identify"/>; the session then shows the mode again from scratch.</summary>
    private void StopIdentifyingNow()
    {
        if (!double.IsNaN(_identifyStart))
        {
            _identifyStart = double.NaN;
            _session?.Forget();
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
        private readonly IReadOnlyList<DisplayOutput> _monitors;
        private readonly AmbiMoteConfig? _config;
        private AmbientMirror? _mirror;
        private LightingSettings? _shown;
        private IEffect? _effect;
        private double _effectTime;
        private double _last = double.NaN;
        private bool _clearAll;

        public Session(
            ILightingHardware hardware,
            List<(MoteDeviceInfo Info, MoteHost Host)> hosts,
            AmbiMoteConfig? config,
            IReadOnlyList<DisplayOutput> monitors)
        {
            _hardware = hardware;
            _hosts = hosts;
            _config = config;
            _monitors = monitors;
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

        /// <summary>Every channel on every open host: the number of steps <see cref="Identify"/> takes.</summary>
        public int ChannelCount => _hosts.Count * MoteProtocol.ChannelCount;

        /// <summary>Lights only channel <paramref name="step"/> of all <see cref="ChannelCount"/>, in the identify pattern.</summary>
        public EngineStatus Identify(int step, LightingSettings settings)
        {
            ThrowIfUnplugged();
            CloseAmbient();
            _shown = null;
            var (info, lit) = _hosts[step / MoteProtocol.ChannelCount];
            int channel = (step % MoteProtocol.ChannelCount) + 1;
            foreach (var (_, host) in _hosts)
            {
                host.Brightness = Math.Max(settings.LedBrightness, IdentifyMinBrightness);
                host.MaxMilliamps = settings.MaxMilliamps;
                host.Clear();
            }

            Lighting.Identify.Light(lit.GetPixels(channel), channel);
            foreach (var (_, host) in _hosts)
            {
                host.Show();
            }

            string name = _config?.Hosts.FirstOrDefault(c => string.Equals(c.InstanceId, info.InstanceId, StringComparison.OrdinalIgnoreCase)) is { } named
                ? $"Host \"{named.Id}\" ({info.PortName})"
                : $"Host {info.PortName}";
            string where = _mapped.FirstOrDefault(s => s.Host == info && s.Stick.Channel == channel) is { } mapped
                ? $"mapped to the {mapped.Stick.Edge.ToString().ToLowerInvariant()} of {mapped.Monitor.DeviceName.TrimStart('\\', '.')}"
                : "not in the stick mapping";
            return new EngineStatus(
                EngineState.Identifying,
                $"{step + 1} of {ChannelCount}: {name}, channel {channel} ({Lighting.Identify.ChannelColourName(channel)}), {where}",
                [.. _problems]);
        }

        /// <summary>Forgets what was shown, so the next <see cref="Update"/> sends the mode again from scratch.</summary>
        public void Forget()
        {
            CloseAmbient();
            _shown = null;
            _effect = null;
            _clearAll = true;
        }

        public (bool Animated, EngineStatus Status) Update(LightingSettings settings, double now)
        {
            double elapsed = double.IsNaN(_last) ? 0 : Math.Clamp(now - _last, 0, 1);
            _last = now;
            ThrowIfUnplugged();
            if (_clearAll)
            {
                // Modes only draw the mapped sticks, so blank the rest, which may still show what Identify left.
                _clearAll = false;
                foreach (var (_, host) in _hosts)
                {
                    host.Clear();
                    host.Show();
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

        private void ThrowIfUnplugged()
        {
            foreach (var (_, host) in _hosts)
            {
                if (!host.IsOpen)
                {
                    throw new IOException("A Mote host was unplugged.");
                }
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
            if (previous is not null
                && (!string.Equals(previous.AmbientSource, settings.AmbientSource, StringComparison.OrdinalIgnoreCase)
                    || previous.OuterSticks != settings.OuterSticks))
            {
                // Different monitors to capture: start again.
                CloseAmbient();
                resend = true;
            }

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
            _mirror.Depth = settings.Depth;
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
            var monitors = new List<AmbientMonitor>();
            foreach (var group in AmbientSticks(settings).GroupBy(static s => s.Capture, StringComparer.OrdinalIgnoreCase))
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
                monitors.Add(new AmbientMonitor(capture, [.. group.Select(static s => s.Stick)]));
            }

            if (monitors.Count > 0)
            {
                // Start dark rather than with the last mode's colours on sticks whose monitor is not captured.
                var mirrored = monitors.SelectMany(static m => m.Sticks).Select(static s => s.Host).ToHashSet();
                foreach (var (_, host) in _hosts)
                {
                    host.Clear();
                    if (!mirrored.Contains(host))
                    {
                        // The mirror never sends to this host, so turn it dark now.
                        host.Show();
                    }
                }

                _mirror = new AmbientMirror(monitors, settings.Smoothing, settings.Depth, processor: new ColourProcessor(settings.Gamma, settings.Saturation));
            }
        }

        /// <summary>
        /// The sticks ambient mode lights, each with the monitor to capture for it. With an
        /// <see cref="LightingSettings.AmbientSource"/>, everything comes from that monitor: sticks on monitors to its left
        /// or right are extended from it or left dark, and sticks on monitors above or below it stay dark.
        /// </summary>
        private IEnumerable<(string Capture, AmbientStick Stick)> AmbientSticks(LightingSettings settings)
        {
            var byInfo = _hosts.ToDictionary(static h => h.Info, static h => h.Host);
            DisplayOutput? source = null;
            if (settings.AmbientSource is { } name)
            {
                source = _monitors.FirstOrDefault(m => string.Equals(m.DeviceName, name, StringComparison.OrdinalIgnoreCase));
                if (source is null)
                {
                    _ambientProblems.Add($"The ambient source monitor {name} is not connected, so each stick follows its own monitor.");
                }
            }

            foreach (var s in _mapped)
            {
                var host = byInfo[s.Host];
                if (source is null || string.Equals(s.Monitor.DeviceName, source.DeviceName, StringComparison.OrdinalIgnoreCase))
                {
                    yield return (s.Monitor.DeviceName, new AmbientStick(host, s.Stick.Channel, s.Stick.Edge, s.Stick.From, s.Stick.To));
                    continue;
                }

                ScreenEdge? side = s.Monitor.Left + s.Monitor.Width <= source.Left ? ScreenEdge.Left
                    : s.Monitor.Left >= source.Left + source.Width ? ScreenEdge.Right
                    : null;
                if (settings.OuterSticks == OuterSticks.Extend && side is not null)
                {
                    yield return (source.DeviceName, new AmbientStick(host, s.Stick.Channel, s.Stick.Edge, s.Stick.From, s.Stick.To, side));
                }
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
