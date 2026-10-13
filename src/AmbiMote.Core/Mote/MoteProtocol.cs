namespace AmbiMote.Core.Mote;

/// <summary>
/// The serial protocol spoken by the Pimoroni Mote USB host, as implemented by Pimoroni's
/// Python library (https://github.com/pimoroni/mote).
/// </summary>
/// <remarks>
/// Every command starts with the ASCII header <c>mote</c>, then a command byte:
/// <list type="bullet">
/// <item><c>c</c> configures a channel: channel number (1-4), pixel count, flags (bit 0 = gamma correction on the host).</item>
/// <item><c>o</c> outputs pixels: blue, green, red for every pixel of every configured channel, in channel order.
/// Unconfigured channels send nothing.</item>
/// </list>
/// </remarks>
public static class MoteProtocol
{
    public const int UsbVendorId = 0x16D0;

    public const int UsbProductId = 0x08C4;

    public const int BaudRate = 115200;

    public const int ChannelCount = 4;

    /// <summary>The host supports 512 pixels across its four channels.</summary>
    public const int MaxPixelsPerChannel = 128;

    /// <summary>Pixels on one Mote stick.</summary>
    public const int PixelsPerStick = 16;

    private static ReadOnlySpan<byte> Header => "mote"u8;

    private const byte ConfigureCommand = (byte)'c';

    private const byte OutputCommand = (byte)'o';

    public static byte[] ConfigureChannel(int channel, int pixelCount, bool gammaCorrection)
    {
        ValidateChannel(channel);
        ValidatePixelCount(pixelCount);

        var buffer = new byte[Header.Length + 4];
        Header.CopyTo(buffer);
        buffer[Header.Length] = ConfigureCommand;
        buffer[Header.Length + 1] = (byte)channel;
        buffer[Header.Length + 2] = (byte)pixelCount;
        buffer[Header.Length + 3] = gammaCorrection ? (byte)1 : (byte)0;
        return buffer;
    }

    /// <summary>Builds an output command. <paramref name="channels"/>[0] is channel 1; null means unconfigured.</summary>
    public static byte[] Output(IReadOnlyList<Rgb[]?> channels)
    {
        ArgumentNullException.ThrowIfNull(channels);
        if (channels.Count > ChannelCount)
        {
            throw new ArgumentException($"The Mote host has {ChannelCount} channels.", nameof(channels));
        }

        int pixelCount = channels.Sum(static c => c?.Length ?? 0);
        var buffer = new byte[Header.Length + 1 + (pixelCount * 3)];
        Header.CopyTo(buffer);
        buffer[Header.Length] = OutputCommand;

        int offset = Header.Length + 1;
        foreach (var pixels in channels)
        {
            if (pixels is null)
            {
                continue;
            }

            foreach (var pixel in pixels)
            {
                buffer[offset++] = pixel.B;
                buffer[offset++] = pixel.G;
                buffer[offset++] = pixel.R;
            }
        }

        return buffer;
    }

    public static void ValidateChannel(int channel)
    {
        if (channel is < 1 or > ChannelCount)
        {
            throw new ArgumentOutOfRangeException(nameof(channel), channel, $"Channel must be between 1 and {ChannelCount}.");
        }
    }

    private static void ValidatePixelCount(int pixelCount)
    {
        if (pixelCount is < 1 or > MaxPixelsPerChannel)
        {
            throw new ArgumentOutOfRangeException(
                nameof(pixelCount), pixelCount, $"Pixel count must be between 1 and {MaxPixelsPerChannel}.");
        }
    }
}
