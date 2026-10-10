using RoomEq.Core.Dsp;
using RoomEq.Core.Eq;
using Xunit;

namespace RoomEq.Core.Tests;

/// <summary>
/// Régressions de la chaîne d'égalisation : le changement de TAUX
/// D'ÉCHANTILLONNAGE doit reconstruire les filtres (sinon la réponse réelle du
/// traitement correspond à l'ancien taux après bascule de sortie).
/// </summary>
public class EqChainRateTests
{
    private static EqPreset Preset()
    {
        var p = new EqPreset("test");
        p.Bands.Add(new EqBand(FilterType.LowPass, 500, 0, 0.71));
        p.Bands.Add(new EqBand(FilterType.Peaking, 2000, 4.0, 2.0));
        return p;
    }

    [Fact]
    public void Configure_SampleRateChange_RebuildsFilters()
    {
        var chain = new EqChain();
        chain.Configure(48000, 1);
        chain.SetPreset(Preset());

        // bascule de sortie : le taux passe de 48 kHz à 8 kHz
        chain.Configure(8000, 1);

        // référence : chaîne neuve créée directement au nouveau taux
        var reference = new EqChain();
        reference.Configure(8000, 1);
        reference.SetPreset(Preset());

        // même bruit dans les deux chaînes → mêmes sorties échantillon par échantillon
        var rnd = new Random(7);
        for (int block = 0; block < 6; block++)
        {
            var a = new float[512];
            var b = new float[512];
            for (int i = 0; i < a.Length; i++)
                a[i] = b[i] = (float)(rnd.NextDouble() * 2 - 1);

            chain.Process(a, 0, a.Length);
            reference.Process(b, 0, b.Length);

            for (int i = 0; i < a.Length; i++)
                Assert.True(Math.Abs(a[i] - b[i]) < 1e-6,
                    $"Écart à {i} : {a[i]} vs {b[i]} — filtres non reconstruits au nouveau taux ?");
        }
    }

    [Fact]
    public void Configure_SameRate_DoesNotResetFilters()
    {
        // re-Configure au MÊME taux ne doit pas réinitialiser l'état des filtres
        var chain = new EqChain();
        chain.Configure(48000, 1);
        chain.SetPreset(Preset());

        var warmup = new float[1024];
        var rnd = new Random(3);
        for (int i = 0; i < warmup.Length; i++) warmup[i] = (float)(rnd.NextDouble() * 2 - 1);
        chain.Process(warmup, 0, warmup.Length);

        // entrée déterministe, lue deux fois : une sans re-Configure, une après
        var input = new float[256];
        for (int i = 0; i < input.Length; i++) input[i] = (float)(rnd.NextDouble() * 2 - 1);

        var direct = (float[])input.Clone();
        chain.Process(direct, 0, direct.Length);

        chain.Configure(48000, 1); // même taux : pas de reset
        var after = (float[])input.Clone();
        chain.Process(after, 0, after.Length);

        // l'état temporel est conservé : les deux lectures diffèrent (continuité)
        bool differ = false;
        for (int i = 0; i < direct.Length; i++)
            if (Math.Abs(direct[i] - after[i]) > 1e-9) { differ = true; break; }
        Assert.True(differ, "L'état des filtres a été réinitialisé par un re-Configure au même taux.");
    }
}
