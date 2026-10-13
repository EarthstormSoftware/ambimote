using System.Buffers.Binary;

using AmbiMote.Core.Capture;

namespace AmbiMote.Core.Tests.Capture;

public class BmpWriterTests
{
    [Fact]
    public void Write_TopDown24BitWithPaddedRows()
    {
        var image = new BgraImage(2, 2);
        image.SetPixel(0, 0, new Rgb(1, 2, 3));
        image.SetPixel(1, 0, new Rgb(4, 5, 6));
        image.SetPixel(0, 1, new Rgb(7, 8, 9));
        image.SetPixel(1, 1, new Rgb(10, 11, 12));
        using var stream = new MemoryStream();

        BmpWriter.Write(image, stream);

        var bmp = stream.ToArray();
        Assert.Equal(54 + (2 * 8), bmp.Length);
        Assert.Equal("BM"u8.ToArray(), bmp[..2]);
        Assert.Equal(bmp.Length, BinaryPrimitives.ReadInt32LittleEndian(bmp.AsSpan(2)));
        Assert.Equal(2, BinaryPrimitives.ReadInt32LittleEndian(bmp.AsSpan(18)));
        Assert.Equal(-2, BinaryPrimitives.ReadInt32LittleEndian(bmp.AsSpan(22)));
        Assert.Equal(24, BinaryPrimitives.ReadInt16LittleEndian(bmp.AsSpan(28)));
        Assert.Equal(new byte[] { 3, 2, 1, 6, 5, 4, 0, 0, 9, 8, 7, 12, 11, 10, 0, 0 }, bmp[54..]);
    }
}
