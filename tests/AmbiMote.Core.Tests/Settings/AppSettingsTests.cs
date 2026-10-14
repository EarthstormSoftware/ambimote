using AmbiMote.Core.Engine;
using AmbiMote.Core.Settings;

namespace AmbiMote.Core.Tests.Settings;

public sealed class AppSettingsTests : IDisposable
{
    private readonly string _folder = Path.Combine(Path.GetTempPath(), "AmbiMote.Tests", Guid.NewGuid().ToString("N"));

    public void Dispose()
    {
        if (Directory.Exists(_folder))
        {
            Directory.Delete(_folder, recursive: true);
        }
    }

    [Fact]
    public void SaveAndLoad_RoundTrips()
    {
        string path = Path.Combine(_folder, "settings.json");
        var settings = new AppSettings
        {
            StartMinimised = true,
            MinimiseToTray = false,
            PauseWhenAway = false,
            Lighting = new LightingSettings { Mode = LightingMode.Effect, Colour = new Rgb(1, 2, 3), Effect = EffectKind.Candle, Kelvin = 3000 },
        };

        settings.Save(path);
        var loaded = AppSettings.Load(path, out string? problem);

        Assert.Null(problem);
        Assert.Equal(settings.Normalised(), loaded);
        Assert.Contains("\"#010203\"", File.ReadAllText(path), StringComparison.Ordinal);
        Assert.Contains("\"candle\"", File.ReadAllText(path), StringComparison.Ordinal);
        Assert.DoesNotContain("steadyColour", File.ReadAllText(path), StringComparison.Ordinal);
    }

    [Fact]
    public void Load_MissingFile_GivesDefaultsQuietly()
    {
        var loaded = AppSettings.Load(Path.Combine(_folder, "none.json"), out string? problem);

        Assert.Null(problem);
        Assert.Equal(new AppSettings(), loaded);
    }

    [Fact]
    public void Load_BrokenFile_GivesDefaultsAndSaysWhy()
    {
        string path = Path.Combine(_folder, "settings.json");
        Directory.CreateDirectory(_folder);
        File.WriteAllText(path, "{ not json");

        var loaded = AppSettings.Load(path, out string? problem);

        Assert.NotNull(problem);
        Assert.Equal(new AppSettings(), loaded);
    }

    [Fact]
    public void LastMode_FollowsTheModeAndComesBackWhenTheLightsTurnOn()
    {
        var settings = new AppSettings { Lighting = new LightingSettings { Mode = LightingMode.Colour } }.Normalised();

        var off = settings.WithLightsOn(false);
        var on = off.WithLightsOn(true);

        Assert.Equal(LightingMode.Colour, settings.LastMode);
        Assert.Equal(LightingMode.Off, off.Lighting.Mode);
        Assert.Equal(LightingMode.Colour, off.LastMode);
        Assert.Equal(LightingMode.Colour, on.Lighting.Mode);
    }

    [Fact]
    public void Parse_NeverRemembersOffAsTheLastMode()
    {
        var loaded = AppSettings.Parse("""{ "lighting": { "mode": "off" }, "lastMode": "off" }""");

        Assert.Equal(new LightingSettings().Mode, loaded.LastMode);
    }

    [Fact]
    public void Parse_KeepsDefaultsForMissingValuesAndClampsTheRest()
    {
        var loaded = AppSettings.Parse("""{ "lighting": { "mode": "light", "brightness": 3 } }""");

        Assert.Equal(LightingMode.Light, loaded.Lighting.Mode);
        Assert.Equal(1, loaded.Lighting.Brightness);
        Assert.Equal(new LightingSettings().Kelvin, loaded.Lighting.Kelvin);
        Assert.True(loaded.MinimiseToTray);
        Assert.True(loaded.PauseWhenAway);
    }
}
