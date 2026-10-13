using System.Buffers.Binary;

namespace AmbiMote.Core.Capture;

/// <summary>Saves a <see cref="BgraImage"/> as a 24-bit BMP, to check what was captured.</summary>
public static class BmpWriter
{
    public static void Write(BgraImage image, Stream stream)
    {
        ArgumentNullException.ThrowIfNull(image);
        ArgumentNullException.ThrowIfNull(stream);

        int rowSize = ((image.Width * 3) + 3) & ~3;
        int dataSize = rowSize * image.Height;
        var header = new byte[54];
        header[0] = (byte)'B';
        header[1] = (byte)'M';
        BinaryPrimitives.WriteInt32LittleEndian(header.AsSpan(2), header.Length + dataSize);
        BinaryPrimitives.WriteInt32LittleEndian(header.AsSpan(10), header.Length);
        BinaryPrimitives.WriteInt32LittleEndian(header.AsSpan(14), 40);
        BinaryPrimitives.WriteInt32LittleEndian(header.AsSpan(18), image.Width);
        BinaryPrimitives.WriteInt32LittleEndian(header.AsSpan(22), -image.Height); // Top-down rows.
        BinaryPrimitives.WriteInt16LittleEndian(header.AsSpan(26), 1);
        BinaryPrimitives.WriteInt16LittleEndian(header.AsSpan(28), 24);
        BinaryPrimitives.WriteInt32LittleEndian(header.AsSpan(34), dataSize);
        stream.Write(header);

        var row = new byte[rowSize];
        for (int y = 0; y < image.Height; y++)
        {
            for (int x = 0; x < image.Width; x++)
            {
                int p = (y * image.Stride) + (x * 4);
                row[x * 3] = image.Pixels[p];
                row[(x * 3) + 1] = image.Pixels[p + 1];
                row[(x * 3) + 2] = image.Pixels[p + 2];
            }

            stream.Write(row);
        }
    }
}
