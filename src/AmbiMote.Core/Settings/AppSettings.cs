using System.Text.Json;
using System.Text.Json.Serialization;

using AmbiMote.Core.Engine;

namespace AmbiMote.Core.Settings;

/// <summary>
/// The desktop app's settings, saved by the app itself to <c>%APPDATA%\AmbiMote\settings.json</c>. The stick mapping
/// stays in <c>config.json</c>; whether AmbiMote starts with Windows is kept by <see cref="StartupRegistration"/>.
/// </summary>
public sealed record AppSettings
{
    private static readonly JsonSerializerOptions s_json = new(JsonSerializerDefaults.Web)
    {
        Converters = { new JsonStringEnumConverter(JsonNamingPolicy.CamelCase), new RgbJsonConverter() },
        ReadCommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = true,
        WriteIndented = true,
    };

    public LightingSettings Lighting { get; init; } = new();

    /// <summary>Open in the tray, without showing the window.</summary>
    public bool StartMinimised { get; init; }

    /// <summary>Hide the window to the tray when it is minimised, instead of keeping it on the taskbar.</summary>
    public bool MinimiseToTray { get; init; } = true;

    /// <summary>Turn the lights off while the PC is locked or the screens are off, and back on afterwards.</summary>
    public bool PauseWhenAway { get; init; } = true;

    /// <summary>The mode to go back to when the lights are turned on again. Never <see cref="LightingMode.Off"/>.</summary>
    public LightingMode LastMode { get; init; } = new LightingSettings().Mode;

    public static string DefaultPath =>
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "AmbiMote", "settings.json");

    /// <summary>
    /// Loads the settings. A missing or unreadable file gives the defaults, since the app can always write a new one;
    /// <paramref name="problem"/> then says why, unless the file just did not exist yet.
    /// </summary>
    public static AppSettings Load(string path, out string? problem)
    {
        problem = null;
        try
        {
            if (!File.Exists(path))
            {
                return new AppSettings();
            }

            return Parse(File.ReadAllText(path));
        }
        catch (Exception ex) when (ex is JsonException or IOException or UnauthorizedAccessException)
        {
            problem = $"{path} could not be read, so the default settings are used: {ex.Message}";
            return new AppSettings();
        }
    }

    /// <exception cref="JsonException">The JSON is not valid.</exception>
    public static AppSettings Parse(string json)
    {
        var settings = JsonSerializer.Deserialize<AppSettings>(json, s_json) ?? new AppSettings();
        return (settings with { Lighting = settings.Lighting ?? new LightingSettings() }).Normalised();
    }

    /// <summary>
    /// A copy with every value in range, and with <see cref="LastMode"/> following the lighting mode whenever the lights
    /// are on.
    /// </summary>
    public AppSettings Normalised()
    {
        var lighting = Lighting.Normalised();
        var lastMode = lighting.Mode != LightingMode.Off ? lighting.Mode
            : Enum.IsDefined(LastMode) && LastMode != LightingMode.Off ? LastMode
            : new LightingSettings().Mode;
        return this with { Lighting = lighting, LastMode = lastMode };
    }

    /// <summary>Turns the lights on in <see cref="LastMode"/>, or off.</summary>
    public AppSettings WithLightsOn(bool on) => (this with { Lighting = Lighting with { Mode = on ? LastMode : LightingMode.Off } }).Normalised();

    public string ToJson() => JsonSerializer.Serialize(this, s_json);

    /// <summary>Saves the settings, replacing the file in one step so a crash cannot leave half a file.</summary>
    public void Save(string path)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(path))!);
        string temp = path + ".tmp";
        File.WriteAllText(temp, ToJson());
        File.Move(temp, path, overwrite: true);
    }

    /// <summary>Writes colours as hex, such as <c>"#ff6000"</c>.</summary>
    private sealed class RgbJsonConverter : JsonConverter<Rgb>
    {
        public override Rgb Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options) =>
            Rgb.TryParse(reader.GetString(), out var colour) ? colour : throw new JsonException("Colours must be hex, such as \"#ff6000\".");

        public override void Write(Utf8JsonWriter writer, Rgb value, JsonSerializerOptions options) => writer.WriteStringValue(value.ToString());
    }
}
