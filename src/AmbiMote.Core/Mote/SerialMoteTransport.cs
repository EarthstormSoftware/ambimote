using System.IO.Ports;

namespace AmbiMote.Core.Mote;

/// <summary>Talks to a Mote host over its USB serial port.</summary>
public sealed class SerialMoteTransport : IMoteTransport
{
    private readonly SerialPort _port;

    public SerialMoteTransport(string portName)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(portName);
        _port = new SerialPort(portName, MoteProtocol.BaudRate) { WriteTimeout = 1000 };
        _port.Open();
    }

    public string PortName => _port.PortName;

    public void Write(ReadOnlySpan<byte> data) => _port.BaseStream.Write(data);

    public void Dispose() => _port.Dispose();
}
