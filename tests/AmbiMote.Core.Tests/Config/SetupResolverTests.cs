using AmbiMote.Core.Capture;
using AmbiMote.Core.Config;
using AmbiMote.Core.Mote;
using AmbiMote.Core.Sampling;

namespace AmbiMote.Core.Tests.Config;

public class SetupResolverTests
{
    private static readonly AmbiMoteConfig s_config = new(
        [new HostConfig("left", "USB\\A"), new HostConfig("right", "USB\\B")],
        [
            new StickConfig("left", 1, @"\\.\DISPLAY1", ScreenEdge.Top, 0.5, 0),
            new StickConfig("left", 4, @"\\.\DISPLAY3", ScreenEdge.Left, 0, 1),
            new StickConfig("right", 1, @"\\.\DISPLAY1", ScreenEdge.Top, 0.5, 1),
        ]);

    private static readonly DisplayOutput s_display1 = new(@"\\.\DISPLAY1", 0, 0, 2560, 1440, false, "gpu");
    private static readonly DisplayOutput s_display3 = new(@"\\.\DISPLAY3", -1920, 0, 1920, 1080, false, "gpu");

    [Fact]
    public void Resolve_EverythingPresent_UsesEveryStick()
    {
        MoteDeviceInfo a = new("COM10", "usb\\a", null), b = new("COM11", "USB\\B", null);

        var setup = SetupResolver.Resolve(s_config, [a, b], [s_display1, s_display3]);

        Assert.Empty(setup.Problems);
        Assert.Equal(
            [(a, s_display1), (a, s_display3), (b, s_display1)],
            setup.Sticks.Select(static s => (s.Host, s.Monitor)));
    }

    [Fact]
    public void Resolve_MissingHost_SkipsItsSticksAndSaysSo()
    {
        var setup = SetupResolver.Resolve(s_config, [new MoteDeviceInfo("COM10", "USB\\A", null)], [s_display1, s_display3]);

        Assert.Equal(2, setup.Sticks.Count);
        Assert.Contains("'right' is not plugged in", Assert.Single(setup.Problems));
    }

    [Fact]
    public void Resolve_MissingMonitor_SkipsItsSticksAndSaysSoOnce()
    {
        var setup = SetupResolver.Resolve(
            s_config, [new MoteDeviceInfo("COM10", "USB\\A", null), new MoteDeviceInfo("COM11", "USB\\B", null)], [s_display3]);

        Assert.Equal(4, Assert.Single(setup.Sticks).Stick.Channel);
        Assert.Contains(@"\\.\DISPLAY1 is not attached", Assert.Single(setup.Problems));
    }
}
