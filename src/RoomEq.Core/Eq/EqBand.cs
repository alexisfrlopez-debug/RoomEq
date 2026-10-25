using RoomEq.Core.Dsp;

namespace RoomEq.Core.Eq;

/// <summary>
/// Une bande de l'égaliseur paramétrique (spec I-EQ-01) : type, fréquence
/// centrale, gain, facteur de qualité, activation. Sérialisable JSON
/// (System.Text.Json). La bande est un modèle de réglage, pas un filtre : le
/// clamp des valeurs est fait par <see cref="BiquadFilter.Configure"/>.
/// </summary>
public sealed class EqBand
{
    public FilterType Type { get; set; } = FilterType.Peaking;
    public double Frequency { get; set; } = 1000.0;
    public double GainDb { get; set; }
    public double Q { get; set; } = 1.1;
    public bool Enabled { get; set; } = true;

    public EqBand() { }

    public EqBand(FilterType type, double freq, double gainDb, double q, bool enabled = true)
    {
        Type = type;
        Frequency = freq;
        GainDb = gainDb;
        Q = q;
        Enabled = enabled;
    }

    public EqBand Clone() => new(Type, Frequency, GainDb, Q, Enabled);

    public override string ToString() => $"{Frequency:0.#} Hz  {GainDb:+0.0;-0.0} dB  Q {Q:0.00}";
}
