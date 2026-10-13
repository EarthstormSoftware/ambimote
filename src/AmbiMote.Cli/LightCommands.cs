using System.CommandLine;
using System.Diagnostics;

using AmbiMote.Core.Config;
using AmbiMote.Core.Lighting;
using AmbiMote.Core.Mote;

namespace AmbiMote.Cli;

/// <summary>Commands that use the sticks as ordinary lights.</summary>
internal static class LightCommands
{
    /// <summary>The effects, with the colour and seconds per cycle each uses unless told otherwise.</summary>
    private static readonly Dictionary<string, (string Description, string? Colour, double Period)> s_effects = new()
    {
        ["rainbow"] = ("A rainbow flowing along all the sticks.", null, 10),
        ["breathe"] = ("One colour slowly brightening and dimming.", "warm", 6),
        ["candle"] = ("Each LED flickering gently, like a candle.", "candle", 0),
        ["chase"] = ("A dot running along the sticks in order, to check which way round they are.", "white", 8),
    };

    public static Command CreateEffectCommand()
    {
        var name = new Argument<string>("effect")
        {
            Description = string.Join(" ", s_effects.Select(static e => $"{e.Key}: {e.Value.Description}")),
        };
        name.AcceptOnlyFromAmong([.. s_effects.Keys]);
        var colour = new Option<string?>("--colour", "-c")
        {
            Description = $"{HostCommands.ColourHelp} Default: warm for breathe, candle for candle, white for chase. Not used by rainbow.",
        };
        var period = new Option<double?>("--period")
        {
            Description = "Seconds for one cycle: once round for rainbow and chase, one breath for breathe. Not used by candle.",
        };
        var duration = new Option<double?>("--duration") { Description = "Stop after this many seconds, instead of waiting for Ctrl+C." };
        var fps = new Option<int>("--fps") { Description = "Updates per second.", DefaultValueFactory = _ => 30 };
        var channelOrder = new Option<bool>("--channel-order")
        {
            Description = "Ignore the config and run through every stick in channel order: channel 1 pixel 0 to 15, then channel 2, and so on.",
        };

        colour.Validators.Add(static result =>
        {
            if (!Colours.TryParse(result.GetValueOrDefault<string?>(), out _))
            {
                result.AddError(HostCommands.ColourError);
            }
        });
        foreach (var positive in new Option<double?>[] { period, duration })
        {
            positive.Validators.Add(result =>
            {
                if (result.GetValueOrDefault<double?>() is <= 0)
                {
                    result.AddError($"{positive.Name} must be more than 0.");
                }
            });
        }

        fps.Validators.Add(static result =>
        {
            if (result.GetValueOrDefault<int>() is < 1 or > 60)
            {
                result.AddError("--fps must be between 1 and 60.");
            }
        });

        var command = new Command(
            "effect",
            "Run a lighting effect until Ctrl+C, then turn the sticks off. With a config (see 'ambimote config'), effects run "
            + "clockwise around the monitors; without one, through the sticks in channel order.")
        {
            name, colour, period, duration, fps, channelOrder,
            HostCommands.PortOption, HostCommands.BrightnessOption, HostCommands.MaxCurrentOption,
        };
        command.SetAction((parseResult, cancellationToken) => HostCommands.RunOnHosts(parseResult, cancellationToken, async (hosts, token) =>
        {
            string effectName = parseResult.GetValue(name)!;
            var defaults = s_effects[effectName];
            Colours.TryParse(parseResult.GetValue(colour) ?? defaults.Colour ?? "white", out var rgb);
            double seconds = parseResult.GetValue(period) ?? defaults.Period;
            IEffect effect = effectName switch
            {
                "rainbow" => new RainbowEffect(seconds),
                "breathe" => new BreatheEffect(rgb, seconds),
                "candle" => new CandleEffect(rgb),
                _ => new ChaseEffect(rgb, seconds),
            };

            AmbiMoteConfig? config = null;
            if (!parseResult.GetValue(channelOrder) && !ConfigCommands.TryLoad(out config))
            {
                return ExitCodes.Config;
            }

            StickChain chain;
            if (config is null)
            {
                chain = new StickChain(hosts.SelectMany(static h =>
                    Enumerable.Range(1, MoteProtocol.ChannelCount).Select(channel => (h.Host, channel))));
            }
            else if (ConfigCommands.CreateMappedChain(config, hosts) is { } mapped)
            {
                chain = mapped;
            }
            else
            {
                return ExitCodes.NoDevice;
            }

            Console.WriteLine(
                $"Running {effectName} on {chain.PixelCount} LEDs, {(config is null ? "in channel order" : "around the monitors")}. Press Ctrl+C to stop.");

            using var stop = CancellationTokenSource.CreateLinkedTokenSource(token);
            if (parseResult.GetValue(duration) is { } limit)
            {
                stop.CancelAfter(TimeSpan.FromSeconds(limit));
            }

            using var timer = new PeriodicTimer(TimeSpan.FromSeconds(1.0 / parseResult.GetValue(fps)));
            var clock = Stopwatch.StartNew();
            do
            {
                chain.Show(effect, clock.Elapsed.TotalSeconds);
            }
            while (await timer.WaitForNextTickAsync(stop.Token));

            return ExitCodes.Success;
        }));
        return command;
    }
}
