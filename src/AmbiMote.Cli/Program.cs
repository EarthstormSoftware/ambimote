// ambimote: command-line tool for AmbiMote.
using System.CommandLine;

using AmbiMote.Cli;

var root = new RootCommand("Ambient lighting behind your monitors with Pimoroni Mote sticks.");
root.Subcommands.Add(HostCommands.CreateListCommand());
root.Subcommands.Add(HostCommands.CreateIdentifyCommand());
root.Subcommands.Add(HostCommands.CreateFillCommand());
root.Subcommands.Add(HostCommands.CreateOffCommand());

var parseResult = root.Parse(args);
int exitCode = await parseResult.InvokeAsync();

// The parser reports its own errors with exit code 1; keep usage errors on 2.
return parseResult.Errors.Count > 0 ? ExitCodes.Usage : exitCode;
