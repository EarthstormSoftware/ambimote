using AmbiMote.Core.Engine;

namespace AmbiMote.Core.Settings;

/// <summary>A mode to use while a program is in front.</summary>
/// <param name="App">The program's file name, such as <c>game.exe</c>.</param>
public sealed record AppModeRule(string App, LightingMode Mode);

/// <summary>
/// Modes to switch to automatically, depending on what is in front: a program with a rule of its own, or anything
/// filling a screen, such as a game or a video. The mode goes back afterwards.
/// </summary>
public sealed record AutoModeSettings
{
    /// <summary>The mode while a full-screen window is in front, or null to leave the mode alone.</summary>
    public LightingMode? FullScreen { get; init; } = LightingMode.Ambient;

    /// <summary>
    /// The monitor ambient mode takes its colours from while <see cref="FullScreen"/> is in charge, such as
    /// <c>\\.\DISPLAY1</c>, or null to use <see cref="LightingSettings.AmbientSource"/>. It is never saved as the
    /// user's own choice, so it ends with the full-screen window. See <see cref="ForFullScreen"/>.
    /// </summary>
    public string? FullScreenSource { get; init; }

    /// <summary>Programs with a mode of their own. These win over <see cref="FullScreen"/>.</summary>
    public IReadOnlyList<AppModeRule> Apps { get; init; } = [];

    /// <summary>Whether anything is set, so the app only watches the foreground window when it needs to.</summary>
    public bool IsActive => FullScreen is not null || Apps.Count > 0;

    /// <summary>A program's file name as rules use it: no folder, and <c>.exe</c> added if there is no extension.</summary>
    public static string NormaliseApp(string app)
    {
        string name = Path.GetFileName(app.Trim().Trim('"'));
        return name.Length == 0 || Path.HasExtension(name) ? name : name + ".exe";
    }

    /// <summary>The rule for <paramref name="app"/>, if it has one. File names are compared ignoring case.</summary>
    public AppModeRule? RuleFor(string? app) =>
        app is null ? null : Apps.FirstOrDefault(r => string.Equals(r.App, app, StringComparison.OrdinalIgnoreCase));

    /// <summary>
    /// A copy with <paramref name="app"/> using <paramref name="mode"/>: its rule changed in place, or a new one at the end.
    /// </summary>
    public AutoModeSettings WithApp(string app, LightingMode mode)
    {
        app = NormaliseApp(app);
        bool found = RuleFor(app) is not null;
        var apps = found
            ? Apps.Select(r => string.Equals(r.App, app, StringComparison.OrdinalIgnoreCase) ? r with { Mode = mode } : r)
            : Apps.Append(new AppModeRule(app, mode));
        return (this with { Apps = [.. apps] }).Normalised();
    }

    /// <summary>What the lights show while <see cref="FullScreen"/> is in charge: <paramref name="lighting"/> with <see cref="FullScreenSource"/>, if set.</summary>
    public LightingSettings ForFullScreen(LightingSettings lighting) =>
        FullScreenSource is { } source ? lighting with { AmbientSource = source } : lighting;

    public AutoModeSettings WithoutApp(string app) =>
        this with { Apps = [.. Apps.Where(r => !string.Equals(r.App, app, StringComparison.OrdinalIgnoreCase))] };

    /// <summary>A copy with unknown modes dropped, and each program named once, the standard way.</summary>
    public AutoModeSettings Normalised()
    {
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var apps = new List<AppModeRule>();
        foreach (var rule in Apps ?? [])
        {
            string app = rule is null ? "" : NormaliseApp(rule.App ?? "");
            if (app.Length > 0 && Enum.IsDefined(rule!.Mode) && seen.Add(app))
            {
                apps.Add(rule with { App = app });
            }
        }

        return this with
        {
            FullScreen = FullScreen is { } mode && !Enum.IsDefined(mode) ? null : FullScreen,
            FullScreenSource = string.IsNullOrWhiteSpace(FullScreenSource) ? null : FullScreenSource,
            Apps = apps,
        };
    }

    // Records compare lists by reference; compare the rules instead, so unchanged settings are not saved again.
    public bool Equals(AutoModeSettings? other) =>
        other is not null && FullScreen == other.FullScreen && FullScreenSource == other.FullScreenSource && Apps.SequenceEqual(other.Apps);

    public override int GetHashCode() => HashCode.Combine(FullScreen, Apps.Count);
}
