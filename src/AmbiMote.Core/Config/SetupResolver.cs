using AmbiMote.Core.Capture;
using AmbiMote.Core.Mote;

namespace AmbiMote.Core.Config;

/// <summary>A configured stick whose host is plugged in and whose monitor is attached.</summary>
public sealed record ResolvedStick(StickConfig Stick, MoteDeviceInfo Host, DisplayOutput Monitor);

/// <summary>The sticks that can be used now, and why any others cannot.</summary>
public sealed record ResolvedSetup(IReadOnlyList<ResolvedStick> Sticks, IReadOnlyList<string> Problems);

/// <summary>Matches the config to the Mote hosts and monitors that are present.</summary>
public static class SetupResolver
{
    public static ResolvedSetup Resolve(AmbiMoteConfig config, IReadOnlyList<MoteDeviceInfo> hosts, IReadOnlyList<DisplayOutput> monitors)
    {
        ArgumentNullException.ThrowIfNull(config);
        ArgumentNullException.ThrowIfNull(hosts);
        ArgumentNullException.ThrowIfNull(monitors);

        var problems = new List<string>();
        var hostsById = new Dictionary<string, MoteDeviceInfo>(StringComparer.OrdinalIgnoreCase);
        foreach (var host in config.Hosts)
        {
            var found = hosts.FirstOrDefault(h => string.Equals(h.InstanceId, host.InstanceId, StringComparison.OrdinalIgnoreCase));
            if (found is null)
            {
                problems.Add($"Host '{host.Id}' is not plugged in, or is in a different USB port. See 'ambimote list'.");
            }
            else
            {
                hostsById[host.Id] = found;
            }
        }

        var missingMonitors = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var sticks = new List<ResolvedStick>();
        foreach (var stick in config.Sticks)
        {
            var monitor = monitors.FirstOrDefault(m => string.Equals(m.DeviceName, stick.Monitor, StringComparison.OrdinalIgnoreCase));
            if (monitor is null)
            {
                if (missingMonitors.Add(stick.Monitor))
                {
                    problems.Add($"Monitor {stick.Monitor} is not attached. See 'ambimote monitors'.");
                }
            }
            else if (hostsById.TryGetValue(stick.Host, out var host))
            {
                sticks.Add(new ResolvedStick(stick, host, monitor));
            }
        }

        return new ResolvedSetup(sticks, problems);
    }
}
