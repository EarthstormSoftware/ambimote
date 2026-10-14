using System.Text.Json;
using System.Text.Json.Serialization;

using AmbiMote.Core.Engine;
using AmbiMote.Core.Settings;

namespace AmbiMote.Core.Control;

/// <summary>The running app's answer to a <see cref="ControlCommand"/>: what it is showing now, or why the command failed.</summary>
public sealed record ControlReply
{
    private static readonly JsonSerializerOptions s_json = new(JsonSerializerDefaults.Web)
    {
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    };

    /// <summary>Why the command failed, or null if it worked.</summary>
    public string? Error { get; init; }

    /// <summary>The mode, in lower case: off, light, colour, effect or ambient.</summary>
    public string? Mode { get; init; }

    /// <summary>The mode the lights come back on in, while they are off.</summary>
    public string? LastMode { get; init; }

    /// <summary>Brightness in percent.</summary>
    public int? Brightness { get; init; }

    public int? Kelvin { get; init; }

    /// <summary>The colour for colour mode, as hex such as <c>#ff6000</c>.</summary>
    public string? Colour { get; init; }

    public string? Effect { get; init; }

    /// <summary>What the engine is doing, in lower case: off, running, waiting, error, paused or identifying.</summary>
    public string? State { get; init; }

    /// <summary>The engine's status line, as the app shows it.</summary>
    public string? Message { get; init; }

    public static ControlReply Failed(string error) => new() { Error = error };

    public static ControlReply From(AppSettings settings, EngineStatus status)
    {
        ArgumentNullException.ThrowIfNull(settings);
        ArgumentNullException.ThrowIfNull(status);
        var lighting = settings.Lighting;
        return new ControlReply
        {
            Mode = Name(lighting.Mode),
            LastMode = Name(settings.LastMode),
            Brightness = (int)Math.Round(lighting.Brightness * 100),
            Kelvin = lighting.Kelvin,
            Colour = lighting.Colour.ToString(),
            Effect = Name(lighting.Effect),
            State = Name(status.State),
            Message = status.Message,
        };
    }

    /// <exception cref="JsonException">The text is not a reply.</exception>
    public static ControlReply Parse(string json) => JsonSerializer.Deserialize<ControlReply>(json, s_json) ?? throw new JsonException("Empty reply.");

    public string ToJson() => JsonSerializer.Serialize(this, s_json);

    /// <summary>One line for people, such as <c>Light at 2700 K, 60% brightness</c>.</summary>
    public string Describe()
    {
        if (Error is not null)
        {
            return Error;
        }

        string what = Mode switch
        {
            "off" => $"Off (turning on goes back to {LastMode})",
            "light" => $"Light at {Kelvin} K",
            "colour" => $"Colour {Colour}",
            "effect" => $"Effect {Effect}",
            _ => Mode ?? "Unknown",
        };
        return Mode == "off" ? what : $"{Capitalise(what)}, {Brightness}% brightness";
    }

    private static string Name<T>(T value)
        where T : struct, Enum => value.ToString().ToLowerInvariant();

    private static string Capitalise(string text) => text.Length == 0 ? text : char.ToUpperInvariant(text[0]) + text[1..];
}
