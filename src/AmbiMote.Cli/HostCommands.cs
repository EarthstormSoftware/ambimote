using System.CommandLine;
using System.Text.Json;

using AmbiMote.Core;
using AmbiMote.Core.Lighting;
using AmbiMote.Core.Mote;

namespace AmbiMote.Cli;

/// <summary>Commands that talk to Mote hosts directly: finding them, identifying sticks and test colours.</summary>
internal static class HostCommands
{
    internal const string ColourHelp =
        "A colour name (red, orange, yellow, green, cyan, blue, purple, magenta, pink, white, candle, warm, neutral, daylight), "
        + "a colour temperature such as 3000k, or hex such as ff8000.";

    internal const string ColourError = "Unknown colour. Use a name such as warm or red, a temperature such as 3000k, or hex such as ff8000.";

    private static readonly Rgb[] s_channelColours =
    [
        new(255, 0, 0),
        new(0, 255, 0),
        new(0, 0, 255),
        new(255, 160, 0),
    ];

    internal static readonly Option<string[]> PortOption = new("--port", "-p")
    {
        Description = "Only use the Mote host on this serial port, such as COM5. Repeat for several. Default: every Mote host.",
        AllowMultipleArgumentsPerToken = true,
    };

    internal static readonly Option<double> BrightnessOption = new("--brightness", "-b")
    {
        Description = "Brightness from 0 to 1.",
        DefaultValueFactory = _ => 0.25,
    };

    internal static readonly Option<double?> MaxCurrentOption = new("--max-current")
    {
        Description = $"Most current the sticks on one host may draw, in mA (estimated). Default: {MoteHost.DefaultMaxMilliamps}.",
    };

    static HostCommands()
    {
        BrightnessOption.Validators.Add(static result =>
        {
            if (result.GetValueOrDefault<double>() is < 0 or > 1)
            {
                result.AddError("--brightness must be between 0 and 1.");
            }
        });
        MaxCurrentOption.Validators.Add(static result =>
        {
            if (result.GetValueOrDefault<double?>() is <= 0)
            {
                result.AddError("--max-current must be more than 0.");
            }
        });
    }

    public static Command CreateListCommand()
    {
        var json = new Option<bool>("--json") { Description = "Write the result as JSON." };
        var command = new Command("list", "List the Mote hosts plugged into this PC.") { json };
        command.SetAction(parseResult =>
        {
            var hosts = MoteDiscovery.FindHosts();
            if (parseResult.GetValue(json))
            {
                Console.WriteLine(JsonSerializer.Serialize(
                    hosts.Select(static h => new { port = h.PortName, serialNumber = h.SerialNumber, instanceId = h.InstanceId })));
            }
            else if (hosts.Count == 0)
            {
                Console.Error.WriteLine("No Mote hosts found.");
            }
            else
            {
                foreach (var host in hosts)
                {
                    Console.WriteLine($"{host.PortName,-8} serial {host.SerialNumber ?? "(none)"}  {host.InstanceId}");
                }
            }

            return hosts.Count == 0 ? ExitCodes.NoDevice : ExitCodes.Success;
        });
        return command;
    }

    public static Command CreateIdentifyCommand()
    {
        var delay = new Option<int?>("--delay")
        {
            Description = "Move to the next stick after this many milliseconds, instead of waiting for Enter.",
        };
        var command = new Command(
            "identify",
            "Light each stick in turn so you can see which host and channel it is on. Pixel 0 is white; the rest fade out along the stick.")
        {
            PortOption, BrightnessOption, MaxCurrentOption, delay,
        };
        command.SetAction((parseResult, cancellationToken) => RunOnHosts(parseResult, cancellationToken, async (hosts, token) =>
        {
            int? delayMs = parseResult.GetValue(delay);
            foreach (var (info, host) in hosts)
            {
                for (int channel = 1; channel <= MoteProtocol.ChannelCount; channel++)
                {
                    foreach (var (_, other) in hosts)
                    {
                        other.Clear();
                        other.Show();
                    }

                    LightForIdentify(host.GetPixels(channel), s_channelColours[channel - 1]);
                    host.Show();
                    Console.WriteLine(
                        $"{info.PortName} (serial {info.SerialNumber ?? "none"}), channel {channel}: {ColourName(channel)}.");
                    await WaitForNextAsync(delayMs, token);
                }
            }

            return ExitCodes.Success;
        }));
        return command;
    }

