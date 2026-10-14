using AmbiMote.Core.Capture;
using AmbiMote.Core.Mote;
using AmbiMote.Core.Processing;
using AmbiMote.Core.Sampling;

namespace AmbiMote.Core;

/// <summary>A stick lit from part of one edge of a monitor.</summary>
/// <param name="From">Where pixel 0 sits along the edge, from 0 to 1 (left to right, or top to bottom).</param>
/// <param name="To">Where the last pixel sits along the edge.</param>
public sealed record AmbientStick(MoteHost Host, int Channel, ScreenEdge Edge, double From, double To);

/// <summary>One monitor's capture and the sticks behind it.</summary>
public sealed record AmbientMonitor(IScreenCapture Capture, IReadOnlyList<AmbientStick> Sticks);

/// <summary>What one <see cref="AmbientMirror.Update"/> did.</summary>
/// <param name="Captured">Monitors that had a new frame.</param>
/// <param name="Sent">Hosts that were sent new colours.</param>
public readonly record struct AmbientUpdate(int Captured, int Sent);

/// <summary>
/// Lights every stick from the edge of the monitor it sits behind. Colours fade towards the screen's
/// over <see cref="SmoothingSeconds"/> so they do not flicker, and a host is only sent a frame when its colours change.
/// </summary>
public sealed class AmbientMirror
{
    private readonly (IScreenCapture Capture, StickState[] Sticks)[] _monitors;
    private readonly MoteHost[] _hosts;
    private readonly HashSet<MoteHost> _shown = [];
    private ColourProcessor _processor;
    private double _smoothingSeconds;
    private bool _reprocess;

    /// <param name="processor">Gamma and saturation applied to the sampled colours. Default: none, colours are sent as sampled.</param>
    public AmbientMirror(IReadOnlyList<AmbientMonitor> monitors, double smoothingSeconds = 0.15, double depth = 0.1, ColourProcessor? processor = null)
    {
        ArgumentNullException.ThrowIfNull(monitors);
        ArgumentOutOfRangeException.ThrowIfNegative(smoothingSeconds);
        if (depth is <= 0 or > 1)
        {
            throw new ArgumentOutOfRangeException(nameof(depth), depth, "Depth must be more than 0 and at most 1.");
        }

        _monitors = [.. monitors.Select(static m => (m.Capture, m.Sticks.Select(static s => new StickState(s)).ToArray()))];
        _hosts = [.. monitors.SelectMany(static m => m.Sticks).Select(static s => s.Host).Distinct()];
        _processor = processor ?? ColourProcessor.None;
        _smoothingSeconds = smoothingSeconds;
        Depth = depth;
    }

    /// <summary>
    /// How quickly colours follow the screen: the time to get about two thirds of the way to a new colour. 0 follows at once.
    /// </summary>
    public double SmoothingSeconds
    {
        get => _smoothingSeconds;
        set
        {
            ArgumentOutOfRangeException.ThrowIfNegative(value);
            _smoothingSeconds = value;
        }
    }

    /// <summary>How far into the screen each stick samples, as a fraction of its width or height.</summary>
    public double Depth { get; }

    /// <summary>
    /// Applied to the sampled colours before smoothing, so fades happen in LED brightness. A new processor applies
    /// from the next <see cref="Update"/>, without waiting for the screen to change.
    /// </summary>
    public ColourProcessor Processor
    {
        get => _processor;
        set
        {
            ArgumentNullException.ThrowIfNull(value);
            _processor = value;
            _reprocess = true;
        }
    }

    /// <summary>Captures any monitors that changed, moves the colours on by <paramref name="elapsedSeconds"/>, and sends what changed.</summary>
    public AmbientUpdate Update(double elapsedSeconds)
    {
        int captured = 0;
        foreach (var (capture, sticks) in _monitors)
        {
            if (capture.TryCapture(TimeSpan.Zero, out var image) && image is not null)
            {
                captured++;
                foreach (var stick in sticks)
                {
                    EdgeSampler.Sample(image, stick.Config.Edge, stick.Config.From, stick.Config.To, Depth, stick.Sampled);
                    stick.Process(_processor);
                }
            }
            else if (_reprocess)
            {
                foreach (var stick in sticks.Where(static s => s.HasTarget))
                {
                    stick.Process(_processor);
                }
            }
        }

        _reprocess = false;

        double follow = SmoothingSeconds <= 0 ? 1 : 1 - Math.Exp(-Math.Max(0, elapsedSeconds) / SmoothingSeconds);
        var changed = new HashSet<MoteHost>();
        foreach (var (_, sticks) in _monitors)
        {
            foreach (var stick in sticks)
            {
                if (stick.HasTarget && stick.Step(follow))
                {
                    changed.Add(stick.Config.Host);
                }
            }
        }

        int sent = 0;
        foreach (var host in _hosts)
        {
            // Send each host once at the start, even if its colours are still black, so it shows this run's colours.
            if (changed.Contains(host) || (!_shown.Contains(host) && _monitors.Any(m => m.Sticks.Any(s => s.HasTarget && s.Config.Host == host))))
            {
                host.Show();
                _shown.Add(host);
                sent++;
            }
        }

        return new AmbientUpdate(captured, sent);
    }

    private sealed class StickState(AmbientStick config)
    {
        private readonly double[] _current = new double[config.Host.GetPixels(config.Channel).Length * 3];

        public AmbientStick Config { get; } = config;

        /// <summary>The colours sampled from the screen, before processing.</summary>
        public Rgb[] Sampled { get; } = new Rgb[config.Host.GetPixels(config.Channel).Length];

        /// <summary>The processed colours the pixels fade towards.</summary>
        public Rgb[] Target { get; } = new Rgb[config.Host.GetPixels(config.Channel).Length];

        public bool HasTarget { get; private set; }

        public void Process(ColourProcessor processor)
        {
            Sampled.CopyTo(Target, 0);
            processor.Process(Target);
            HasTarget = true;
        }

        /// <summary>Moves each pixel <paramref name="follow"/> of the way to its target and writes it to the host. True if any pixel changed.</summary>
        public bool Step(double follow)
        {
            var pixels = Config.Host.GetPixels(Config.Channel);
            bool changed = false;
            for (int i = 0; i < pixels.Length; i++)
            {
                var target = Target[i];
                var next = new Rgb(
                    Move(ref _current[i * 3], target.R, follow),
                    Move(ref _current[(i * 3) + 1], target.G, follow),
                    Move(ref _current[(i * 3) + 2], target.B, follow));
                if (next != pixels[i])
                {
                    pixels[i] = next;
                    changed = true;
                }
            }

            return changed;
        }

        private static byte Move(ref double current, byte target, double follow)
        {
            current += (target - current) * follow;
            if (Math.Abs(target - current) < 0.5)
            {
                current = target;
            }

            return (byte)Math.Round(current);
        }
    }
}
