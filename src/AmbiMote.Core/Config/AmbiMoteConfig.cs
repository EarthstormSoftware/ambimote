using System.Text.Json;
using System.Text.Json.Serialization;

using AmbiMote.Core.Mote;
using AmbiMote.Core.Sampling;

namespace AmbiMote.Core.Config;

/// <summary>A Mote host, matched by its Windows device instance ID.</summary>
/// <param name="Id">A name for the host, used by <see cref="StickConfig.Host"/>.</param>
/// <param name="InstanceId">
/// The device instance ID from <c>ambimote list</c>. Mote hosts have no USB serial number, so this is based on the USB
/// port the host is plugged into: keep each host in the same port.
/// </param>
public sealed record HostConfig(string Id, string InstanceId);

/// <summary>Where one stick is mounted.</summary>
/// <param name="Host">The <see cref="HostConfig.Id"/> of the host the stick is plugged into.</param>
/// <param name="Channel">The channel on that host, 1 to 4.</param>
/// <param name="Monitor">The monitor's device name, such as <c>\\.\DISPLAY1</c>, from <c>ambimote monitors</c>.</param>
/// <param name="Edge">The edge of the monitor the stick sits behind.</param>
/// <param name="From">
/// Where pixel 0 sits along the edge, from 0 to 1, measured left to right (top and bottom edges) or top to bottom (left and right edges).
/// </param>
/// <param name="To">Where the last pixel sits along the edge. Less than <paramref name="From"/> if the stick runs the other way.</param>
public sealed record StickConfig(string Host, int Channel, string Monitor, ScreenEdge Edge, double From, double To);

/// <summary>The per-user AmbiMote settings: which hosts there are and where each stick is mounted.</summary>
public sealed record AmbiMoteConfig(IReadOnlyList<HostConfig> Hosts, IReadOnlyList<StickConfig> Sticks)
{
    private static readonly JsonSerializerOptions s_json = new(JsonSerializerDefaults.Web)
    {
        Converters = { new JsonStringEnumConverter(JsonNamingPolicy.CamelCase) },
        RespectRequiredConstructorParameters = true,
        RespectNullableAnnotations = true,
        ReadCommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = true,
        WriteIndented = true,
    };

    /// <summary>The config file: <c>%APPDATA%\AmbiMote\config.json</c>.</summary>
    public static string DefaultPath =>
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "AmbiMote", "config.json");

    /// <summary>Loads and checks a config file. Returns null if the file does not exist.</summary>
    /// <exception cref="InvalidDataException">The file is not valid JSON, or the settings do not make sense.</exception>
    public static AmbiMoteConfig? Load(string path)
    {
        if (!File.Exists(path))
        {
            return null;
        }

        return Parse(File.ReadAllText(path));
    }

    /// <summary>Parses and checks config JSON.</summary>
    /// <exception cref="InvalidDataException">The JSON is not valid, or the settings do not make sense.</exception>
    public static AmbiMoteConfig Parse(string json)
    {
        AmbiMoteConfig? config;
        try
        {
            config = JsonSerializer.Deserialize<AmbiMoteConfig>(json, s_json);
        }
        catch (JsonException ex)
        {
            throw new InvalidDataException($"The config is not valid JSON: {ex.Message}", ex);
        }

        if (config is null)
        {
            throw new InvalidDataException("The config is empty.");
        }

        config = config with { Hosts = config.Hosts ?? [], Sticks = config.Sticks ?? [] };
        config.Validate();
        return config;
    }

    public string ToJson() => JsonSerializer.Serialize(this, s_json);

    private void Validate()
    {
        var hostIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var host in Hosts)
        {
            if (string.IsNullOrWhiteSpace(host?.Id) || string.IsNullOrWhiteSpace(host.InstanceId))
            {
                throw new InvalidDataException("Every host needs an id and an instanceId.");
            }

            if (!hostIds.Add(host.Id))
            {
                throw new InvalidDataException($"There are two hosts with the id '{host.Id}'.");
            }
        }

        var used = new HashSet<(string, int)>();
        foreach (var stick in Sticks)
        {
            if (stick is null)
            {
                throw new InvalidDataException("A stick entry is empty.");
            }

            string name = $"Stick {stick.Host} channel {stick.Channel}";
            if (string.IsNullOrWhiteSpace(stick.Host) || !hostIds.Contains(stick.Host))
            {
                throw new InvalidDataException($"{name}: there is no host with the id '{stick.Host}'.");
            }

            if (stick.Channel is < 1 or > MoteProtocol.ChannelCount)
            {
                throw new InvalidDataException($"{name}: the channel must be between 1 and {MoteProtocol.ChannelCount}.");
            }

            if (!used.Add((stick.Host.ToUpperInvariant(), stick.Channel)))
            {
                throw new InvalidDataException($"{name} is listed twice.");
            }

            if (string.IsNullOrWhiteSpace(stick.Monitor))
            {
                throw new InvalidDataException($"{name}: the monitor is missing.");
            }

            if (!Enum.IsDefined(stick.Edge))
            {
                throw new InvalidDataException($"{name}: the edge must be left, top, right or bottom.");
            }

            if (stick.From is < 0 or > 1 || stick.To is < 0 or > 1 || double.IsNaN(stick.From) || double.IsNaN(stick.To))
            {
                throw new InvalidDataException($"{name}: from and to must be between 0 and 1.");
            }
        }
    }
}
