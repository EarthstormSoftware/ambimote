// Draws the AmbiMote icons: a monitor showing a sunset, glowing the same colours onto the wall behind it.
// Usage: dotnet run tools/make-icon.cs -- <output folder> [preview folder]
// Writes ambimote.ico (the app) and ambimote-off.ico (the tray icon while the lights are off), with every size drawn
// separately so the small ones stay sharp. With a preview folder, also writes a PNG of each size, and preview.png:
// the tray sizes blown up 8 times, on and off, on a dark and a light taskbar.
using System.IO.Compression;

int[] sizes = [16, 20, 24, 32, 40, 48, 64, 256];
int[] traySizes = [16, 20, 24, 32];
string output = args.Length > 0 ? args[0] : ".";
string? preview = args.Length > 1 ? args[1] : null;
Directory.CreateDirectory(output);
if (preview is not null)
{
    Directory.CreateDirectory(preview);
}

var drawn = new Dictionary<(int Size, bool On), byte[]>();
foreach (bool on in new[] { true, false })
{
    var images = new List<(int Size, byte[] Png)>();
    foreach (int size in sizes)
    {
        byte[] rgba = Icon.Draw(size, on);
        byte[] png = Png.Encode(size, size, rgba);
        drawn[(size, on)] = rgba;
        images.Add((size, png));
        if (preview is not null)
        {
            File.WriteAllBytes(Path.Combine(preview, $"ambimote{(on ? "" : "-off")}-{size}.png"), png);
        }
    }

    File.WriteAllBytes(Path.Combine(output, on ? "ambimote.ico" : "ambimote-off.ico"), Ico.Encode(images));
}

if (preview is not null)
{
    const int zoom = 8;
    const int cell = (32 * zoom) + 16;
    int sheetWidth = cell * traySizes.Length;
    int sheetHeight = cell * 4;
    var sheet = new byte[sheetWidth * sheetHeight * 4];
    (byte, byte, byte)[] backgrounds = [(32, 32, 32), (243, 243, 243)];
    for (int row = 0; row < 4; row++)
    {
        bool on = row % 2 == 0;
        var (br, bg, bb) = backgrounds[row / 2];
        for (int column = 0; column < traySizes.Length; column++)
        {
            int size = traySizes[column];
            byte[] rgba = drawn[(size, on)];
            for (int y = 0; y < cell; y++)
            {
                for (int x = 0; x < cell; x++)
                {
                    int sx = (x - 8) / zoom, sy = (y - 8) / zoom;
                    double r = br, g = bg, b = bb;
                    if (x >= 8 && y >= 8 && sx < size && sy < size)
                    {
                        int i = ((sy * size) + sx) * 4;
                        double a = rgba[i + 3] / 255.0;
                        r = (rgba[i] * a) + (r * (1 - a));
                        g = (rgba[i + 1] * a) + (g * (1 - a));
                        b = (rgba[i + 2] * a) + (b * (1 - a));
                    }

                    int o = ((((row * cell) + y) * sheetWidth) + (column * cell) + x) * 4;
                    sheet[o] = (byte)r;
                    sheet[o + 1] = (byte)g;
                    sheet[o + 2] = (byte)b;
                    sheet[o + 3] = 255;
                }
            }
        }
    }

    File.WriteAllBytes(Path.Combine(preview, "preview.png"), Png.Encode(sheetWidth, sheetHeight, sheet));
}

Console.WriteLine($"Wrote the icons to {Path.GetFullPath(output)}.");

static class Icon
{
    // Sunset colours across the screen, left to right; the glow behind each edge takes the colour of the screen there.
    private static readonly (double At, double R, double G, double B)[] s_sunset =
    [
        (0.00, 255, 140, 30),
        (0.35, 255, 50, 120),
        (0.65, 130, 70, 255),
        (1.00, 20, 200, 255),
    ];

