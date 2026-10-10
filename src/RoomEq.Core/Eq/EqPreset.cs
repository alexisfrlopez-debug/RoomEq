using RoomEq.Core.Dsp;

namespace RoomEq.Core.Eq;

/// <summary>
/// Un preset d'égalisation complet : nom, préamplification, liste de bandes.
/// Sérialisable en JSON.
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
        foreach (var b in Bands) p.Bands.Add(b.Clone());
        return p;
    }

    /// <summary>Crée un preset paramétrique plat avec N bandes peaking réparties géométriquement.</summary>
    public static EqPreset Flat(int bandCount = 10, double fMin = 31.25, double fMax = 16000.0)
    {
        var p = new EqPreset("Plat");
        double ratio = Math.Pow(fMax / fMin, 1.0 / (bandCount - 1));
        for (int i = 0; i < bandCount; i++)
        {
            double f = fMin * Math.Pow(ratio, i);
            p.Bands.Add(new EqBand(FilterType.Peaking, f, 0.0, 1.1));
        }
        return p;
    }
}