    public static Command CreateFillCommand()
    {
        var colour = new Argument<string>("colour") { Description = ColourHelp };
        var channel = new Option<int?>("--channel", "-c") { Description = "Only fill this channel (1-4)." };
        colour.Validators.Add(static result =>
        {
            if (!Colours.TryParse(result.GetValueOrDefault<string>(), out _))
            {
                result.AddError(ColourError);
            }
        });
        channel.Validators.Add(static result =>
        {
            if (result.GetValueOrDefault<int?>() is < 1 or > MoteProtocol.ChannelCount)
            {
                result.AddError($"--channel must be between 1 and {MoteProtocol.ChannelCount}.");
            }
        });
        var command = new Command("fill", "Set the sticks to one colour. They stay lit after the command exits.")
        {
            colour, PortOption, BrightnessOption, MaxCurrentOption, channel,
        };
        command.SetAction((parseResult, cancellationToken) => RunOnHosts(parseResult, cancellationToken, (hosts, _) =>
        {
            Colours.TryParse(parseResult.GetValue(colour), out var rgb);
            int? onlyChannel = parseResult.GetValue(channel);
            foreach (var (_, host) in hosts)
            {
                if (onlyChannel is { } c)
                {
                    host.Fill(c, rgb);
                }
                else
                {
                    host.FillAll(rgb);
                }

                host.Show();
            }

            return Task.FromResult(ExitCodes.Success);
        }, turnOffAfter: false));
        return command;
    }

    public static Command CreateOffCommand()
    {
        var command = new Command("off", "Turn the sticks off.") { PortOption };
        command.SetAction((parseResult, cancellationToken) =>
            RunOnHosts(parseResult, cancellationToken, static (_, _) => Task.FromResult(ExitCodes.Success)));
        return command;
    }

    /// <summary>
    /// Opens the selected hosts with all four channels configured as sticks, runs <paramref name="action"/>,
    /// and turns the sticks off afterwards (also on Ctrl+C) unless <paramref name="turnOffAfter"/> is false.
    /// </summary>
    internal static async Task<int> RunOnHosts(
        ParseResult parseResult,
        CancellationToken cancellationToken,
        Func<IReadOnlyList<(MoteDeviceInfo Info, MoteHost Host)>, CancellationToken, Task<int>> action,
        bool turnOffAfter = true)
    {
        var found = MoteDiscovery.FindHosts();
        string[] ports = parseResult.GetValue(PortOption) ?? [];
        var selected = ports.Length == 0
            ? found
            : found.Where(h => ports.Contains(h.PortName, StringComparer.OrdinalIgnoreCase)).ToList();
        if (selected.Count == 0)
        {
            Console.Error.WriteLine(found.Count == 0
                ? "No Mote hosts found."
                : $"No Mote host on {string.Join(", ", ports)}. Found: {string.Join(", ", found.Select(static h => h.PortName))}.");
            return ExitCodes.NoDevice;
        }

        var hosts = new List<(MoteDeviceInfo Info, MoteHost Host)>();
        try
        {
            foreach (var info in selected)
            {
                var host = MoteHost.Open(info.PortName);
                hosts.Add((info, host));
                host.Brightness = parseResult.GetValue(BrightnessOption);
                host.MaxMilliamps = parseResult.GetValue(MaxCurrentOption) ?? MoteHost.DefaultMaxMilliamps;
                host.ConfigureSticks();
            }

            return await action(hosts, cancellationToken);
        }
        catch (OperationCanceledException)
        {
            return ExitCodes.Success;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or TimeoutException)
        {
            Console.Error.WriteLine($"Could not talk to the Mote host: {ex.Message}");
            return ExitCodes.Error;
        }
        catch (InvalidOperationException) when (hosts.Any(static h => !h.Host.IsOpen))
        {
            // SerialPort closes itself when the USB device goes away.
            Console.Error.WriteLine("The Mote host was disconnected.");
            return ExitCodes.Error;
        }
        finally
        {
            foreach (var (_, host) in hosts)
            {
                try
                {
                    if (turnOffAfter)
                    {
                        host.Off();
                    }
                }
                catch (Exception ex) when (ex is IOException or TimeoutException or InvalidOperationException)
                {
                    // The host may have been unplugged; there is nothing left to turn off.
                }

                host.Dispose();
            }
        }
    }

    private static void LightForIdentify(Span<Rgb> pixels, Rgb colour)
    {
        pixels[0] = Rgb.White;
        for (int i = 1; i < pixels.Length; i++)
        {
            pixels[i] = colour.Scale(1.0 - (0.85 * (i - 1) / Math.Max(1, pixels.Length - 2)));
        }
    }

    private static string ColourName(int channel) => channel switch
    {
        1 => "red",
        2 => "green",
        3 => "blue",
        _ => "orange",
    };

    private static async Task WaitForNextAsync(int? delayMs, CancellationToken cancellationToken)
    {
        if (delayMs is { } ms)
        {
            await Task.Delay(ms, cancellationToken);
            return;
        }

        if (Console.IsInputRedirected)
        {
            await Task.Delay(2000, cancellationToken);
            return;
        }

        Console.Write("Press Enter for the next stick...");
        await Task.Run(Console.ReadLine, cancellationToken).WaitAsync(cancellationToken);
    }
}
