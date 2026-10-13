using AmbiMote.Core.Capture;

namespace AmbiMote.Core.Tests.Capture;

public class DesktopDuplicationCaptureTests
{
    [Theory]
    [InlineData(2560, 1440, 128, 5u, 80, 45)]
    [InlineData(3840, 2160, 128, 5u, 120, 67)]
    [InlineData(1920, 1080, 128, 4u, 120, 67)]
    [InlineData(3440, 1440, 128, 5u, 107, 45)]
    [InlineData(100, 50, 128, 0u, 100, 50)]
    [InlineData(1, 1, 128, 0u, 1, 1)]
    [InlineData(4, 1, 1, 2u, 1, 1)]
    public void ChooseMipLevel_PicksTheFirstLevelThatFits(int width, int height, int maxSize, uint level, int levelWidth, int levelHeight)
    {
        Assert.Equal((level, levelWidth, levelHeight), DesktopDuplicationCapture.ChooseMipLevel(width, height, maxSize));
    }
}
