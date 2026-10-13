using System.CommandLine;
using System.Diagnostics;
using System.Text.Json;

using AmbiMote.Core;
using AmbiMote.Core.Capture;
using AmbiMote.Core.Mote;
using AmbiMote.Core.Sampling;

using SharpGen.Runtime;

namespace AmbiMote.Cli;

/// <summary>Commands that capture the screen: listing monitors and driving sticks from them.</summary>
internal static class CaptureCommands
{
    public static Command CreateMonitorsCommand()
    {
        var json = new Option<bool>("--json") { Description = "Write the result as JSON." };
        var command = new Command("monitors", "List the monitors attached to the desktop.") { json };
        command.SetAction(parseResult =>
        {
            var outputs = DisplayOutput.List();
            if (parseResult.GetValue(json))
            {
                Console.WriteLine(JsonSerializer.Serialize(outputs.Select(static o => new
                {
                    deviceName = o.DeviceName,
                    left = o.Left,
                    top = o.Top,
                    width = o.Width,
                    height = o.Height,
                    primary = o.IsPrimary,
                    rotated = o.Rotated,
                    adapter = o.Adapter,
                })));
            }
            else
            {
                foreach (var o in outputs)
                {
                    string flags = (o.IsPrimary ? "  primary" : "") + (o.Rotated ? "  rotated" : "");
                    Console.WriteLine($"{o.DeviceName,-14} {o.Width}x{o.Height} at ({o.Left},{o.Top})  {o.Adapter}{flags}");
                }
            }

            return ExitCodes.Success;
        });
        return command;
    }

    public static Command CreateSnapshotCommand()
    {
        var file = new Argument<FileInfo>("file") { Description = "Where to save the image, as a BMP." };
        var monitor = new Option<string?>("--monitor", "-m")
        {
            Description = "The monitor to capture, such as \\\\.\\DISPLAY1. Default: the primary monitor.",
        };
        var command = new Command(
            "snapshot",
            "Save the small image AmbiMote samples from, and print the colours along each edge, to check what it sees.")
        {
            file, monitor,
        };
        command.SetAction(parseResult =>
        {
            string? deviceName = parseResult.GetValue(monitor) ?? DisplayOutput.List().FirstOrDefault(static o => o.IsPrimary)?.DeviceName;
            if (deviceName is null)
            {
                Console.Error.WriteLine("No primary monitor found. Pick one with --monitor.");
                return ExitCodes.Usage;
            }

            try
            {
                using var capture = DesktopDuplicationCapture.Open(deviceName);
                BgraImage? image = null;
                for (int attempt = 0; attempt < 10 && image is null; attempt++)
                {
                    capture.TryCapture(TimeSpan.FromMilliseconds(200), out image);
                }

                if (image is null)
                {
                    Console.Error.WriteLine($"No frame from {deviceName}. Is the screen asleep or locked?");
                    return ExitCodes.Error;
                }

                var path = parseResult.GetValue(file)!;
                using (var stream = path.Create())
                {
                    BmpWriter.Write(image, stream);
                }

                Console.WriteLine($"{deviceName}: {image.Width}x{image.Height}. Saved to {path.FullName}.");
                var pixels = new Rgb[MoteProtocol.PixelsPerStick];
                foreach (var edge in Enum.GetValues<ScreenEdge>())
                {
                    EdgeSampler.Sample(image, edge, 0, 1, 0.1, pixels);
                    Console.WriteLine($"{edge.ToString().ToLowerInvariant(),-7} {string.Join(' ', pixels)}");
                }

                return ExitCodes.Success;
            }
            catch (ArgumentException)
            {
                Console.Error.WriteLine($"No monitor named {deviceName}. See 'ambimote monitors'.");
                return ExitCodes.Usage;
            }
            catch (Exception ex) when (ex is NotSupportedException or SharpGenException or IOException or UnauthorizedAccessException)
            {
                Console.Error.WriteLine($"Could not take a snapshot of {deviceName}: {ex.Message}");
                return ExitCodes.Error;
            }
        });
        return command;
    }

