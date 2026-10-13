using AmbiMote.Core.Config;
using AmbiMote.Core.Sampling;

namespace AmbiMote.Core.Tests.Config;

public class AmbiMoteConfigTests
{
    private const string Valid = """
        {
          // Comments are allowed.
          "hosts": [ { "id": "left", "instanceId": "USB\\VID_16D0&PID_08C4\\1" } ],
          "sticks": [
            { "host": "left", "channel": 1, "monitor": "\\\\.\\DISPLAY1", "edge": "top", "from": 0.5, "to": 0 },
            { "host": "LEFT", "channel": 4, "monitor": "\\\\.\\DISPLAY3", "edge": "left", "from": 0, "to": 1 },
          ],
        }
        """;

    [Fact]
    public void Parse_ReadsHostsAndSticks()
    {
        var config = AmbiMoteConfig.Parse(Valid);

        Assert.Equal(new HostConfig("left", @"USB\VID_16D0&PID_08C4\1"), Assert.Single(config.Hosts));
        Assert.Equal(new StickConfig("left", 1, @"\\.\DISPLAY1", ScreenEdge.Top, 0.5, 0), config.Sticks[0]);
        Assert.Equal(ScreenEdge.Left, config.Sticks[1].Edge);
    }

    [Fact]
    public void ToJson_RoundTrips()
    {
        var config = AmbiMoteConfig.Parse(Valid);

        var again = AmbiMoteConfig.Parse(config.ToJson());

        Assert.Equal(config.Hosts, again.Hosts);
        Assert.Equal(config.Sticks, again.Sticks);
    }

    [Theory]
    [InlineData("not json", "not valid JSON")]
    [InlineData("""{ "hosts": [ { "id": "a" } ], "sticks": [] }""", "not valid JSON")]
    [InlineData("""{ "hosts": [ { "id": "a", "instanceId": "x" }, { "id": "A", "instanceId": "y" } ], "sticks": [] }""", "two hosts")]
    [InlineData("""{ "hosts": [], "sticks": [ { "host": "b", "channel": 1, "monitor": "m", "edge": "top", "from": 0, "to": 1 } ] }""", "no host")]
    [InlineData("""{ "hosts": [ { "id": "a", "instanceId": "x" } ], "sticks": [ { "host": "a", "channel": 5, "monitor": "m", "edge": "top", "from": 0, "to": 1 } ] }""", "channel")]
    [InlineData("""{ "hosts": [ { "id": "a", "instanceId": "x" } ], "sticks": [ { "host": "a", "channel": 1, "monitor": "m", "edge": "top", "from": 0, "to": 1.5 } ] }""", "between 0 and 1")]
    [InlineData("""{ "hosts": [ { "id": "a", "instanceId": "x" } ], "sticks": [ { "host": "a", "channel": 1, "monitor": "m", "edge": "middle", "from": 0, "to": 1 } ] }""", "not valid JSON")]
    [InlineData("""{ "hosts": [ { "id": "a", "instanceId": "x" } ], "sticks": [ { "host": "a", "channel": 1, "monitor": "m", "edge": "top", "to": 1 } ] }""", "not valid JSON")]
    [InlineData("""
        { "hosts": [ { "id": "a", "instanceId": "x" } ], "sticks": [
          { "host": "a", "channel": 1, "monitor": "m", "edge": "top", "from": 0, "to": 1 },
          { "host": "a", "channel": 1, "monitor": "m", "edge": "left", "from": 0, "to": 1 } ] }
        """, "listed twice")]
    public void Parse_RejectsBadSettings(string json, string message)
    {
        var ex = Assert.Throws<InvalidDataException>(() => AmbiMoteConfig.Parse(json));
        Assert.Contains(message, ex.Message);
    }

    [Fact]
    public void Load_MissingFile_ReturnsNull()
    {
        Assert.Null(AmbiMoteConfig.Load(Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"), "config.json")));
    }

    [Fact]
    public void ExampleFile_IsValid()
    {
        string path = Path.Combine(FindRepoRoot(), "config.example.json");

        var config = AmbiMoteConfig.Load(path);

        Assert.NotNull(config);
        Assert.NotEmpty(config.Sticks);
    }

    private static string FindRepoRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "AmbiMote.slnx")))
        {
            dir = dir.Parent;
        }

        return dir?.FullName ?? throw new InvalidOperationException("Could not find the repository root.");
    }
}
