using AmbiMote.Core.Capture;
using AmbiMote.Core.Config;
using AmbiMote.Core.Engine;
using AmbiMote.Core.Mote;
using AmbiMote.Core.Sampling;

namespace AmbiMote.Core.Tests.Engine;

public class LightingEngineTests
{
    private const string Display1 = @"\\.\DISPLAY1";

    private sealed class RecordingTransport : IMoteTransport
    {
        public int Writes { get; private set; }

        public bool IsOpen { get; set; } = true;

        public bool Disposed { get; private set; }

        public void Write(ReadOnlySpan<byte> data)
        {
            if (!IsOpen)
            {
                throw new IOException("The port is closed.");
            }

            Writes++;
        }

        public void Dispose() => Disposed = true;
    }

    private sealed class FakeCapture : IScreenCapture
    {
        public Queue<BgraImage> Frames { get; } = new();

        public bool Disposed { get; private set; }

        public bool TryCapture(TimeSpan timeout, out BgraImage? image)
        {
            image = Frames.Count > 0 ? Frames.Dequeue() : null;
            return image is not null;
        }

        public void Dispose() => Disposed = true;
    }

    private sealed class FakeHardware : ILightingHardware
    {
        public AmbiMoteConfig? Config { get; set; }

        public Exception? ConfigError { get; set; }

        public List<MoteDeviceInfo> Plugged { get; } = [];

        public Dictionary<string, (MoteHost Host, RecordingTransport Transport)> Opened { get; } = [];

        public List<DisplayOutput> Monitors { get; } = [new DisplayOutput(Display1, 0, 0, 1920, 1080, false, "GPU")];

        public List<FakeCapture> Captures { get; } = [];

        public Queue<BgraImage> NextFrames { get; } = new();

        public AmbiMoteConfig? LoadConfig() => ConfigError is null ? Config : throw ConfigError;

        public IReadOnlyList<MoteDeviceInfo> FindHosts() => [.. Plugged];

        public MoteHost OpenHost(MoteDeviceInfo info)
        {
            var transport = new RecordingTransport();
            var host = new MoteHost(transport);
            Opened[info.PortName] = (host, transport);
            return host;
        }

        public IReadOnlyList<DisplayOutput> ListMonitors() => Monitors;

        public IScreenCapture OpenCapture(string deviceName)
        {
            var capture = new FakeCapture();
            while (NextFrames.Count > 0)
            {
                capture.Frames.Enqueue(NextFrames.Dequeue());
            }

            Captures.Add(capture);
            return capture;
        }
    }

    private static MoteDeviceInfo Info(string port) => new(port, $@"USB\VID_16D0&PID_08C4\0&0&{port}", null);

    private static AmbiMoteConfig OneStickConfig(params string[] ports) => new(
        [.. ports.Select(p => new HostConfig(p, Info(p).InstanceId))],
        [new StickConfig(ports[0], 1, Display1, ScreenEdge.Top, 0, 1)]);

    private static BgraImage Filled(Rgb colour)
    {
        var image = new BgraImage(8, 8);
        for (int y = 0; y < 8; y++)
        {
            for (int x = 0; x < 8; x++)
            {
                image.SetPixel(x, y, colour);
            }
        }

        return image;
    }

    private static (LightingEngine Engine, FakeHardware Hardware) Create(LightingSettings settings, params string[] ports)
    {
        var hardware = new FakeHardware();
        hardware.Plugged.AddRange(ports.Select(Info));
        return (new LightingEngine(hardware, settings), hardware);
    }

    [Fact]
    public void Colour_WithoutConfig_LightsEveryChannelAndSendsOnlyWhenSomethingChanges()
    {
        var colour = new Rgb(10, 20, 30);
        var (engine, hardware) = Create(new LightingSettings { Mode = LightingMode.Colour, Colour = colour }, "COM5");

        var wait = engine.Tick(0);
        var (host, transport) = hardware.Opened["COM5"];
        int afterFirst = transport.Writes;
        engine.Tick(1);
        engine.Tick(2);

        Assert.Equal(LightingEngine.SteadyCheck, wait);
        Assert.Equal(EngineState.Running, engine.Status.State);
        Assert.Equal("4 sticks on 1 Mote host (in channel order: no stick mapping)", engine.Status.Message);
        for (int channel = 1; channel <= MoteProtocol.ChannelCount; channel++)
        {
            Assert.All(host.GetPixels(channel).ToArray(), p => Assert.Equal(colour, p));
        }

        Assert.Equal(afterFirst, transport.Writes);

        engine.Apply(engine.Settings with { Brightness = 0.8 });
        engine.Tick(3);

        Assert.Equal(afterFirst + 1, transport.Writes);
        Assert.Equal(0.8 * 0.8, host.Brightness, precision: 9);
    }