    /// <summary>RGBA pixels, straight alpha, 4x4 supersampled.</summary>
    public static byte[] Draw(int size, bool on)
    {
        const int samples = 4;
        bool small = size <= 24;

        // The monitor, in fractions of the icon. Small icons drop the stand and fill more of the square.
        double width = small ? 0.70 : 0.60;
        double height = small ? 0.50 : 0.40;
        double centreY = small ? 0.50 : 0.44;
        double radius = small ? 0.08 : 0.045;
        double bezel = Math.Max(1.0 / size, small ? 0.07 : 0.03);
        double glowReach = small ? 0.16 : 0.15;

        var rgba = new byte[size * size * 4];
        for (int py = 0; py < size; py++)
        {
            for (int px = 0; px < size; px++)
            {
                double r = 0, g = 0, b = 0, a = 0;
                for (int sy = 0; sy < samples; sy++)
                {
                    for (int sx = 0; sx < samples; sx++)
                    {
                        double x = (px + ((sx + 0.5) / samples)) / size;
                        double y = (py + ((sy + 0.5) / samples)) / size;
                        var (cr, cg, cb, ca) = Sample(x, y);
                        r += cr * ca;
                        g += cg * ca;
                        b += cb * ca;
                        a += ca;
                    }
                }

                int i = ((py * size) + px) * 4;
                if (a > 0)
                {
                    rgba[i] = ToByte(r / a);
                    rgba[i + 1] = ToByte(g / a);
                    rgba[i + 2] = ToByte(b / a);
                }

                rgba[i + 3] = ToByte(255 * a / (samples * samples));
            }
        }

        return rgba;

        (double R, double G, double B, double A) Sample(double x, double y)
        {
            double left = 0.5 - (width / 2);
            double top = centreY - (height / 2);
            double body = RoundBox(x - 0.5, y - centreY, width / 2, height / 2, radius);
            (double R, double G, double B, double A) colour = (0, 0, 0, 0);

            // The glow on the wall: strongest along the top and sides, where the sticks are.
            if (on && body > 0)
            {
                double t = Math.Clamp((x - left) / width, 0, 1);
                var (gr, gg, gb) = Sunset(t);
                double below = Math.Clamp((y - (centreY + (height / 2))) / (glowReach * 2), 0, 1);
                double strength = Math.Exp(-body / (glowReach * 0.5)) * (1 - (0.6 * below));
                colour = (gr, gg, gb, Math.Clamp(strength, 0, 1) * 0.95);
            }

            // The stand.
            if (!small)
            {
                double neck = RoundBox(x - 0.5, y - 0.715, 0.035, 0.06, 0.01);
                double foot = RoundBox(x - 0.5, y - 0.78, 0.14, 0.022, 0.022);
                if (Math.Min(neck, foot) <= 0)
                {
                    colour = Over(colour, on ? (52, 58, 74, 1) : (112, 118, 134, 1));
                }
            }

            if (body <= 0)
            {
                colour = Over(colour, on ? (24, 27, 38, 1) : (112, 118, 134, 1));
                double screen = RoundBox(x - 0.5, y - centreY, (width / 2) - bezel, (height / 2) - bezel, Math.Max(0, radius - bezel));
                if (screen <= 0)
                {
                    double t = Math.Clamp((x - left) / width, 0, 1);
                    double down = Math.Clamp((y - top) / height, 0, 1);
                    var (sr, sg, sb) = Sunset(t);
                    double shade = on ? 1 - (0.45 * down) : 0;
                    colour = on
                        ? (sr * shade, sg * shade, sb * shade, 1)
                        : (50, 55, 68, 1);
                }
            }

            return colour;
        }
    }

    private static (double R, double G, double B) Sunset(double t)
    {
        for (int i = 1; i < s_sunset.Length; i++)
        {
            if (t <= s_sunset[i].At)
            {
                var (a0, r0, g0, b0) = s_sunset[i - 1];
                var (a1, r1, g1, b1) = s_sunset[i];
                double f = (t - a0) / (a1 - a0);
                return (r0 + ((r1 - r0) * f), g0 + ((g1 - g0) * f), b0 + ((b1 - b0) * f));
            }
        }

        var last = s_sunset[^1];
        return (last.R, last.G, last.B);
    }

    /// <summary>Signed distance to a rounded rectangle centred on the origin: negative inside.</summary>
    private static double RoundBox(double x, double y, double halfWidth, double halfHeight, double radius)
    {
        double qx = Math.Abs(x) - halfWidth + radius;
        double qy = Math.Abs(y) - halfHeight + radius;
        double outside = Math.Sqrt((Math.Max(qx, 0) * Math.Max(qx, 0)) + (Math.Max(qy, 0) * Math.Max(qy, 0)));
        return outside + Math.Min(Math.Max(qx, qy), 0) - radius;
    }