    public static Command CreateRunCommand()
    {
        var monitor = new Option<string?>("--monitor", "-m")
        {
            Description = "The monitor to capture, such as \\\\.\\DISPLAY1 (see 'ambimote monitors'). Default: the primary monitor.",
        };
        var edge = new Option<ScreenEdge>("--edge", "-e") { Description = "The edge of the monitor the stick sits behind.", Required = true };
        var channel = new Option<int>("--channel", "-c") { Description = "The channel the stick is on (1-4).", Required = true };
        var from = new Option<double>("--from")
        {
            Description = "Where pixel 0 sits along the edge, from 0 to 1 (left to right, or top to bottom).",
            DefaultValueFactory = _ => 0.0,
        };
        var to = new Option<double>("--to")
        {
            Description = "Where the last pixel sits along the edge, from 0 to 1.",
            DefaultValueFactory = _ => 1.0,
        };
        var depth = new Option<double>("--depth")
        {
            Description = "How far into the screen to sample, as a fraction of its width or height.",
            DefaultValueFactory = _ => 0.1,
        };
        var fps = new Option<int>("--fps") { Description = "Most updates per second.", DefaultValueFactory = _ => 30 };
        var stats = new Option<bool>("--stats") { Description = "Every second, print how many frames were captured and sent, and the time spent." };
        var duration = new Option<double?>("--duration") { Description = "Stop after this many seconds, instead of waiting for Ctrl+C." };

        channel.Validators.Add(static result =>
        {
            if (result.GetValueOrDefault<int>() is < 1 or > MoteProtocol.ChannelCount)
            {
                result.AddError($"--channel must be between 1 and {MoteProtocol.ChannelCount}.");
            }
        });
        foreach (var fraction in new[] { from, to })
        {
            fraction.Validators.Add(result =>
            {
                if (result.GetValueOrDefault<double>() is < 0 or > 1)
                {
                    result.AddError($"{fraction.Name} must be between 0 and 1.");
                }
            });
        }

        depth.Validators.Add(static result =>
        {
            if (result.GetValueOrDefault<double>() is <= 0 or > 1)
            {
                result.AddError("--depth must be more than 0 and at most 1.");
            }
        });
        duration.Validators.Add(static result =>
        {
            if (result.GetValueOrDefault<double?>() is <= 0)
            {
                result.AddError("--duration must be more than 0.");
            }
        });
        fps.Validators.Add(static result =>
        {
            if (result.GetValueOrDefault<int>() is < 1 or > 60)
            {
                result.AddError("--fps must be between 1 and 60.");
            }
        });

        var command = new Command("run", "Light one stick from one edge of a monitor until Ctrl+C, then turn it off.")
        {
            monitor, edge, channel, from, to, depth, fps, stats, duration,
            HostCommands.PortOption, HostCommands.BrightnessOption, HostCommands.MaxCurrentOption,
        };
        command.SetAction((parseResult, cancellationToken) => HostCommands.RunOnHosts(parseResult, cancellationToken, async (hosts, token) =>
        {
            if (hosts.Count > 1)
            {
                Console.Error.WriteLine(
                    $"Found several Mote hosts ({string.Join(", ", hosts.Select(static h => h.Info.PortName))}). Pick one with --port.");
                return ExitCodes.Usage;
            }

            string? deviceName = parseResult.GetValue(monitor) ?? DisplayOutput.List().FirstOrDefault(static o => o.IsPrimary)?.DeviceName;
            if (deviceName is null)
            {
                Console.Error.WriteLine("No primary monitor found. Pick one with --monitor.");
                return ExitCodes.Usage;
            }

            var (info, host) = hosts[0];
            DesktopDuplicationCapture capture;
            try
            {
                capture = DesktopDuplicationCapture.Open(deviceName);
            }
            catch (ArgumentException)
            {
                Console.Error.WriteLine($"No monitor named {deviceName}. See 'ambimote monitors'.");
                return ExitCodes.Usage;
            }
            catch (Exception ex) when (ex is NotSupportedException or SharpGenException)
            {
                Console.Error.WriteLine($"Could not capture {deviceName}: {ex.Message}");
                return ExitCodes.Error;
            }

            using (capture)
            {
                var stickEdge = parseResult.GetValue(edge);
                int stickChannel = parseResult.GetValue(channel);
                var mirror = new StickMirror(
                    capture, host, stickChannel, stickEdge, parseResult.GetValue(from), parseResult.GetValue(to), parseResult.GetValue(depth));
                bool showStats = parseResult.GetValue(stats);
                Console.WriteLine(
                    $"Lighting {info.PortName} channel {stickChannel} from the {stickEdge.ToString().ToLowerInvariant()} edge of {deviceName}. Press Ctrl+C to stop.");

                using var stop = CancellationTokenSource.CreateLinkedTokenSource(token);
                if (parseResult.GetValue(duration) is { } seconds)
                {
                    stop.CancelAfter(TimeSpan.FromSeconds(seconds));
                }

                using var timer = new PeriodicTimer(TimeSpan.FromSeconds(1.0 / parseResult.GetValue(fps)));
                var second = Stopwatch.StartNew();
                var busy = new Stopwatch();
                int ticks = 0, captured = 0, sent = 0;
                try
                {
                    while (await timer.WaitForNextTickAsync(stop.Token))
                    {
                        busy.Start();
                        var update = mirror.Update(TimeSpan.Zero);
                        busy.Stop();
                        ticks++;
                        captured += update == MirrorUpdate.NoNewFrame ? 0 : 1;
                        sent += update == MirrorUpdate.Sent ? 1 : 0;
                        if (showStats && second.ElapsedMilliseconds >= 1000)
                        {
                            Console.WriteLine(
                                $"{ticks} ticks, {captured} captured, {sent} sent, {busy.Elapsed.TotalMilliseconds / ticks:0.00} ms per tick");
                            ticks = captured = sent = 0;
                            busy.Reset();
                            second.Restart();
                        }
                    }
                }
                catch (Exception ex) when (ex is NotSupportedException or SharpGenException)
                {
                    Console.Error.WriteLine($"Could not capture {deviceName}: {ex.Message}");
                    return ExitCodes.Error;
                }
            }

            return ExitCodes.Success;
        }));
        return command;
    }
}
