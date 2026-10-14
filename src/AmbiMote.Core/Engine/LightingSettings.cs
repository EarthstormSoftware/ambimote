using System.Text.Json.Serialization;

using AmbiMote.Core.Lighting;
using AmbiMote.Core.Mote;
using AmbiMote.Core.Processing;

namespace AmbiMote.Core.Engine;

/// <summary>What the sticks show.</summary>
public enum LightingMode
{
    /// <summary>Sticks off, and the Mote hosts closed so other programs can use them.</summary>
    Off,

    /// <summary>White light at a colour temperature, like a lamp.</summary>
    Light,

    /// <summary>One colour.</summary>
    Colour,

    /// <summary>A moving effect.</summary>
    Effect,

    /// <summary>The colours at the edges of the screens.</summary>
    Ambient,
}

/// <summary>The lighting effects.</summary>
public enum EffectKind
{
    /// <summary>A rainbow flowing around the monitors.</summary>
    Rainbow,

    /// <summary>The chosen colour slowly brightening and dimming.</summary>
    Breathe,

    /// <summary>Each LED flickering gently, like a candle.</summary>
    Candle,

    /// <summary>A dot of the chosen colour running around the monitors, to check the mapping.</summary>
    Chase,
}

/// <summary>Everything the <see cref="LightingEngine"/> needs to know about what to show.</summary>
public sealed record LightingSettings
{
    public const int MinKelvin = 1900;
    public const int MaxKelvin = 6500;
    public const double MinEffectSpeed = 0.25;
    public const double MaxEffectSpeed = 4;
    public const double MaxSmoothing = 2;

    public LightingMode Mode { get; init; } = LightingMode.Ambient;

    /// <summary>
    /// Overall brightness from 0 to 1, for every mode, as it looks: 0.5 looks about half as bright as 1. 1 is the
    /// brightest the power limit allows. See <see cref="LedBrightness"/>.
    /// </summary>
    public double Brightness { get; init; } = 0.4;

    /// <summary>
    /// <see cref="Brightness"/> as the LEDs need it. Eyes see light on a curve, so half the light looks far more than
    /// half as bright; squaring makes each step of the slider look about the same size.
    /// </summary>
    [JsonIgnore]
    public double LedBrightness => Brightness * Brightness;

    /// <summary>Colour temperature for <see cref="LightingMode.Light"/>, from <see cref="MinKelvin"/> to <see cref="MaxKelvin"/>.</summary>
    public int Kelvin { get; init; } = 2700;

    /// <summary>The colour for <see cref="LightingMode.Colour"/>, and for the breathe and chase effects.</summary>
    public Rgb Colour { get; init; } = new(255, 96, 0);

    public EffectKind Effect { get; init; } = EffectKind.Rainbow;

    /// <summary>How fast effects run, as a multiple of their normal speed.</summary>
    public double EffectSpeed { get; init; } = 1;

    /// <summary>Seconds for ambient colours to get most of the way to a new screen colour.</summary>
    public double Smoothing { get; init; } = 0.15;

    public double Gamma { get; init; } = ColourProcessor.DefaultGamma;

    public double Saturation { get; init; } = ColourProcessor.DefaultSaturation;

    /// <summary>The most current the sticks on one host may draw, in mA. See <see cref="PowerLimit"/>.</summary>
    public double MaxMilliamps { get; init; } = MoteHost.DefaultMaxMilliamps;

    /// <summary>The colour <see cref="Mode"/> shows when it is a steady light, or null when it moves.</summary>
    [JsonIgnore]
    public Rgb? SteadyColour => Mode switch
    {
        LightingMode.Light => Rgb.FromKelvin(Kelvin),
        LightingMode.Colour => Colour,
        _ => null,
    };

    /// <summary>A copy with every value in range, so a hand-edited or old settings file cannot break anything.</summary>
    public LightingSettings Normalised()
    {
        var defaults = new LightingSettings();
        return this with
        {
            Mode = Enum.IsDefined(Mode) ? Mode : defaults.Mode,
            Brightness = Clamp(Brightness, 0, 1, defaults.Brightness),
            Kelvin = Math.Clamp(Kelvin, MinKelvin, MaxKelvin),
            Effect = Enum.IsDefined(Effect) ? Effect : defaults.Effect,
            EffectSpeed = Clamp(EffectSpeed, MinEffectSpeed, MaxEffectSpeed, defaults.EffectSpeed),
            Smoothing = Clamp(Smoothing, 0, MaxSmoothing, defaults.Smoothing),
            Gamma = Clamp(Gamma, 0.5, 4, defaults.Gamma),
            Saturation = Clamp(Saturation, 0, 3, defaults.Saturation),
            MaxMilliamps = Clamp(MaxMilliamps, 50, 2000, defaults.MaxMilliamps),
        };
    }

    /// <summary>
    /// The effect at its normal speed. Speed is applied by running the effect's clock faster or slower,
    /// so changing it does not make the effect jump.
    /// </summary>
    public IEffect CreateEffect() => Effect switch
    {
        EffectKind.Rainbow => new RainbowEffect(10),
        EffectKind.Breathe => new BreatheEffect(Colour, 6),
        EffectKind.Candle => new CandleEffect(Rgb.FromKelvin(1900)),
        _ => new ChaseEffect(Colour, 8),
    };

    private static double Clamp(double value, double min, double max, double fallback) =>
        double.IsFinite(value) ? Math.Clamp(value, min, max) : fallback;
}
