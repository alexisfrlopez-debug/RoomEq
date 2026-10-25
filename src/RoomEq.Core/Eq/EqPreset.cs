using RoomEq.Core.Dsp;

namespace RoomEq.Core.Eq;

/// <summary>
/// Un preset d'égalisation complet (spec I-EQ-03) : nom, description,
/// préamplification, liste de bandes. Sérialisable JSON.
/// </summary>
public sealed class EqPreset
{
    public string Name { get; set; } = "Sans nom";
    public string? Description { get; set; }
    public double PreampDb { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.Now;
    public List<EqBand> Bands { get; set; } = new();

    public EqPreset() { }

    public EqPreset(string name) => Name = name;

    public EqPreset Clone()
    {
        var p = new EqPreset(Name)
        {
            Description = Description,
            PreampDb = PreampDb,
            CreatedAt = CreatedAt
        };
        foreach (var b in Bands)
            p.Bands.Add(b.Clone());
        return p;
    }

    /// <summary>Crée un preset paramétrique plat : N bandes peaking à 0 dB
    /// réparties géométriquement entre fMin et fMax.</summary>
    public static EqPreset Flat(int bandCount = 10, double fMin = 31.25, double fMax = 16000.0)
    {
        if (bandCount < 1)
            throw new ArgumentOutOfRangeException(nameof(bandCount), "Au moins une bande est requise.");
        var p = new EqPreset("Plat");
        if (bandCount == 1)
        {
            p.Bands.Add(new EqBand(FilterType.Peaking, Math.Sqrt(fMin * fMax), 0.0, 1.1));
            return p;
        }
        double ratio = Math.Pow(fMax / fMin, 1.0 / (bandCount - 1));
        for (int i = 0; i < bandCount; i++)
        {
            double f = fMin * Math.Pow(ratio, i);
            p.Bands.Add(new EqBand(FilterType.Peaking, f, 0.0, 1.1));
        }
        return p;
    }
}