    [Fact]
    public void Light_ShowsTheColourTemperature()
    {
        var (engine, hardware) = Create(new LightingSettings { Mode = LightingMode.Light, Kelvin = 2700 }, "COM5");

        engine.Tick(0);

        Assert.Equal(Rgb.FromKelvin(2700), hardware.Opened["COM5"].Host.GetPixels(2)[7]);
    }

    [Fact]
    public void Off_TurnsTheSticksOffAndClosesTheHosts()
    {
        var (engine, hardware) = Create(new LightingSettings { Mode = LightingMode.Colour }, "COM5");
        engine.Tick(0);
        var (host, transport) = hardware.Opened["COM5"];

        engine.Apply(engine.Settings with { Mode = LightingMode.Off });
        var wait = engine.Tick(1);

        Assert.Equal(Timeout.InfiniteTimeSpan, wait);
        Assert.Equal(EngineStatus.Off, engine.Status);
        Assert.Equal(Rgb.Black, host.GetPixels(1)[0]);
        Assert.True(transport.Disposed);
    }

    [Fact]
    public void Effect_SendsEveryFrame()
    {
        var (engine, hardware) = Create(new LightingSettings { Mode = LightingMode.Effect, Effect = EffectKind.Rainbow }, "COM5");

        var wait = engine.Tick(0);
        int afterFirst = hardware.Opened["COM5"].Transport.Writes;
        engine.Tick(0.1);

        Assert.Equal(LightingEngine.EffectFrameTime, wait);
        Assert.Equal(afterFirst + 1, hardware.Opened["COM5"].Transport.Writes);
    }

    [Fact]
    public void NoHosts_WaitsAndTriesAgainLater()
    {
        var (engine, hardware) = Create(new LightingSettings { Mode = LightingMode.Colour });

        var wait = engine.Tick(0);
        hardware.Plugged.Add(Info("COM5"));
        engine.Tick(1);

        Assert.Equal(TimeSpan.FromSeconds(LightingEngine.RetrySeconds), wait);
        Assert.Equal(EngineState.Waiting, engine.Status.State);
        Assert.Empty(hardware.Opened);

        engine.Tick(LightingEngine.RetrySeconds);

        Assert.Equal(EngineState.Running, engine.Status.State);
        Assert.Contains("COM5", hardware.Opened.Keys);
    }

    [Fact]
    public void Unplugged_ReportsItAndOpensTheHostAgain()
    {
        var (engine, hardware) = Create(new LightingSettings { Mode = LightingMode.Colour }, "COM5");
        engine.Tick(0);
        var first = hardware.Opened["COM5"].Transport;

        first.IsOpen = false;
        engine.Tick(1);

        Assert.Equal(EngineState.Waiting, engine.Status.State);
        Assert.True(first.Disposed);

        engine.Tick(1 + LightingEngine.RetrySeconds);

        Assert.Equal(EngineState.Running, engine.Status.State);
        Assert.NotSame(first, hardware.Opened["COM5"].Transport);
    }

    [Fact]
    public void MissingHost_IsPickedUpWhenItArrives()
    {
        var (engine, hardware) = Create(new LightingSettings { Mode = LightingMode.Colour }, "COM5");
        hardware.Config = OneStickConfig("COM5", "COM6");
        engine.Tick(0);

        Assert.Single(hardware.Opened);
        Assert.Contains(engine.Status.Problems, static p => p.Contains("COM6", StringComparison.Ordinal));

        hardware.Plugged.Add(Info("COM6"));
        engine.Tick(LightingEngine.RetrySeconds);

        Assert.Equal(2, hardware.Opened.Count);
        Assert.Empty(engine.Status.Problems);
    }

    [Fact]
    public void InvalidConfig_IsAnError()
    {
        var (engine, hardware) = Create(new LightingSettings { Mode = LightingMode.Colour }, "COM5");
        hardware.ConfigError = new InvalidDataException("Bad.");

        engine.Tick(0);

        Assert.Equal(EngineState.Error, engine.Status.State);
        Assert.Contains("Bad.", engine.Status.Message, StringComparison.Ordinal);
        Assert.Empty(hardware.Opened);
    }

    [Fact]
    public void Ambient_WithoutConfig_IsAnErrorAndTheSticksStayOff()
    {
        var (engine, hardware) = Create(new LightingSettings { Mode = LightingMode.Colour, Colour = new Rgb(9, 9, 9) }, "COM5");
        engine.Tick(0);

        engine.Apply(engine.Settings with { Mode = LightingMode.Ambient });
        engine.Tick(1);

        Assert.Equal(EngineState.Error, engine.Status.State);
        Assert.Contains("config.example.json", engine.Status.Message, StringComparison.Ordinal);
        Assert.Equal(Rgb.Black, hardware.Opened["COM5"].Host.GetPixels(1)[0]);
    }

