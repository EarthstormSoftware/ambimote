using AmbiMote.Core.Mote;

namespace AmbiMote.Core.Tests.Mote;

public class MoteDiscoveryTests
{
    [Theory]
    [InlineData("VID_16D0&PID_08C4", true)]
    [InlineData("vid_16d0&pid_08c4&MI_00", true)]
    [InlineData("VID_16D0&PID_08C3", false)]
    [InlineData("VID_2E8A&PID_0005", false)]
    public void IsMoteDeviceKey_MatchesTheMoteVendorAndProduct(string deviceKey, bool expected)
    {
        Assert.Equal(expected, MoteDiscovery.IsMoteDeviceKey(deviceKey));
    }

    [Theory]
    [InlineData("A1B2C3D4", "A1B2C3D4")]
    [InlineData("7&1234ABCD&0&2", null)]
    [InlineData("", null)]
    public void SerialNumberFromInstanceId_IgnoresLocationBasedIds(string instanceId, string? expected)
    {
        Assert.Equal(expected, MoteDiscovery.SerialNumberFromInstanceId(instanceId));
    }

    [Fact]
    public void Select_KeepsOnlyPresentPortsInPortOrder()
    {
        var entries = new (string, string, string?)[]
        {
            ("VID_16D0&PID_08C4", "SERIAL-B", "COM12"),
            ("VID_16D0&PID_08C4", "SERIAL-A", "COM5"),
            ("VID_16D0&PID_08C4", "UNPLUGGED", "COM7"),
            ("VID_16D0&PID_08C4", "NO-PORT", null),
        };

        var hosts = MoteDiscovery.Select(entries, ["COM1", "com5", "COM12"]);

        Assert.Collection(
            hosts,
            h =>
            {
                Assert.Equal("COM5", h.PortName);
                Assert.Equal("SERIAL-A", h.SerialNumber);
                Assert.Equal(@"USB\VID_16D0&PID_08C4\SERIAL-A", h.InstanceId);
            },
            h => Assert.Equal("COM12", h.PortName));
    }

    [Fact]
    public void Select_DoesNotTreatCompositeInterfaceIdsAsSerialNumbers()
    {
        var entries = new (string, string, string?)[] { ("VID_16D0&PID_08C4&MI_00", "ABCDEF", "COM5") };

        Assert.Null(Assert.Single(MoteDiscovery.Select(entries, ["COM5"])).SerialNumber);
    }
}
