using AmbiMote.Core.Capture;
using AmbiMote.Core.Mote;
using AmbiMote.Core.Sampling;

namespace AmbiMote.Core.Tests;

public class StickMirrorTests
{
    private sealed class QueuedCapture : IScreenCapture
    {
        public Queue<BgraImage?> Frames { get; } = new();

        public bool TryCapture(TimeSpan timeout, out BgraImage? image)
        {
            image = Frames.Count > 0 ? Frames.Dequeue() : null;
            return image is not null;
        }

        public void Dispose()
        {
        }
    }

    private sealed class RecordingTransport : IMoteTransport
    {
        public List<byte[]> Writes { get; } = [];

        public bool IsOpen { get; set; } = true;

        public void Write(ReadOnlySpan<byte> data) => Writes.Add(data.ToArray());

        public void Dispose()
        {
        }
    }

    private static BgraImage Solid(Rgb colour)
    {
        var image = new BgraImage(4, 4);
        for (int y = 0; y < 4; y++)
        {
            for (int x = 0; x < 4; x++)
            {
                image.SetPixel(x, y, colour);
            }
        }

        return image;
    }

    private static (StickMirror Mirror, QueuedCapture Capture, RecordingTransport Transport, MoteHost Host) Create()
    {
        var capture = new QueuedCapture();
        var transport = new RecordingTransport();
        var host = new MoteHost(transport) { MaxMilliamps = null };
        host.ConfigureSticks();
        transport.Writes.Clear();
        return (new StickMirror(capture, host, 2, ScreenEdge.Top), capture, transport, host);
    }

    [Fact]
    public void Update_NoNewFrame_SendsNothing()
    {
        var (mirror, _, transport, _) = Create();

        Assert.Equal(MirrorUpdate.NoNewFrame, mirror.Update(TimeSpan.Zero));
        Assert.Empty(transport.Writes);
    }

    [Fact]
    public void Update_NewFrame_LightsOnlyItsChannel()
    {
        var (mirror, capture, transport, host) = Create();
        capture.Frames.Enqueue(Solid(new Rgb(10, 20, 30)));

        Assert.Equal(MirrorUpdate.Sent, mirror.Update(TimeSpan.Zero));

        Assert.Single(transport.Writes);
        Assert.All(host.GetPixels(2).ToArray(), static p => Assert.Equal(new Rgb(10, 20, 30), p));
        Assert.All(host.GetPixels(1).ToArray(), static p => Assert.Equal(Rgb.Black, p));
    }

    [Fact]
    public void Update_SameColoursAgain_SendsNothing()
    {
        var (mirror, capture, transport, _) = Create();
        capture.Frames.Enqueue(Solid(new Rgb(10, 20, 30)));
        capture.Frames.Enqueue(Solid(new Rgb(10, 20, 30)));
        capture.Frames.Enqueue(Solid(new Rgb(40, 50, 60)));

        Assert.Equal(MirrorUpdate.Sent, mirror.Update(TimeSpan.Zero));
        Assert.Equal(MirrorUpdate.Unchanged, mirror.Update(TimeSpan.Zero));
        Assert.Equal(MirrorUpdate.Sent, mirror.Update(TimeSpan.Zero));

        Assert.Equal(2, transport.Writes.Count);
    }

    [Fact]
    public void Update_FirstFrameBlack_IsStillSent()
    {
        var (mirror, capture, transport, _) = Create();
        capture.Frames.Enqueue(Solid(Rgb.Black));

        Assert.Equal(MirrorUpdate.Sent, mirror.Update(TimeSpan.Zero));
        Assert.Single(transport.Writes);
    }
}