    private static (double R, double G, double B, double A) Over((double R, double G, double B, double A) under, (double R, double G, double B, double A) over)
    {
        double a = over.A + (under.A * (1 - over.A));
        if (a <= 0)
        {
            return (0, 0, 0, 0);
        }

        double Mix(double u, double o) => ((o * over.A) + (u * under.A * (1 - over.A))) / a;
        return (Mix(under.R, over.R), Mix(under.G, over.G), Mix(under.B, over.B), a);
    }

    private static byte ToByte(double value) => (byte)Math.Round(Math.Clamp(value, 0, 255));
}

static class Png
{
    private static readonly uint[] s_crcTable = MakeCrcTable();

    public static byte[] Encode(int width, int height, byte[] rgba)
    {
        using var raw = new MemoryStream();
        using (var zlib = new ZLibStream(raw, CompressionLevel.SmallestSize, leaveOpen: true))
        {
            for (int y = 0; y < height; y++)
            {
                zlib.WriteByte(0);
                zlib.Write(rgba, y * width * 4, width * 4);
            }
        }

        using var png = new MemoryStream();
        png.Write([0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A]);
        var header = new byte[13];
        WriteBigEndian(header, 0, (uint)width);
        WriteBigEndian(header, 4, (uint)height);
        header[8] = 8;
        header[9] = 6;
        WriteChunk(png, "IHDR", header);
        WriteChunk(png, "IDAT", raw.ToArray());
        WriteChunk(png, "IEND", []);
        return png.ToArray();
    }

    private static void WriteChunk(Stream stream, string type, byte[] data)
    {
        var length = new byte[4];
        WriteBigEndian(length, 0, (uint)data.Length);
        stream.Write(length);
        byte[] typeAndData = [.. type.Select(static c => (byte)c), .. data];
        stream.Write(typeAndData);
        var crc = new byte[4];
        WriteBigEndian(crc, 0, Crc(typeAndData));
        stream.Write(crc);
    }

    private static void WriteBigEndian(byte[] buffer, int offset, uint value)
    {
        buffer[offset] = (byte)(value >> 24);
        buffer[offset + 1] = (byte)(value >> 16);
        buffer[offset + 2] = (byte)(value >> 8);
        buffer[offset + 3] = (byte)value;
    }

    private static uint Crc(byte[] data)
    {
        uint crc = 0xFFFFFFFF;
        foreach (byte b in data)
        {
            crc = s_crcTable[(crc ^ b) & 0xFF] ^ (crc >> 8);
        }

        return crc ^ 0xFFFFFFFF;
    }

    private static uint[] MakeCrcTable()
    {
        var table = new uint[256];
        for (uint n = 0; n < 256; n++)
        {
            uint c = n;
            for (int k = 0; k < 8; k++)
            {
                c = (c & 1) != 0 ? 0xEDB88320 ^ (c >> 1) : c >> 1;
            }

            table[n] = c;
        }

        return table;
    }
}

static class Ico
{
    /// <summary>An ICO file with PNG images, which Windows Vista and later read at every size.</summary>
    public static byte[] Encode(IReadOnlyList<(int Size, byte[] Png)> images)
    {
        using var ico = new MemoryStream();
        using var writer = new BinaryWriter(ico);
        writer.Write((ushort)0);
        writer.Write((ushort)1);
        writer.Write((ushort)images.Count);
        int offset = 6 + (16 * images.Count);
        foreach (var (size, png) in images)
        {
            writer.Write((byte)(size >= 256 ? 0 : size));
            writer.Write((byte)(size >= 256 ? 0 : size));
            writer.Write((byte)0);
            writer.Write((byte)0);
            writer.Write((ushort)1);
            writer.Write((ushort)32);
            writer.Write((uint)png.Length);
            writer.Write((uint)offset);
            offset += png.Length;
        }

        foreach (var (_, png) in images)
        {
            writer.Write(png);
        }

        writer.Flush();
        return ico.ToArray();
    }
}
