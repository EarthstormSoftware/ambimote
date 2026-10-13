namespace AmbiMote.Core.Lighting;

/// <summary>A lighting effect: works out the colour of every LED at a moment in time.</summary>
public interface IEffect
{
    /// <summary>
    /// Fills <paramref name="pixels"/> for the time <paramref name="seconds"/> since the effect started.
    /// <paramref name="positions"/> says where each pixel sits around the whole setup, from 0 to 1;
    /// it has the same length as <paramref name="pixels"/>.
    /// </summary>
    void Render(double seconds, ReadOnlySpan<double> positions, Span<Rgb> pixels);
}
