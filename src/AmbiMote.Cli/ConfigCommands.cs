using System.CommandLine;
using System.Globalization;

using AmbiMote.Core.Capture;
using AmbiMote.Core.Config;
using AmbiMote.Core.Lighting;
using AmbiMote.Core.Mote;

namespace AmbiMote.Cli;

/// <summary>The per-user config: where each stick is mounted.</summary>
internal static class ConfigCommands
{
    public static Command CreateConfigCommand()
    {
        var command = new Command(
            "config",
            $"Check the config file ({AmbiMoteConfig.DefaultPath}) against the Mote hosts and monitors that are connected.");
        command.SetAction(static _ =>
        {
            if (!TryLoad(out var config, required: true))
            {
                return ExitCodes.Config;
            }

            var setup = SetupResolver.Resolve(config!, MoteDiscovery.FindHosts(), DisplayOutput.List());
            Console.WriteLine($"{AmbiMoteConfig.DefaultPath}: {config!.Hosts.Count} hosts, {config.Sticks.Count} sticks.");
            foreach (var stick in config.Sticks)
            {
                bool ready = setup.Sticks.Any(s => s.Stick == stick);
                Console.WriteLine(string.Create(
                    CultureInfo.InvariantCulture,
                    $"  {stick.Host,-8} ch{stick.Channel}  {stick.Monitor,-14} {stick.Edge.ToString().ToLowerInvariant(),-6} {stick.From:0.00} -> {stick.To:0.00}  {(ready ? "ready" : "not available")}"));
            }

            foreach (string problem in setup.Problems)
            {
                Console.WriteLine($"Warning: {problem}");
            }

            return ExitCodes.Success;
        });
        return command;
    }

    /// <summary>
    /// Loads the config, printing why if it cannot. A missing file is fine unless <paramref name="required"/>;
    /// <paramref name="config"/> is then null.
    /// </summary>
    public static bool TryLoad(out AmbiMoteConfig? config, bool required = false)
    {
        string path = AmbiMoteConfig.DefaultPath;
        try
        {
            config = AmbiMoteConfig.Load(path);
        }
        catch (Exception ex) when (ex is InvalidDataException or IOException or UnauthorizedAccessException)
        {
            Console.Error.WriteLine($"{path}: {ex.Message}");
            config = null;
            return false;
        }

        if (config is null && required)
        {
            Console.Error.WriteLine($"No config at {path}. Copy config.example.json from the repository there and fill it in.");
            return false;
        }

        return true;
    }

    /// <summary>
    /// The configured sticks on the open hosts, in config order, with their positions around the setup.
    /// Prints a warning for each stick that cannot be used. Null if none can.
    /// </summary>
    public static StickChain? CreateMappedChain(AmbiMoteConfig config, IReadOnlyList<(MoteDeviceInfo Info, MoteHost Host)> hosts)
    {
        var setup = SetupResolver.Resolve(config, [.. hosts.Select(static h => h.Info)], DisplayOutput.List());
        foreach (string problem in setup.Problems)
        {
            Console.Error.WriteLine($"Warning: {problem}");
        }

        if (setup.Sticks.Count == 0)
        {
            Console.Error.WriteLine("None of the sticks in the config can be used.");
            return null;
        }

        var byInfo = hosts.ToDictionary(static h => h.Info, static h => h.Host);
        var placed = setup.Sticks
            .Select(s => new PlacedStick(s.Monitor, s.Stick.Edge, s.Stick.From, s.Stick.To, byInfo[s.Host].GetPixels(s.Stick.Channel).Length))
            .ToList();
        return new StickChain(setup.Sticks.Select(s => (byInfo[s.Host], s.Stick.Channel)), SetupLayout.Positions(placed));
    }
}
