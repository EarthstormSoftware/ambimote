namespace AmbiMote.Core.Mote;

/// <summary>The byte stream to a Mote host. A serial port in practice; a fake in tests.</summary>
public interface IMoteTransport : IDisposable
{
    void Write(ReadOnlySpan<byte> data);
}
