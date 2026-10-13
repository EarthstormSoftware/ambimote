namespace AmbiMote.Core.Mote;

/// <summary>The byte stream to a Mote host. A serial port in practice; a fake in tests.</summary>
public interface IMoteTransport : IDisposable
{
    /// <summary>False once the device has gone away, for example when it is unplugged.</summary>
    bool IsOpen { get; }

    void Write(ReadOnlySpan<byte> data);
}
