using AmbiMote.Core.Capture;
using AmbiMote.Core.Mote;
using AmbiMote.Core.Sampling;

namespace AmbiMote.Core;

/// <summary>Drives one stick from one edge of one monitor.</summary>
public sealed class StickMirror
{
    private readonly IScreenCapture _capture;
    private readonly MoteHost _host;
    private readonly Rgb[] _sampled;
    private bool _shown;

    public StickMirror(IScreenCapture capture, MoteHost host, int channel, ScreenEdge edge, double from = 0, double to = 1, double depth = 0.1)
    {
        _capture = capture ?? throw new ArgumentNullException(nameof(capture));
        _host = host ?? throw new ArgumentNullException(nameof(host));
        Channel = channel;
        Edge = edge;
        From = from;
        To = to;
        Depth = depth;
        _sampled = new Rgb[host.GetPixels(channel).Length];
    }

    public int Channel { get; }

    public ScreenEdge Edge { get; }

    public double From { get; }

    public double To { get; }

    public double Depth { get; }

    /// <summary>
    /// Captures the screen and updates the stick. Nothing is sent if the screen or the sampled colours did not change.
    /// </summary>
    public MirrorUpdate Update(TimeSpan timeout)
    {
        if (!_capture.TryCapture(timeout, out var image) || image is null)
        {
            return MirrorUpdate.NoNewFrame;
        }

        EdgeSampler.Sample(image, Edge, From, To, Depth, _sampled);
        var pixels = _host.GetPixels(Channel);
        if (_shown && pixels.SequenceEqual(_sampled))
        {
            return MirrorUpdate.Unchanged;
        }

        _sampled.CopyTo(pixels);
        _host.Show();
        _shown = true;
        return MirrorUpdate.Sent;
    }
}

/// <summary>What one <see cref="StickMirror.Update"/> did.</summary>
public enum MirrorUpdate
{
    /// <summary>The screen had not changed.</summary>
    NoNewFrame,

    /// <summary>The screen changed, but not the colours along the edge, so nothing was sent.</summary>
    Unchanged,

    /// <summary>New colours were sent to the stick.</summary>
    Sent,
}
