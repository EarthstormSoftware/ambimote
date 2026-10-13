namespace AmbiMote.Core.Capture;

/// <summary>A small image in BGRA byte order, packed with no row padding.</summary>
public sealed class BgraImage
{
    public BgraImage(int width, int height)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(width);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(height);
        Width = width;
        Height = height;
        Pixels = new byte[width * height * 4];
    }

    public int Width { get; }

    public int Height { get; }

    /// <summary>Bytes per row.</summary>
    public int Stride => Width * 4;

    public byte[] Pixels { get; }

    public Rgb GetPixel(int x, int y)
    {
        int i = (y * Stride) + (x * 4);
        return new Rgb(Pixels[i + 2], Pixels[i + 1], Pixels[i]);
    }

    public void SetPixel(int x, int y, Rgb colour)
    {
        int i = (y * Stride) + (x * 4);
        Pixels[i] = colour.B;
        Pixels[i + 1] = colour.G;
        Pixels[i + 2] = colour.R;
        Pixels[i + 3] = 255;
    }
}
