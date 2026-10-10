using RoomEq.Core.Dsp;

namespace RoomEq.Core.Eq;

/// <summary>
/// Une bande de l'égaliseur paramétrique : fréquence centrale, gain, facteur de qualité Q.
/// Sérialisable en JSON (System.Text.Json).
/// </summary>
public sealed class EqBand
{
    public FilterType Type { get; set; } = FilterType.Peaking;
    public double Frequency { get; set; } = 1000.0;
    public double GainDb { get; set; }
    public double Q { get; set; } = 1.0;
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