    [Fact]
    public void Ambient_LightsTheMappedStickFromTheScreen_AndStopsCapturingInOtherModes()
    {
        var (engine, hardware) = Create(new LightingSettings { Mode = LightingMode.Ambient, Smoothing = 0, Gamma = 1, Saturation = 1 }, "COM5");
        hardware.Config = OneStickConfig("COM5");
        hardware.NextFrames.Enqueue(Filled(new Rgb(100, 50, 0)));

        var wait = engine.Tick(0);
        var host = hardware.Opened["COM5"].Host;

        Assert.Equal(LightingEngine.FrameTime, wait);
        Assert.Equal("1 stick on 1 Mote host", engine.Status.Message);
        Assert.Equal(new Rgb(100, 50, 0), host.GetPixels(1)[0]);
        Assert.Equal(Rgb.Black, host.GetPixels(2)[0]);

        engine.Apply(engine.Settings with { Mode = LightingMode.Colour });
        engine.Tick(1);

        Assert.True(Assert.Single(hardware.Captures).Disposed);
    }

    [Fact]
    public void Ambient_NewGamma_AppliesWithoutANewFrame()
    {
        var (engine, hardware) = Create(new LightingSettings { Mode = LightingMode.Ambient, Smoothing = 0, Gamma = 1, Saturation = 1 }, "COM5");
        hardware.Config = OneStickConfig("COM5");
        hardware.NextFrames.Enqueue(Filled(new Rgb(128, 128, 128)));
        engine.Tick(0);

        engine.Apply(engine.Settings with { Gamma = 2.2 });
        engine.Tick(0.1);

        Assert.Equal(new Rgb(56, 56, 56), hardware.Opened["COM5"].Host.GetPixels(1)[0]);
    }

    [Fact]
    public void Reconnect_OpensTheHostsAgain()
    {
        var (engine, hardware) = Create(new LightingSettings { Mode = LightingMode.Colour }, "COM5");
        engine.Tick(0);
        var first = hardware.Opened["COM5"].Transport;

        engine.Reconnect();
        engine.Tick(0.1);

        Assert.True(first.Disposed);
        Assert.NotSame(first, hardware.Opened["COM5"].Transport);
    }

    [Fact]
    public void Preview_ShowsEachMappedStickWhereItIs_AndGoesDarkWhenOff()
    {
        var colour = new Rgb(10, 20, 30);
        var (engine, hardware) = Create(new LightingSettings { Mode = LightingMode.Colour, Colour = colour }, "COM5");
        hardware.Config = OneStickConfig("COM5");
        engine.PreviewEnabled = true;

        engine.Tick(0);
        var preview = engine.Preview;

        Assert.NotNull(preview);
        var stick = Assert.Single(preview.Sticks);
        Assert.Equal(Display1, stick.Monitor?.DeviceName);
        Assert.Equal(ScreenEdge.Top, stick.Edge);
        Assert.All(stick.Colours, p => Assert.Equal(colour, p));
        Assert.Equal(Display1, Assert.Single(preview.Monitors).DeviceName);

        engine.Apply(engine.Settings with { Mode = LightingMode.Off });
        engine.Tick(1);

        Assert.All(Assert.Single(engine.Preview!.Sticks).Colours, static p => Assert.Equal(Rgb.Black, p));
    }

    [Fact]
    public void Preview_IsNotMadeUnlessEnabled()
    {
        var (engine, _) = Create(new LightingSettings { Mode = LightingMode.Colour }, "COM5");

        engine.Tick(0);

        Assert.Null(engine.Preview);
    }

    [Fact]
    public void Dispose_TurnsTheSticksOff()
    {
        var (engine, hardware) = Create(new LightingSettings { Mode = LightingMode.Colour, Colour = new Rgb(9, 9, 9) }, "COM5");
        engine.Tick(0);

        engine.Dispose();

        Assert.Equal(Rgb.Black, hardware.Opened["COM5"].Host.GetPixels(1)[0]);
        Assert.True(hardware.Opened["COM5"].Transport.Disposed);
    }

    [Fact]
    public void Normalised_ClampsValuesAndReplacesNonsense()
    {
        var settings = new LightingSettings
        {
            Mode = (LightingMode)99,
            Brightness = 7,
            Kelvin = 100,
            EffectSpeed = double.NaN,
            Gamma = 10,
        }.Normalised();

        Assert.Equal(new LightingSettings().Mode, settings.Mode);
        Assert.Equal(1, settings.Brightness);
        Assert.Equal(LightingSettings.MinKelvin, settings.Kelvin);
        Assert.Equal(1, settings.EffectSpeed);
        Assert.Equal(4, settings.Gamma);
    }
}
