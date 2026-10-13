using System.IO.Ports;

namespace AmbiMote.Core.Mote;

/// <summary>Talks to a Mote host over its USB serial port.</summary>
public sealed class SerialMoteTransport : IMoteTransport
{
    private readonly SerialPort _port;

    public SerialMoteTransport(string portName)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(portName);
        // pyserial, which Pimoroni's library uses, raises DTR and RTS on open. .NET does not by
        // default, and USB serial devices often ignore data until DTR is raised.
        _port = new SerialPort(portName, MoteProtocol.BaudRate)
        {
            WriteTimeout = 1000,
            DtrEnable = true,
            RtsEnable = true,
        };
        _port.Open();
    }

    public string PortName => _port.PortName;

    public bool IsOpen => _port.IsOpen;

    public void Write(ReadOnlySpan<byte> data) => _port.BaseStream.Write(data);

    public void Dispose() => _port.Dispose();
}
