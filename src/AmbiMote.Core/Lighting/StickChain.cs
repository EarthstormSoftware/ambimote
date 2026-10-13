using AmbiMote.Core.Mote;

namespace AmbiMote.Core.Lighting;

/// <summary>All the sticks lit by an effect, joined into one run of LEDs.</summary>
public sealed class StickChain
{
    private readonly (MoteHost Host, int Channel, int Offset, int Length)[] _sticks;
    private readonly MoteHost[] _hosts;
    private readonly double[] _positions;
    private readonly Rgb[] _pixels;

    /// <param name="sticks">The sticks, in the order their pixels appear in <paramref name="positions"/>.</param>
    /// <param name="positions">
    /// Where each pixel sits around the setup, from <see cref="SetupLayout.Positions"/>. Default: evenly through the
    /// sticks in the order given, pixel 0 to the end of each stick, for sticks that are not mapped to monitors.
    /// </param>
    public StickChain(IEnumerable<(MoteHost Host, int Channel)> sticks, IReadOnlyList<double>? positions = null)
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
        if (positions is not null)
        {
            if (positions.Count != offset)
            {
                throw new ArgumentException($"There are {offset} pixels but {positions.Count} positions.", nameof(positions));
            }

            _positions = [.. positions];
        }
        else
        {
            _positions = new double[offset];
            for (int i = 0; i < offset; i++)
            {
                _positions[i] = (double)i / offset;
            }
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
