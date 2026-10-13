using AmbiMote.Core.Lighting;
using AmbiMote.Core.Mote;

namespace AmbiMote.Core.Tests.Lighting;

public class StickChainTests
{
    private sealed class RecordingTransport : IMoteTransport
    {
        public int Writes { get; private set; }

        public bool IsOpen => true;

        public void Write(ReadOnlySpan<byte> data) => Writes++;

        public void Dispose()
        {
        }
    }

    /// <summary>Lights each pixel with its index in the chain, so the order is easy to check.</summary>
    private sealed class IndexEffect : IEffect
    {
        public void Render(double seconds, ReadOnlySpan<double> positions, Span<Rgb> pixels)
        {
            for (int i = 0; i < pixels.Length; i++)
            {
                pixels[i] = new Rgb((byte)i, 0, 0);
            }
        }
    }

    private static (MoteHost Host, RecordingTransport Transport) CreateHost()
    {
        var transport = new RecordingTransport();
        var host = new MoteHost(transport);
        host.ConfigureSticks();
        return (host, transport);
    }

    [Fact]
    public void Positions_RunEvenlyThroughTheSticksInOrder()
    {
        var (host, _) = CreateHost();

        var chain = new StickChain([(host, 1), (host, 2)]);

        Assert.Equal(32, chain.PixelCount);
        Assert.Equal(0, chain.Positions[0]);
        Assert.Equal(16.0 / 32, chain.Positions[16]);
        Assert.Equal(31.0 / 32, chain.Positions[31]);
    }

    [Fact]
    public void Show_SplitsThePixelsAcrossSticksAndHosts()
    {
        var (a, transportA) = CreateHost();
        var (b, transportB) = CreateHost();
        int writesBeforeA = transportA.Writes, writesBeforeB = transportB.Writes;
        var chain = new StickChain([(a, 3), (b, 1), (a, 1)]);

        chain.Show(new IndexEffect(), 0);

        Assert.Equal(new Rgb(0, 0, 0), a.GetPixels(3)[0]);
        Assert.Equal(new Rgb(15, 0, 0), a.GetPixels(3)[15]);
        Assert.Equal(new Rgb(16, 0, 0), b.GetPixels(1)[0]);
        Assert.Equal(new Rgb(32, 0, 0), a.GetPixels(1)[0]);
        Assert.Equal(new Rgb(47, 0, 0), a.GetPixels(1)[15]);
        Assert.All(a.GetPixels(2).ToArray(), static p => Assert.Equal(Rgb.Black, p));

        // One frame per host, even with two sticks on host A.
        Assert.Equal(1, transportA.Writes - writesBeforeA);
        Assert.Equal(1, transportB.Writes - writesBeforeB);
    }
}
