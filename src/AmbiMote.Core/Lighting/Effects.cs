namespace AmbiMote.Core.Lighting;

/// <summary>A rainbow that flows around the setup.</summary>
/// <param name="periodSeconds">Seconds for the colours to go all the way round once.</param>
/// <param name="spread">How many rainbows fit around the setup.</param>
public sealed class RainbowEffect(double periodSeconds = 10, double spread = 1) : IEffect
{
    public void Render(double seconds, ReadOnlySpan<double> positions, Span<Rgb> pixels)
    {
        double shift = seconds / periodSeconds;
        for (int i = 0; i < pixels.Length; i++)
        {
            pixels[i] = Rgb.FromHsv((positions[i] * spread) - shift);
        }
    }
}

/// <summary>One colour that slowly brightens and dims, like breathing.</summary>
/// <param name="periodSeconds">Seconds for one breath, from dim to bright and back.</param>
public sealed class BreatheEffect(Rgb colour, double periodSeconds = 6) : IEffect
{
    /// <summary>The dimmest point of a breath, as a fraction of full brightness.</summary>
    public const double Low = 0.1;

    public void Render(double seconds, ReadOnlySpan<double> positions, Span<Rgb> pixels)
    {
        double wave = 0.5 - (0.5 * Math.Cos(2 * Math.PI * seconds / periodSeconds));
        pixels.Fill(colour.Scale(Low + ((1 - Low) * wave)));
    }
}

/// <summary>A dot of light with a fading tail that runs around the setup, to check the order of the LEDs.</summary>
/// <param name="periodSeconds">Seconds for the dot to go all the way round once.</param>
/// <param name="tail">How many LEDs the fading tail covers behind the dot.</param>
public sealed class ChaseEffect(Rgb colour, double periodSeconds = 8, int tail = 4) : IEffect
{
    public void Render(double seconds, ReadOnlySpan<double> positions, Span<Rgb> pixels)
    {
        double head = seconds / periodSeconds;
        head -= Math.Floor(head);
        double step = 1.0 / Math.Max(1, pixels.Length);
        for (int i = 0; i < pixels.Length; i++)
        {
            // How far behind the dot this pixel is, going round.
            double behind = head - positions[i];
            behind -= Math.Floor(behind);
            double lit = 1 - (behind / (step * (Math.Max(0, tail) + 1)));
            pixels[i] = lit > 0 ? colour.Scale(lit) : Rgb.Black;
        }
    }
}

/// <summary>Each LED flickers gently on its own, like a candle or a fire.</summary>
public sealed class CandleEffect(Rgb colour, Random? random = null) : IEffect
{
    /// <summary>The dimmest a flicker gets, as a fraction of full brightness.</summary>
    public const double Low = 0.45;

    private readonly Random _random = random ?? new Random();
    private double[] _level = [];
    private double[] _target = [];
    private double _last;

    public void Render(double seconds, ReadOnlySpan<double> positions, Span<Rgb> pixels)
    {
        if (_level.Length != pixels.Length)
        {
            _level = new double[pixels.Length];
            _target = new double[pixels.Length];
            _level.AsSpan().Fill(1);
            _target.AsSpan().Fill(1);
            _last = seconds;
        }

        // Each LED drifts towards its own random target, then picks a new one; dips are rarer than small wobbles.
        double move = Math.Clamp((seconds - _last) * 6, 0, 1);
        _last = seconds;
        for (int i = 0; i < pixels.Length; i++)
        {
            _level[i] += (_target[i] - _level[i]) * move;
            if (Math.Abs(_target[i] - _level[i]) < 0.02)
            {
                double r = _random.NextDouble();
                _target[i] = 1 - ((1 - Low) * r * r * r);
            }

            // Dimmer flames are also redder.
            double level = _level[i];
            pixels[i] = new Rgb(
                (byte)Math.Round(colour.R * level),
                (byte)Math.Round(colour.G * level * level),
                (byte)Math.Round(colour.B * level * level * level));
        }
    }
}
