using AmbiMote.Core.Mote;

namespace AmbiMote.Core.Tests.Mote;

public class MoteProtocolTests
{
    [Fact]
    public void ConfigureChannel_MatchesPimoroniLibrary()
    {
        byte[] command = MoteProtocol.ConfigureChannel(2, 16, gammaCorrection: false);

        Assert.Equal("motec"u8.ToArray().Concat(new byte[] { 2, 16, 0 }), command);
    }

    [Fact]
    public void ConfigureChannel_SetsGammaFlag()
    {
        Assert.Equal(1, MoteProtocol.ConfigureChannel(1, 16, gammaCorrection: true)[^1]);
    }

    [Theory]
    [InlineData(0, 16)]
    [InlineData(5, 16)]
    [InlineData(1, 0)]
    [InlineData(1, 129)]
    public void ConfigureChannel_RejectsOutOfRange(int channel, int pixelCount)
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => MoteProtocol.ConfigureChannel(channel, pixelCount, false));
    }

    [Fact]
    public void Output_SendsBlueGreenRedInChannelOrderAndSkipsUnconfiguredChannels()
    {
        Rgb[]?[] channels =
        [
            [new Rgb(1, 2, 3)],
            null,
            [new Rgb(4, 5, 6), new Rgb(7, 8, 9)],
        ];

        byte[] command = MoteProtocol.Output(channels);

        Assert.Equal("moteo"u8.ToArray().Concat(new byte[] { 3, 2, 1, 6, 5, 4, 9, 8, 7 }), command);
    }

    [Fact]
    public void Output_RejectsMoreThanFourChannels()
    {
        Assert.Throws<ArgumentException>(() => MoteProtocol.Output(new Rgb[]?[5]));
    }
}
