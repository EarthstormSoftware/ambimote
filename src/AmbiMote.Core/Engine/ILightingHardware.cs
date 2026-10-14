using AmbiMote.Core.Capture;
using AmbiMote.Core.Config;
using AmbiMote.Core.Mote;

namespace AmbiMote.Core.Engine;

/// <summary>What the <see cref="LightingEngine"/> needs from the PC, so tests can fake it.</summary>
public interface ILightingHardware
{
    /// <summary>The stick mapping, or null if there is none.</summary>
    /// <exception cref="InvalidDataException">The config file is not valid.</exception>
    AmbiMoteConfig? LoadConfig();

    IReadOnlyList<MoteDeviceInfo> FindHosts();

    MoteHost OpenHost(MoteDeviceInfo info);

    IReadOnlyList<DisplayOutput> ListMonitors();

    IScreenCapture OpenCapture(string deviceName);
}

/// <summary>The real hardware: Mote hosts on serial ports, Desktop Duplication and the per-user config file.</summary>
public sealed class WindowsLightingHardware : ILightingHardware
{
    public AmbiMoteConfig? LoadConfig() => AmbiMoteConfig.Load(AmbiMoteConfig.DefaultPath);

    public IReadOnlyList<MoteDeviceInfo> FindHosts() => MoteDiscovery.FindHosts();

    public MoteHost OpenHost(MoteDeviceInfo info) => MoteHost.Open(info.PortName);

    public IReadOnlyList<DisplayOutput> ListMonitors() => DisplayOutput.List();

    public IScreenCapture OpenCapture(string deviceName) => DesktopDuplicationCapture.Open(deviceName);
}
