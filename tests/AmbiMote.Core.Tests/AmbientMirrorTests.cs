using AmbiMote.Core.Capture;
using AmbiMote.Core.Mote;
using AmbiMote.Core.Processing;
using AmbiMote.Core.Sampling;

namespace AmbiMote.Core.Tests;

public class AmbientMirrorTests
{
    private sealed class QueuedCapture : IScreenCapture
    {
        public Queue<BgraImage> Frames { get; } = new();

        public bool TryCapture(TimeSpan timeout, out BgraImage? image)
        {
            image = Frames.Count > 0 ? Frames.Dequeue() : null;
            return image is not null;
        }

        public void Dispose()
        {
        }
    }

    private sealed class CountingTransport : IMoteTransport
    {
        public int Writes { get; private set; }

        public bool IsOpen => true;

        public void Write(ReadOnlySpan<byte> data) => Writes++;

        public void Dispose()
        {
        }
    }

    /// <summary>An image whose left half is one colour and right half another.</summary>
    private static BgraImage Halves(Rgb left, Rgb right)
    {
        var image = new BgraImage(8, 8);
        for (int y = 0; y < 8; y++)
        {
            for (int x = 0; x < 8; x++)
            {
                image.SetPixel(x, y, x < 4 ? left : right);
            }
        }

        return image;
    }

    private static (MoteHost Host, CountingTransport Transport) CreateHost()
    {
        var transport = new CountingTransport();
        var host = new MoteHost(transport) { MaxMilliamps = null };
        host.ConfigureSticks();
        return (host, transport);
    }

    [Fact]
    public void Update_WithoutSmoothing_LightsEachStickFromItsPartOfTheEdge()
    {
        var (host, _) = CreateHost();
        var capture = new QueuedCapture();
        capture.Frames.Enqueue(Halves(new Rgb(200, 0, 0), new Rgb(0, 0, 200)));
        var mirror = new AmbientMirror(
            [new AmbientMonitor(capture, [new AmbientStick(host, 1, ScreenEdge.Top, 0.5, 0), new AmbientStick(host, 2, ScreenEdge.Top, 0.5, 1)])],
            smoothingSeconds: 0);

        var update = mirror.Update(1.0 / 30);

        Assert.Equal(new AmbientUpdate(1, 1), update);
        Assert.All(host.GetPixels(1).ToArray(), static p => Assert.Equal(new Rgb(200, 0, 0), p));
        Assert.All(host.GetPixels(2).ToArray(), static p => Assert.Equal(new Rgb(0, 0, 200), p));
        Assert.All(host.GetPixels(3).ToArray(), static p => Assert.Equal(Rgb.Black, p));
    }

    [Fact]
    public void Update_WithSmoothing_FadesTowardsTheScreen()
    {
        var (host, _) = CreateHost();
        var capture = new QueuedCapture();
        capture.Frames.Enqueue(Halves(new Rgb(200, 200, 200), new Rgb(200, 200, 200)));
        var mirror = new AmbientMirror([new AmbientMonitor(capture, [new AmbientStick(host, 1, ScreenEdge.Top, 0, 1)])], smoothingSeconds: 0.1);

        mirror.Update(0.1);
        byte afterOneStep = host.GetPixels(1)[0].R;
        mirror.Update(0.1);
        byte afterTwoSteps = host.GetPixels(1)[0].R;
        for (int i = 0; i < 30; i++)
        {
            mirror.Update(0.1);
        }

        // After one time constant, about 63% of the way there.
        Assert.InRange(afterOneStep, 120, 135);
        Assert.InRange(afterTwoSteps, afterOneStep + 1, 199);
        Assert.Equal(new Rgb(200, 200, 200), host.GetPixels(1)[0]);
    }

    [Fact]
    public void Update_NothingChanged_SendsNothing()
    {
        var (host, transport) = CreateHost();
        var capture = new QueuedCapture();
        capture.Frames.Enqueue(Halves(new Rgb(10, 20, 30), new Rgb(10, 20, 30)));
        capture.Frames.Enqueue(Halves(new Rgb(10, 20, 30), new Rgb(10, 20, 30)));
        var mirror = new AmbientMirror([new AmbientMonitor(capture, [new AmbientStick(host, 1, ScreenEdge.Top, 0, 1)])], smoothingSeconds: 0);
        int before = transport.Writes;

        Assert.Equal(new AmbientUpdate(1, 1), mirror.Update(0.03));
        Assert.Equal(new AmbientUpdate(1, 0), mirror.Update(0.03));
        Assert.Equal(new AmbientUpdate(0, 0), mirror.Update(0.03));
        Assert.Equal(1, transport.Writes - before);
    }

    [Fact]
    public void Update_BlackScreen_IsStillSentOnce()
    {
        var (host, transport) = CreateHost();
        var capture = new QueuedCapture();
        capture.Frames.Enqueue(Halves(Rgb.Black, Rgb.Black));
        var mirror = new AmbientMirror([new AmbientMonitor(capture, [new AmbientStick(host, 1, ScreenEdge.Top, 0, 1)])]);
        int before = transport.Writes;

        Assert.Equal(1, mirror.Update(0.03).Sent);
        Assert.Equal(0, mirror.Update(0.03).Sent);
        Assert.Equal(1, transport.Writes - before);
    }

