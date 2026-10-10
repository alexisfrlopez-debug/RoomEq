using RoomEq.Core.Calibration;
using RoomEq.Core.Dsp;
using RoomEq.Core.Eq;
using RoomEq.Core.Io;
using Xunit;

namespace RoomEq.Core.Tests;

/// <summary>Tests de la bibliothèque intégrée de micros du marché.</summary>
public class MicProfileLibraryTests
{
    [Fact]
    public void Library_ContainsManyValidProfiles()
    {
        var library = MicProfileLibrary.LoadAll();

        Assert.True(library.Count >= 30, $"bibliothèque trop petite : {library.Count} profils");

        // noms uniques
        var names = library.Select(p => p.Name).ToList();
        Assert.Equal(names.Count, names.Distinct(StringComparer.OrdinalIgnoreCase).Count());

        foreach (var p in library)
        {
            Assert.True(p.IsValid, $"{p.Name} invalide");
            Assert.True(p.PointCount >= 8, $"{p.Name} : seulement {p.PointCount} points");

            // fréquences croissantes strictes
            for (int i = 1; i < p.Frequencies.Count; i++)
                Assert.True(p.Frequencies[i] > p.Frequencies[i - 1],
                    $"{p.Name} : fréquences non croissantes à {p.Frequencies[i]:0.#} Hz");

            // gains plausibles
            Assert.All(p.GainsDb, g => Assert.InRange(g, -20, 20));

            // normalisation : gain ≈ 0 dB à 1 kHz
            Assert.True(Math.Abs(p.GainAt(1000)) < 0.6, $"{p.Name} non normalisé à 1 kHz");

            // métadonnées présentes
            Assert.False(string.IsNullOrWhiteSpace(p.Brand), $"{p.Name} sans marque");
            Assert.False(string.IsNullOrWhiteSpace(p.Technology), $"{p.Name} sans technologie");
        }
    }

    [Fact]
    public void Library_CoversAllTechnologies_ActiveAndPassive()
    {
        var library = MicProfileLibrary.LoadAll();

        var technologies = library.Select(p => p.Technology).Distinct().ToList();
        Assert.True(technologies.Count >= 10, $"peu de technologies : {technologies.Count}");

        int active = library.Count(p => p.Technology.Contains("actif", StringComparison.OrdinalIgnoreCase)
                                        || !IsPassiveTechnology(p.Technology));
        int passive = library.Count(p => IsPassiveTechnology(p.Technology));
        Assert.True(passive >= 5, $"trop peu de micros passifs (dynamique/ruban) : {passive}");
        Assert.True(active >= 20, $"trop peu de micros actifs : {active}");

        static bool IsPassiveTechnology(string tech) =>
            tech.Contains("Dynamique", StringComparison.OrdinalIgnoreCase) ||
            tech.Contains("Ruban", StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Library_IncludesWellKnownMeasurementMics()
    {
        var library = MicProfileLibrary.LoadAll();
        var names = library.Select(p => p.Name).ToList();

        foreach (var expected in new[] { "miniDSP UMIK-1", "Behringer ECM8000", "Dayton Audio EMM-6",
                                         "Earthworks M23", "Shure SM57", "Shure SM58", "Royer R-121",
                                         "Neumann TLM 103", "Audix TM1", "Sonarworks XREF 20" })
        {
            Assert.Contains(expected, names);
        }
    }

    [Fact]
    public void Search_MatchesNameBrandTechnologyAndNotes()
    {
        var library = MicProfileLibrary.LoadAll();

        Assert.Contains(MicProfileLibrary.Search(library, "UMIK"), p => p.Name == "miniDSP UMIK-1");
        Assert.All(MicProfileLibrary.Search(library, "Shure"), p => Assert.Equal("Shure", p.Brand));
        Assert.Contains(MicProfileLibrary.Search(library, "ruban"),
            p => p.Technology.Contains("Ruban", StringComparison.OrdinalIgnoreCase));
        Assert.Contains(MicProfileLibrary.Search(library, "mesure"),
            p => p.UsageContains());
        // recherche vide = tout
        Assert.Equal(library.Count, MicProfileLibrary.Search(library, "").Count);
    }

    [Fact]
    public void ToEditableCopy_IsIndependent()
    {
        var library = MicProfileLibrary.LoadAll();
        var source = library[0];
        var copy = MicProfileLibrary.ToEditableCopy(source, "Mon micro");

        Assert.Equal("Mon micro", copy.Name);
        copy.GainsDb[0] = 99;
        Assert.NotEqual(99, source.GainsDb[0]);
        Assert.Equal(source.Frequencies.Count, copy.Frequencies.Count);
    }

    [Fact]
    public void CorrectionDesigner_SubtractsMicResponseBeforeDesign()
    {
        // preuve de bout en bout : une bosse micro de +3 dB au-dessus de 2 kHz
        // apparaît comme une bosse de pièce si aucun profil n'est fourni
        // (l'EQ la « corrige » à tort), et disparaît avec le bon profil.
        var freqs = new List<double>();
        for (double f = 25; f <= 20000; f *= Math.Pow(2, 1.0 / 6.0)) freqs.Add(f);

        var roomPeak = new BiquadFilter(FilterType.Peaking, 48000, 80, 5.0, 1.2);
        var micRise = new BiquadFilter(FilterType.Peaking, 48000, 5000, 3.0, 0.8);

        var measured = freqs.Select(f => roomPeak.ResponseDb(f, 48000) + micRise.ResponseDb(f, 48000)).ToArray();

        var mic = new MicProfile
        {
            Name = "micro à bosse HF",
            Frequencies = new List<double> { 20, 1000, 2000, 3000, 5000, 8000, 12000, 20000 },
            GainsDb = new List<double> { 0, 0, 0.3, 1.2, 2.6, 3.0, 2.6, 1.5 }
        };

        var opt = new CorrectionOptions { FreqMin = 40, FreqMax = 16000, ThresholdDb = 0.5 };
        var withoutMic = CorrectionDesigner.Design(freqs.ToArray(), measured, opt, 48000, "sans", null);
        var withMic = CorrectionDesigner.Design(freqs.ToArray(), measured, opt, 48000, "avec", mic);

        double correctionAt8k(List<EqBand> bands) =>
            SpectrumTools.InterpLog(bands.Select(b => b.Frequency).ToList(),
                                    bands.Select(b => b.GainDb).ToList(), 8000.0);

        double cutWithout = correctionAt8k(withoutMic.Preset.Bands);
        double cutWith = correctionAt8k(withMic.Preset.Bands);

        // sans profil : l'EQ coupe la fausse bosse micro (≈ -1 dB à 8 kHz)
        Assert.True(cutWithout < -0.4, $"la bosse micro n'est pas coupée sans profil : {cutWithout:0.00} dB");
        // avec profil : la bosse micro est soustraite avant, l'EQ ne la corrige plus
        Assert.True(cutWith > cutWithout + 1.0,
            $"la correction micro n'est pas prise en compte : sans {cutWithout:0.00} / avec {cutWith:0.00} dB");
    }
}

/// <summary>Extension locale pour le test de recherche.</summary>
internal static class MicProfileTestExtensions
{
    public static bool UsageContains(this MicProfile p)
        => p.Notes.Contains("mesure", StringComparison.OrdinalIgnoreCase) ||
           p.Technology.Contains("mesure", StringComparison.OrdinalIgnoreCase) ||
           (p is { }) && (p.Technology + p.Notes + p.Name).Contains("mesure", StringComparison.OrdinalIgnoreCase);
}
