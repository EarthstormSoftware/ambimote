using Microsoft.Win32;

namespace AmbiMote.Core.Settings;

/// <summary>Starts a program when the user signs in to Windows, through the per-user <c>Run</c> key.</summary>
/// <param name="baseKey">The key holding <c>Software\Microsoft\Windows\CurrentVersion\Run</c>. Default: HKEY_CURRENT_USER.</param>
/// <param name="name">The name of the entry, shown in Task Manager's Startup apps.</param>
public sealed class StartupRegistration(RegistryKey? baseKey = null, string name = "AmbiMote")
{
    private const string RunKey = @"Software\Microsoft\Windows\CurrentVersion\Run";

    private readonly RegistryKey _baseKey = baseKey ?? Registry.CurrentUser;

    /// <summary>The command Windows runs: the program's path, quoted.</summary>
    public string? Command
    {
        get
        {
            using var run = _baseKey.OpenSubKey(RunKey);
            return run?.GetValue(name) as string;
        }
    }

    public bool IsEnabled => Command is not null;

    /// <summary>Starts <paramref name="exePath"/> at sign-in, replacing any older path (for example after the app was moved).</summary>
    public void Enable(string exePath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(exePath);
        using var run = _baseKey.CreateSubKey(RunKey);
        run.SetValue(name, $"\"{exePath}\"");
    }

    public void Disable()
    {
        using var run = _baseKey.OpenSubKey(RunKey, writable: true);
        run?.DeleteValue(name, throwOnMissingValue: false);
    }

    /// <summary>True if the entry starts <paramref name="exePath"/>, rather than a copy somewhere else.</summary>
    public bool Starts(string exePath) =>
        Command is { } command && string.Equals(command.Trim('"'), exePath, StringComparison.OrdinalIgnoreCase);
}
