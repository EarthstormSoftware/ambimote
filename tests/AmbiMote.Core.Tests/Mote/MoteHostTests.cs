using AmbiMote.Core.Mote;

namespace AmbiMote.Core.Tests.Mote;

public class MoteHostTests
{
    private sealed class RecordingTransport : IMoteTransport
    {
        public List<byte[]> Writes { get; } = [];

        public bool IsOpen { get; set; } = true;

        public bool Disposed { get; private set; }

        public void Write(ReadOnlySpan<byte> data) => Writes.Add(data.ToArray());

        public void Dispose() => Disposed = true;
    }

    /// <summary>The red value of every LED on channel 1 in a frame: output is "mote", 'o', then blue, green, red.</summary>
    private static byte[] Reds(byte[] frame, int count = 16) => [.. Enumerable.Range(0, count).Select(i => frame[5 + (i * 3) + 2])];

    [Fact]
    public void Show_WithDither_SpreadsInBetweenLevelsAcrossTheLeds()
    {
        var transport = new RecordingTransport();
        using var host = new MoteHost(transport) { Brightness = 0.25, MaxMilliamps = null, Dither = true };
        host.ConfigureChannel(1);
        host.Fill(1, new Rgb(6, 0, 0));

        host.Show();
        host.Show();

        // 6 x 0.25 = 1.5: some LEDs at 1 and some at 2, averaging 1.5, and the same every frame so nothing flickers.
        var reds = Reds(transport.Writes[^1]);
        Assert.All(reds, static r => Assert.InRange(r, (byte)1, (byte)2));
        Assert.InRange(reds.Average(static r => r), 1.4, 1.6);
        Assert.Equal(reds, Reds(transport.Writes[^2]));
    }

    [Fact]
    public void Show_WithDither_BrighteningStepsUpOneLedAtATime()
    {
        var transport = new RecordingTransport();
        using var host = new MoteHost(transport) { MaxMilliamps = null, Dither = true };
        host.ConfigureChannel(1);
        host.Fill(1, new Rgb(255, 0, 0));
        int previous = 0;

        // A slow fade from level 1 to 2: the total rises steadily, never jumping all sixteen LEDs at once.
        for (int step = 0; step <= 32; step++)
        {
            host.Brightness = (1 + (step / 32.0)) / 255;
            host.Show();
            int total = Reds(transport.Writes[^1]).Sum(static r => r);
            Assert.InRange(total - previous, step == 0 ? total : 0, step == 0 ? total : 2);
            previous = total;
        }
    }

    [Fact]
    public void IsOpen_FollowsTheTransport()
    {
        var transport = new RecordingTransport();
        using var host = new MoteHost(transport);

        Assert.True(host.IsOpen);
        transport.IsOpen = false;
        Assert.False(host.IsOpen);
    }

    [Fact]
    public void ConfigureSticks_ConfiguresFourChannelsOfSixteen()
    {
        var transport = new RecordingTransport();
        using var host = new MoteHost(transport);

        host.ConfigureSticks();

        Assert.Equal(4, transport.Writes.Count);
        for (int channel = 1; channel <= 4; channel++)
        {
            Assert.Equal(MoteProtocol.ConfigureChannel(channel, 16, false), transport.Writes[channel - 1]);
            Assert.Equal(16, host.GetPixels(channel).Length);
        }
    }

    [Fact]
    public void Show_SendsPixelsWithBrightnessApplied()
    {
        var transport = new RecordingTransport();
        using var host = new MoteHost(transport) { Brightness = 0.5, MaxMilliamps = null };
        host.ConfigureChannel(1, 2);
        host.GetPixels(1)[1] = new Rgb(200, 100, 0);

        host.Show();

        Assert.Equal("moteo"u8.ToArray().Concat(new byte[] { 0, 0, 0, 0, 50, 100 }), transport.Writes[^1]);
    }

    [Fact]
    public void Show_UsesTheDefaultPowerLimit()
    {
        var transport = new RecordingTransport();
        using var host = new MoteHost(transport);
        host.ConfigureSticks();
        host.FillAll(Rgb.White);

        host.Show();

        var sent = transport.Writes[^1].AsSpan(5).ToArray();
        double estimate = sent.Sum(static b => (double)b) / 255.0 * PowerLimit.MilliampsPerComponent;
        Assert.InRange(estimate, 0, MoteHost.DefaultMaxMilliamps);
    }

    [Fact]
    public void Off_SendsAllBlack()
    {
        var transport = new RecordingTransport();
        using var host = new MoteHost(transport);
        host.ConfigureChannel(3, 16);
        host.Fill(3, Rgb.White);

        host.Off();

        Assert.All(transport.Writes[^1].AsSpan(5).ToArray(), static b => Assert.Equal(0, b));
    }

    [Fact]
    public void GetPixels_ThrowsForUnconfiguredChannel()
    {
        using var host = new MoteHost(new RecordingTransport());

        Assert.False(host.IsConfigured(2));
        Assert.Throws<InvalidOperationException>(() => host.GetPixels(2).Length);
    }

    [Fact]
    public void Dispose_ClosesTheTransport()
    {
        var transport = new RecordingTransport();

        new MoteHost(transport).Dispose();

        Assert.True(transport.Disposed);
    }
}
