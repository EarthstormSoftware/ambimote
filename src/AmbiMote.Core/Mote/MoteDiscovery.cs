using System.IO.Ports;

using Microsoft.Win32;

namespace AmbiMote.Core.Mote;

/// <summary>A Mote host plugged into this PC.</summary>
/// <param name="PortName">The serial port, such as <c>COM5</c>. Can change between boots.</param>
/// <param name="InstanceId">The Windows device instance ID.</param>
/// <param name="SerialNumber">The USB serial number, when the host reports one. Stable, so use it to tell hosts apart.</param>
public sealed record MoteDeviceInfo(string PortName, string InstanceId, string? SerialNumber);

/// <summary>Finds Mote hosts by their USB vendor and product IDs.</summary>
public static class MoteDiscovery
{
    private const string UsbEnumKey = @"SYSTEM\CurrentControlSet\Enum\USB";

    /// <summary>The Mote hosts that are plugged in now, ordered by port name.</summary>
    public static IReadOnlyList<MoteDeviceInfo> FindHosts()
    {
        using var usb = Registry.LocalMachine.OpenSubKey(UsbEnumKey);
        if (usb is null)
        {
            return [];
        }

        var entries = new List<(string DeviceKey, string InstanceId, string? PortName)>();
        foreach (string deviceKey in usb.GetSubKeyNames())
        {
            if (!IsMoteDeviceKey(deviceKey))
            {
                continue;
            }

            using var device = usb.OpenSubKey(deviceKey);
            if (device is null)
            {
                continue;
            }

            foreach (string instanceId in device.GetSubKeyNames())
            {
                using var parameters = device.OpenSubKey($@"{instanceId}\Device Parameters");
                entries.Add((deviceKey, instanceId, parameters?.GetValue("PortName") as string));
            }
        }

        return Select(entries, SerialPort.GetPortNames());
    }

    /// <summary>
    /// Picks the entries whose port is present. Windows keeps registry entries for devices that
    /// were unplugged, so the port list is what says a host is connected.
    /// </summary>
    internal static IReadOnlyList<MoteDeviceInfo> Select(
        IEnumerable<(string DeviceKey, string InstanceId, string? PortName)> entries,
        IEnumerable<string> presentPorts)
    {
        var present = new HashSet<string>(presentPorts, StringComparer.OrdinalIgnoreCase);
        return entries
            .Where(e => e.PortName is not null && present.Contains(e.PortName))
            .Select(e => new MoteDeviceInfo(
                e.PortName!,
                $@"USB\{e.DeviceKey}\{e.InstanceId}",
                IsComposite(e.DeviceKey) ? null : SerialNumberFromInstanceId(e.InstanceId)))
            .OrderBy(d => d.PortName.Length)
            .ThenBy(d => d.PortName, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    internal static bool IsMoteDeviceKey(string deviceKey) =>
        deviceKey.StartsWith(
            $"VID_{MoteProtocol.UsbVendorId:X4}&PID_{MoteProtocol.UsbProductId:X4}", StringComparison.OrdinalIgnoreCase);

    /// <summary>
    /// For a device with a USB serial number, Windows uses the serial number as the instance ID.
    /// Without one it makes up an ID from the port location, which always contains '&amp;'.
    /// </summary>
    internal static string? SerialNumberFromInstanceId(string instanceId) =>
        string.IsNullOrEmpty(instanceId) || instanceId.Contains('&') ? null : instanceId;

    private static bool IsComposite(string deviceKey) => deviceKey.Contains("&MI_", StringComparison.OrdinalIgnoreCase);
}
