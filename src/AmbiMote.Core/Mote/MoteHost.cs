namespace AmbiMote.Core.Mote;

/// <summary>
/// One Mote USB host with up to four channels of pixels. Set pixels, then call <see cref="Show"/>.
/// The host keeps showing the last frame after the program exits, so call <see cref="Off"/> to turn the sticks off.
/// </summary>
public sealed class MoteHost(IMoteTransport transport) : IDisposable
{
    /// <summary>A safe default for a host powered from a USB 2.0 port.</summary>
    public const double DefaultMaxMilliamps = 400;

    private readonly IMoteTransport _transport = transport ?? throw new ArgumentNullException(nameof(transport));
    private readonly Rgb[]?[] _channels = new Rgb[]?[MoteProtocol.ChannelCount];

    /// <summary>Overall brightness from 0 to 1, applied when the frame is sent.</summary>
    public double Brightness { get; set; } = 1.0;

    /// <summary>The most current the sticks may draw, in milliamps; null for no limit. See <see cref="PowerLimit"/>.</summary>
    public double? MaxMilliamps { get; set; } = DefaultMaxMilliamps;

    /// <summary>False once the host has been unplugged.</summary>
    public bool IsOpen => _transport.IsOpen;

    public static MoteHost Open(string portName) => new(new SerialMoteTransport(portName));

    /// <summary>Configures a channel and clears its pixels.</summary>
    public void ConfigureChannel(int channel, int pixelCount = MoteProtocol.PixelsPerStick)
    {
        var command = MoteProtocol.ConfigureChannel(channel, pixelCount, gammaCorrection: false);
        _transport.Write(command);
        _channels[channel - 1] = new Rgb[pixelCount];
    }

    /// <summary>Configures all four channels for one stick each.</summary>
    public void ConfigureSticks()
    {
        for (int channel = 1; channel <= MoteProtocol.ChannelCount; channel++)
        {
            ConfigureChannel(channel);
        }
    }

    public bool IsConfigured(int channel)
    {
        MoteProtocol.ValidateChannel(channel);
        return _channels[channel - 1] is not null;
    }

    /// <summary>The pixels of a configured channel, to read or change in place.</summary>
    public Span<Rgb> GetPixels(int channel)
    {
        MoteProtocol.ValidateChannel(channel);
        return _channels[channel - 1]
            ?? throw new InvalidOperationException($"Channel {channel} has not been configured.");
    }

    public void Fill(int channel, Rgb colour) => GetPixels(channel).Fill(colour);

    public void FillAll(Rgb colour)
    {
        foreach (var pixels in _channels)
        {
            pixels?.AsSpan().Fill(colour);
        }
    }

    public void Clear() => FillAll(Rgb.Black);

    /// <summary>Sends the pixels to the sticks, after applying <see cref="Brightness"/> and <see cref="MaxMilliamps"/>.</summary>
    public void Show() => _transport.Write(MoteProtocol.Output(PowerLimit.Apply(_channels, Brightness, MaxMilliamps)));

    /// <summary>Turns every configured stick off.</summary>
    public void Off()
    {
        Clear();
        Show();
    }

    public void Dispose() => _transport.Dispose();
}
