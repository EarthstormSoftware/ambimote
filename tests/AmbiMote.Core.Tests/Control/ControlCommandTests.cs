using AmbiMote.Core.Control;
using AmbiMote.Core.Engine;
using AmbiMote.Core.Settings;

namespace AmbiMote.Core.Tests.Control;

public sealed class ControlCommandTests
{
    private static AppSettings Settings(LightingMode mode, double brightness = 0.4) =>
        new AppSettings { Lighting = new LightingSettings { Mode = mode, Brightness = brightness } }.Normalised();

    private static ControlCommand Parse(string line)
    {
        Assert.True(ControlCommand.TryParse(line, out var command, out string error), error);
        return command;
    }

    [Theory]
    [InlineData("status", "status")]
    [InlineData("  TOGGLE ", "toggle")]
    [InlineData("light", "light")]
    [InlineData("light 3000", "light 3000")]
    [InlineData("light 3000K", "light 3000")]
    [InlineData("light warm", "light 2700")]
    [InlineData("colour red", "colour #ff0000")]
    [InlineData("colour #00FF80", "colour #00ff80")]
    [InlineData("effect Candle", "effect candle")]
    [InlineData("brightness 60", "brightness 60")]
    [InlineData("brightness 60%", "brightness 60")]
    [InlineData("brightness +10", "brightness +10")]
    [InlineData("brightness -5", "brightness -5")]
    public void TryParse_ReadsWhatToStringWrites(string line, string expected)
    {
        var command = Parse(line);

        Assert.Equal(expected, command.ToString());
        Assert.Equal(command, Parse(command.ToString()));
    }

    [Theory]
    [InlineData("")]
    [InlineData("dance")]
    [InlineData("1")]
    [InlineData("on,off")]
    [InlineData("on now")]
    [InlineData("light 1000")]
    [InlineData("light 9000")]
    [InlineData("colour mauve")]
    [InlineData("effect 2")]
    [InlineData("brightness")]
    [InlineData("brightness 120")]
    [InlineData("brightness +0")]
    [InlineData("brightness bright")]
    public void TryParse_RejectsNonsenseAndSaysWhy(string line)
    {
        Assert.False(ControlCommand.TryParse(line, out _, out string error));
        Assert.NotEmpty(error);
    }

    [Fact]
    public void Toggle_TurnsOffAndBackOnInTheSameMode()
    {
        var toggle = Parse("toggle");

        var off = toggle.ApplyTo(Settings(LightingMode.Effect));
        var on = toggle.ApplyTo(off);

        Assert.Equal(LightingMode.Off, off.Lighting.Mode);
        Assert.Equal(LightingMode.Effect, on.Lighting.Mode);
    }

    [Theory]
    [InlineData(LightingMode.Light, LightingMode.Colour)]
    [InlineData(LightingMode.Effect, LightingMode.Ambient)]
    [InlineData(LightingMode.Ambient, LightingMode.Light)]
    public void Next_CyclesThroughTheModes(LightingMode from, LightingMode expected)
    {
        Assert.Equal(expected, Parse("next").ApplyTo(Settings(from)).Lighting.Mode);
    }

    [Fact]
    public void Next_WhileOff_TurnsOnInTheLastMode()
    {
        var off = Settings(LightingMode.Colour).WithLightsOn(false);

        Assert.Equal(LightingMode.Colour, Parse("next").ApplyTo(off).Lighting.Mode);
    }

    [Fact]
    public void ModeCommands_SwitchModeAndKeepTheOtherSettings()
    {
        var settings = Settings(LightingMode.Ambient) with { Lighting = Settings(LightingMode.Ambient).Lighting with { Kelvin = 4000 } };

        var light = Parse("light").ApplyTo(settings);
        var warm = Parse("light 2200").ApplyTo(settings);
        var colour = Parse("colour blue").ApplyTo(settings);
        var effect = Parse("effect chase").ApplyTo(settings);

        Assert.Equal((LightingMode.Light, 4000), (light.Lighting.Mode, light.Lighting.Kelvin));
        Assert.Equal((LightingMode.Light, 2200), (warm.Lighting.Mode, warm.Lighting.Kelvin));
        Assert.Equal((LightingMode.Colour, new Rgb(0, 0, 255)), (colour.Lighting.Mode, colour.Lighting.Colour));
        Assert.Equal((LightingMode.Effect, EffectKind.Chase), (effect.Lighting.Mode, effect.Lighting.Effect));
        Assert.Equal(LightingMode.Effect, effect.LastMode);
    }

    [Theory]
    [InlineData(40, "brightness 75", 75)]
    [InlineData(40, "brightness +10", 50)]
    [InlineData(37, "brightness +10", 40)]
    [InlineData(37, "brightness -10", 30)]
    [InlineData(95, "brightness +10", 100)]
    [InlineData(10, "brightness -10", 5)]
    [InlineData(3, "brightness -10", 3)]
    public void Brightness_SetsOrStepsToWholeSteps(int from, string line, int expected)
    {
        var after = Parse(line).ApplyTo(Settings(LightingMode.Light, from / 100.0));

        Assert.Equal(expected, (int)Math.Round(after.Lighting.Brightness * 100));
    }

    [Theory]
    [InlineData("status")]
    [InlineData("identify")]
    [InlineData("reconnect")]
    [InlineData("show")]
    public void EngineCommands_LeaveTheSettingsAlone(string line)
    {
        var command = Parse(line);
        var settings = Settings(LightingMode.Light);

        Assert.False(command.ChangesSettings);
        Assert.Equal(settings, command.ApplyTo(settings));
    }

    [Fact]
    public void Reply_RoundTripsAndDescribesTheMode()
    {
        var settings = Settings(LightingMode.Light, 0.6) with { Lighting = Settings(LightingMode.Light, 0.6).Lighting with { Kelvin = 2700 } };
        var reply = ControlReply.From(settings, new EngineStatus(EngineState.Running, "Light on 8 sticks", []));

        var parsed = ControlReply.Parse(reply.ToJson());

        Assert.Equal(reply, parsed);
        Assert.Equal("Light at 2700 K, 60% brightness", parsed.Describe());
        Assert.Equal("running", parsed.State);
        Assert.DoesNotContain("error", reply.ToJson(), StringComparison.Ordinal);
    }

    [Fact]
    public void Reply_WhileOff_SaysWhatComesBack()
    {
        var reply = ControlReply.From(Settings(LightingMode.Ambient).WithLightsOn(false), EngineStatus.Off);

        Assert.Equal("Off (turning on goes back to ambient)", reply.Describe());
    }
}
