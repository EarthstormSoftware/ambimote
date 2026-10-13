using AmbiMote.Core.Mote;

namespace AmbiMote.Core.Lighting;

/// <summary>
/// All the sticks lit by an effect, joined into one run of LEDs. Until sticks are mapped to monitors,
/// positions follow the order the sticks were given in, pixel 0 to the end of each stick.
/// </summary>
public sealed class StickChain
{
    private readonly (MoteHost Host, int Channel, int Offset, int Length)[] _sticks;
    private readonly MoteHost[] _hosts;
    private readonly double[] _positions;
    private readonly Rgb[] _pixels;

    public StickChain(IEnumerable<(MoteHost Host, int Channel)> sticks)
    {
        ArgumentNullException.ThrowIfNull(sticks);
        var list = new List<(MoteHost, int, int, int)>();
        int offset = 0;
        foreach (var (host, channel) in sticks)
        {
            int length = host.GetPixels(channel).Length;
            list.Add((host, channel, offset, length));
            offset += length;
        }

        _sticks = [.. list];
        _hosts = [.. _sticks.Select(static s => s.Host).Distinct()];
        _pixels = new Rgb[offset];
        _positions = new double[offset];
        for (int i = 0; i < offset; i++)
        {
            _positions[i] = (double)i / offset;
        }
    }

    public int PixelCount => _pixels.Length;

    public ReadOnlySpan<double> Positions => _positions;

    /// <summary>Renders <paramref name="effect"/> at <paramref name="seconds"/> and sends it to every host.</summary>
    public void Show(IEffect effect, double seconds)
    {
        ArgumentNullException.ThrowIfNull(effect);
        effect.Render(seconds, _positions, _pixels);
        foreach (var (host, channel, offset, length) in _sticks)
        {
            _pixels.AsSpan(offset, length).CopyTo(host.GetPixels(channel));
        }

        foreach (var host in _hosts)
        {
            host.Show();
        }
    }
}