    [Fact]
    public void Update_TwoMonitorsTwoHosts_OnlySendsTheHostWhoseColoursChanged()
    {
        var (a, transportA) = CreateHost();
        var (b, transportB) = CreateHost();
        var left = new QueuedCapture();
        var right = new QueuedCapture();
        left.Frames.Enqueue(Halves(new Rgb(50, 0, 0), new Rgb(50, 0, 0)));
        right.Frames.Enqueue(Halves(new Rgb(0, 50, 0), new Rgb(0, 50, 0)));
        var mirror = new AmbientMirror(
        [
            new AmbientMonitor(left, [new AmbientStick(a, 4, ScreenEdge.Left, 0, 1)]),
            new AmbientMonitor(right, [new AmbientStick(b, 4, ScreenEdge.Right, 0, 1)]),
        ],
        smoothingSeconds: 0);
        mirror.Update(0.03);
        int beforeA = transportA.Writes, beforeB = transportB.Writes;

        right.Frames.Enqueue(Halves(new Rgb(0, 90, 0), new Rgb(0, 90, 0)));
        var update = mirror.Update(0.03);

        Assert.Equal(new AmbientUpdate(1, 1), update);
        Assert.Equal(0, transportA.Writes - beforeA);
        Assert.Equal(1, transportB.Writes - beforeB);
        Assert.Equal(new Rgb(0, 90, 0), b.GetPixels(4)[0]);
        Assert.Equal(new Rgb(50, 0, 0), a.GetPixels(4)[0]);
    }

    [Fact]
    public void Update_AppliesTheColourProcessor()
    {
        var (host, _) = CreateHost();
        var capture = new QueuedCapture();
        capture.Frames.Enqueue(Halves(new Rgb(128, 128, 128), new Rgb(128, 128, 128)));
        var mirror = new AmbientMirror(
            [new AmbientMonitor(capture, [new AmbientStick(host, 1, ScreenEdge.Top, 0, 1)])],
            smoothingSeconds: 0,
            processor: new ColourProcessor(gamma: 2.2, saturation: 1));

        mirror.Update(0.03);

        Assert.Equal(new Rgb(56, 56, 56), host.GetPixels(1)[0]);
    }

    [Fact]
    public void Processor_Changed_AppliesWithoutANewFrame()
    {
        var (host, transport) = CreateHost();
        var capture = new QueuedCapture();
        capture.Frames.Enqueue(Halves(new Rgb(128, 128, 128), new Rgb(128, 128, 128)));
        var mirror = new AmbientMirror([new AmbientMonitor(capture, [new AmbientStick(host, 1, ScreenEdge.Top, 0, 1)])], smoothingSeconds: 0);
        mirror.Update(0.03);
        int before = transport.Writes;

        mirror.Processor = new ColourProcessor(gamma: 2.2, saturation: 1);
        var update = mirror.Update(0.03);

        Assert.Equal(new AmbientUpdate(0, 1), update);
        Assert.Equal(1, transport.Writes - before);
        Assert.Equal(new Rgb(56, 56, 56), host.GetPixels(1)[0]);
    }

    [Fact]
    public void Constructor_RejectsBadSettings()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => new AmbientMirror([], smoothingSeconds: -1));
        Assert.Throws<ArgumentOutOfRangeException>(() => new AmbientMirror([], depth: 0));
        Assert.Throws<ArgumentOutOfRangeException>(() => new AmbientMirror([]).Depth = 1.5);
    }

    [Fact]
    public void Depth_SetsHowFarIntoTheScreenColoursComeFrom_FromTheNextFrame()
    {
        // The top two rows are grey and the rest black.
        static BgraImage TopRowsGrey()
        {
            var image = new BgraImage(8, 8);
            for (int y = 0; y < 8; y++)
            {
                for (int x = 0; x < 8; x++)
                {
                    image.SetPixel(x, y, y < 2 ? new Rgb(200, 200, 200) : Rgb.Black);
                }
            }

            return image;
        }

        var (host, _) = CreateHost();
        var capture = new QueuedCapture();
        capture.Frames.Enqueue(TopRowsGrey());
        capture.Frames.Enqueue(TopRowsGrey());
        var mirror = new AmbientMirror([new AmbientMonitor(capture, [new AmbientStick(host, 1, ScreenEdge.Top, 0, 1)])], smoothingSeconds: 0, depth: 0.25);

        mirror.Update(1.0 / 30);
        var shallow = host.GetPixels(1)[0];
        mirror.Depth = 0.5;
        mirror.Update(1.0 / 30);

        Assert.Equal(new Rgb(200, 200, 200), shallow);
        Assert.Equal(new Rgb(100, 100, 100), host.GetPixels(1)[0]);
    }
}
