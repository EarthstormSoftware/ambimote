using AmbiMote.Core.Capture;
using AmbiMote.Core.Sampling;

namespace AmbiMote.Core.Engine;

/// <summary>One stick in a <see cref="LightingPreview"/>.</summary>
/// <param name="Monitor">The monitor it sits behind, or null when there is no stick mapping.</param>
/// <param name="From">Where pixel 0 sits along the edge, from 0 to 1.</param>
/// <param name="To">Where the last pixel sits.</param>
/// <param name="Colours">The colours the sticks are set to, before brightness and the power limit.</param>
public sealed record PreviewStick(string HostId, int Channel, DisplayOutput? Monitor, ScreenEdge Edge, double From, double To, Rgb[] Colours);

/// <summary>What every stick is showing and where it is mounted, to draw in the window.</summary>
/// <param name="Monitors">The monitors the sticks sit behind; empty when there is no stick mapping.</param>
public sealed record LightingPreview(IReadOnlyList<DisplayOutput> Monitors, IReadOnlyList<PreviewStick> Sticks)
{
    /// <summary>The same sticks, all off.</summary>
    public LightingPreview Dark() =>
        this with { Sticks = [.. Sticks.Select(static s => s with { Colours = new Rgb[s.Colours.Length] })] };
}
