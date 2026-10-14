using AmbiMote.Core.Capture;
using AmbiMote.Core.Sampling;

namespace AmbiMote.Core.Tests.Sampling;

public class EdgeSamplerTests
{
    /// <summary>An image where each pixel's red is its x and green is its y, so averages are easy to check.</summary>
    private static BgraImage Gradient(int width, int height)
    {
        var image = new BgraImage(width, height);
        for (int y = 0; y < height; y++)
        {
            for (int x = 0; x < width; x++)
            {
                image.SetPixel(x, y, new Rgb((byte)(x * 10), (byte)(y * 10), 7));
            }
        }

        return image;
    }

    [Fact]
    public void Top_AveragesEqualStripsAcrossTheTopRows()
    {
        var image = Gradient(8, 10);
        var pixels = new Rgb[4];

        EdgeSampler.Sample(image, ScreenEdge.Top, 0, 1, 0.2, pixels);

        // Each LED covers two columns (x, x+1) and the top two rows (y = 0, 1).
        Assert.Equal([new(5, 5, 7), new(25, 5, 7), new(45, 5, 7), new(65, 5, 7)], pixels);
    }

    [Fact]
    public void Bottom_UsesTheBottomRows()
    {
        var image = Gradient(4, 10);
        var pixels = new Rgb[1];

        EdgeSampler.Sample(image, ScreenEdge.Bottom, 0, 1, 0.1, pixels);

        Assert.Equal(new Rgb(15, 90, 7), pixels[0]);
    }

    [Fact]
    public void Left_RunsTopToBottom()
    {
        var image = Gradient(10, 4);
        var pixels = new Rgb[2];

        EdgeSampler.Sample(image, ScreenEdge.Left, 0, 1, 0.1, pixels);

        Assert.Equal([new(0, 5, 7), new(0, 25, 7)], pixels);
    }

    [Fact]
    public void Right_UsesTheRightColumns()
    {
        var image = Gradient(10, 4);
        var pixels = new Rgb[1];

        EdgeSampler.Sample(image, ScreenEdge.Right, 0, 1, 0.1, pixels);

        Assert.Equal(new Rgb(90, 15, 7), pixels[0]);
    }

    [Fact]
    public void FromGreaterThanTo_ReversesTheStick()
    {
        var image = Gradient(8, 4);
        var forward = new Rgb[4];
        var backward = new Rgb[4];

        EdgeSampler.Sample(image, ScreenEdge.Top, 0, 1, 0.25, forward);
        EdgeSampler.Sample(image, ScreenEdge.Top, 1, 0, 0.25, backward);

        Assert.Equal(forward.Reverse(), backward);
    }

    [Fact]
    public void PartOfAnEdge_OnlySamplesThatPart()
    {
        var image = Gradient(10, 4);
        var pixels = new Rgb[2];

        EdgeSampler.Sample(image, ScreenEdge.Top, 0.5, 0.9, 0.25, pixels);

        Assert.Equal([new(55, 0, 7), new(75, 0, 7)], pixels);
    }

    [Fact]
    public void MoreLedsThanImagePixels_EachLedStillGetsAColour()
    {
        var image = Gradient(4, 4);
        var pixels = new Rgb[16];

        EdgeSampler.Sample(image, ScreenEdge.Top, 0, 1, 0.25, pixels);

        Assert.Equal(new Rgb(0, 0, 7), pixels[0]);
        Assert.Equal(new Rgb(30, 0, 7), pixels[15]);
    }

    [Fact]
    public void TinyDepth_UsesAtLeastOneRow()
    {
        var image = Gradient(4, 100);
        var pixels = new Rgb[1];

        EdgeSampler.Sample(image, ScreenEdge.Top, 0, 1, 0.001, pixels);

        Assert.Equal(new Rgb(15, 0, 7), pixels[0]);
    }

    [Theory]
    [InlineData(-0.1, 1, 0.1)]
    [InlineData(0, 1.1, 0.1)]
    [InlineData(0, 1, 0)]
    [InlineData(0, 1, 1.5)]
    [InlineData(double.NaN, 1, 0.1)]
    public void InvalidArguments_Throw(double from, double to, double depth)
    {
        var image = Gradient(4, 4);

        Assert.Throws<ArgumentOutOfRangeException>(() => EdgeSampler.Sample(image, ScreenEdge.Top, from, to, depth, new Rgb[4]));
    }

    /// <summary>A 10 x 10 image: the top row red, column <paramref name="column"/> below it blue, the rest black.</summary>
    private static BgraImage RedTopBlueColumn(int column)
    {
        var image = new BgraImage(10, 10);
        for (int y = 0; y < 10; y++)
        {
            for (int x = 0; x < 10; x++)
            {
                image.SetPixel(x, y, y == 0 ? new Rgb(200, 0, 0) : x == column ? new Rgb(0, 0, 200) : Rgb.Black);
            }
        }

        return image;
    }

    [Theory]
    [InlineData(ScreenEdge.Left, 0)]
    [InlineData(ScreenEdge.Right, 9)]
    public void Extend_TopFadesFromTheCornerNextToTheImageToItsSideEdge(ScreenEdge side, int column)
    {
        var image = RedTopBlueColumn(column);
        var pixels = new Rgb[2];

        EdgeSampler.Extend(image, side, ScreenEdge.Top, 0, 1, 0.1, pixels);

        // The corner is red; the top half of the side edge is one red row and four blue: (40, 0, 160).
        // A monitor on the left meets the image at its right end, so the fade runs the other way.
        Rgb near = new(160, 0, 40), far = new(80, 0, 120);
        Assert.Equal(side == ScreenEdge.Left ? [far, near] : new[] { near, far }, pixels);
    }

    [Fact]
    public void Extend_SideStickCopiesTheImagesSideEdgeAtTheSameHeights()
    {
        var image = Gradient(10, 8);
        var extended = new Rgb[4];
        var sampled = new Rgb[4];

        EdgeSampler.Extend(image, ScreenEdge.Right, ScreenEdge.Right, 0, 1, 0.1, extended);
        EdgeSampler.Sample(image, ScreenEdge.Right, 0, 1, 0.1, sampled);

        Assert.Equal(sampled, extended);
    }

    [Fact]
    public void Extend_RejectsAMonitorAboveOrBelow()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => EdgeSampler.Extend(Gradient(4, 4), ScreenEdge.Top, ScreenEdge.Top, 0, 1, 0.1, new Rgb[2]));
    }
}
