using System.CommandLine;
using System.Text.Json;

using AmbiMote.Core.Control;

namespace AmbiMote.Cli;

/// <summary>Controls the running desktop app through its control pipe, instead of opening the Mote hosts directly.</summary>
internal static class AppCommands
{
    private static readonly TimeSpan s_timeout = TimeSpan.FromSeconds(3);

    public static Command CreateAppCommand()
    {
        var words = new Argument<string[]>("command")
        {
            Description = $"What to do: {ControlCommand.Usage}.",
            Arity = ArgumentArity.OneOrMore,
        };
        var json = new Option<bool>("--json") { Description = "Write the app's reply as JSON." };
        words.Validators.Add(static result =>
        {
            if (!ControlCommand.TryParse(string.Join(' ', result.GetValueOrDefault<string[]>() ?? []), out _, out string error))
            {
                result.AddError(error);
            }
        });

        var command = new Command(
            "app",
            "Control the running AmbiMote app: switch mode, change brightness, turn the lights on or off. "
            + "For example: 'ambimote app light 2700', 'ambimote app brightness +10', 'ambimote app toggle'.")
        {
            words, json,
        };
        command.SetAction(async (parseResult, cancellationToken) =>
        {
            ControlCommand.TryParse(string.Join(' ', parseResult.GetValue(words)!), out var request, out _);
            if (!ControlPipe.IsAppRunning())
            {
                Console.Error.WriteLine("The AmbiMote app is not running.");
                return ExitCodes.AppNotRunning;
            }

            ControlReply reply;
            try
            {
                reply = ControlReply.Parse(await ControlPipe.SendAsync(ControlPipe.DefaultName, request.ToString(), s_timeout, cancellationToken));
            }
            catch (Exception ex) when (ex is TimeoutException or IOException or UnauthorizedAccessException or JsonException)
            {
                Console.Error.WriteLine($"The AmbiMote app did not answer: {ex.Message}");
                return ExitCodes.AppNotRunning;
            }

            if (parseResult.GetValue(json))
            {
                Console.WriteLine(reply.ToJson());
            }
            else if (reply.Error is not null)
            {
                Console.Error.WriteLine(reply.Error);
            }
            else
            {
                Console.WriteLine(reply.Describe());

                // After a change the engine has not caught up yet, so its status line would describe the old mode.
                if (!request.ChangesSettings && reply.State is not ("off" or null))
                {
                    Console.WriteLine(reply.Message);
                }
            }

            return reply.Error is null ? ExitCodes.Success : ExitCodes.Error;
        });
        return command;
    }
}
