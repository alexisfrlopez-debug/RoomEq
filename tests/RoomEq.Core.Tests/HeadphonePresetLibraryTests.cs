using RoomEq.Core.Dsp;
using RoomEq.Core.Eq;
using RoomEq.Core.Headphones;
using Xunit;

namespace RoomEq.Core.Tests;

/// <summary>
/// Bibliothèque intégrée de corrections casques/écouteurs (données AutoEq, MIT) :
/// intégrité du catalogue embarqué, plages de valeurs, conversion en preset
/// d'égaliseur et recherche.
/// </summary>
public class HeadphonePresetLibraryTests
{
    [Fact]
    public void Catalogue_IsRich_AndCoversAllRanges()
    {
        var all = HeadphonePresetLibrary.LoadAll();

        // liste « assez fournie » exigée par le cahier des charges v2.0
        Assert.True(all.Count >= 200, $"catalogue trop petit : {all.Count} modèles");
        Assert.True(HeadphonePresetLibrary.Brands().Count >= 30,
            $"trop peu de marques : {HeadphonePresetLibrary.Brands().Count}");

        // toutes les familles et toutes les gammes représentées
        foreach (string type in new[] { "over-ear", "in-ear", "earbud", "gaming" })
            Assert.Contains(all, m => m.Type == type);
        foreach (string gamme in new[] { "budget", "moyenne", "premium" })
            Assert.Contains(all, m => m.Gamme == gamme);
    }

    [Fact]
    public void Models_HaveUniqueIds_AndValidMetadata()
    {
        var all = HeadphonePresetLibrary.LoadAll();
        var ids = all.Select(m => m.Id).ToList();
        Assert.Equal(ids.Count, ids.Distinct(StringComparer.OrdinalIgnoreCase).Count());

        foreach (var m in all)
        {
            Assert.False(string.IsNullOrWhiteSpace(m.Brand), $"marque vide : {m.Id}");
            Assert.False(string.IsNullOrWhiteSpace(m.Model), $"modèle vide : {m.Id}");
            Assert.False(string.IsNullOrWhiteSpace(m.Source), $"source absente : {m.Id}");
            Assert.InRange(m.PreampDb, -15.0, 0.0);
            Assert.InRange(m.Filters.Count, 1, 20);
        }
    }

    [Fact]
    public void Filters_AreWithinEngineRanges()
    {
        foreach (var m in HeadphonePresetLibrary.LoadAll())
        {
            foreach (var f in m.Filters)
            {
                Assert.True(f.Kind is "peaking" or "low-shelf" or "high-shelf",
                    $"{m.Id} : type de filtre inconnu « {f.Kind} »");
                Assert.InRange(f.Frequency, 10.0, 20000.0);
                // ±24 dB : plage de l'éditeur de bandes ; certaines corrections
                // AutoEq atteignent ±20 dB (casques très colorés, ex. Meze 99).
                Assert.InRange(f.GainDb, -24.0, 24.0);
                Assert.InRange(f.Q, 0.1, 12.0);
            }
        }
    }

    [Fact]
    public void ToEqPreset_MapsFiltersAndPreamp_AndFeedsEqChain()
    {
        var hd650 = HeadphonePresetLibrary.Find("sennheiserhd650");
        Assert.NotNull(hd650);
        Assert.Equal("oratory1990", hd650.Source);

        var preset = HeadphonePresetLibrary.ToEqPreset(hd650!);
        Assert.Equal(hd650!.DisplayName, preset.Name);
        Assert.Equal(hd650.PreampDb, preset.PreampDb, 2);
        Assert.Equal(hd650.Filters.Count, preset.Bands.Count);
        Assert.Contains(hd650.Source, preset.Description, StringComparison.Ordinal);

        // le preset doit passer tel quel dans la chaîne temps réel
        var chain = new EqChain();
        chain.Configure(48000, 2);
        chain.SetPreset(preset);

        double peak = 0;
        foreach (var p in chain.ResponseCurve(20, 20000, 200))
            peak = Math.Max(peak, Math.Abs(p.Db));
        Assert.True(peak > 0.5, "courbe de réponse étonnamment plate pour un preset casque");

        // traitement audio sans erreur ni modification de longueur
        var buffer = new float[480];
        for (int i = 0; i < buffer.Length; i++) buffer[i] = 0.1f;
        chain.Process(buffer, 0, buffer.Length);
        Assert.All(buffer, s => Assert.True(float.IsFinite(s)));
    }

    [Fact]
    public void KnownModels_ArePresent()
    {
        // stars de chaque univers : studio, audiophile, nomade, gaming, chi-fi
        string[] expected =
        {
            "sennheiserhd650", "akgk371", "audiotechnicaathm50x", "beyerdynamicdt770pro",
            "sonywh1000xm4", "appleairpodspro2", "hyperxcloudii", "7hzsalnoteszero",
            "moondroparia",
        };
        foreach (string id in expected)
        {
            Assert.True(HeadphonePresetLibrary.Find(id) != null, $"modèle absent : {id}");
        }
    }

    [Fact]
    public void Search_FindsModelsAcrossBrands()
    {
        var r = HeadphonePresetLibrary.Search("650");
        Assert.Contains(r, m => m.Id == "sennheiserhd650");

        var r2 = HeadphonePresetLibrary.Search("sony xm5");
        Assert.Contains(r2, m => m.Id == "sonywh1000xm5");

        var r3 = HeadphonePresetLibrary.Search("");
        Assert.Equal(HeadphonePresetLibrary.Count, r3.Count);

        var r4 = HeadphonePresetLibrary.Search("zzzzinnexistantzzzz");
        Assert.Empty(r4);
    }

    [Fact]
    public void ByBrand_MatchesBrandOnly()
    {
        var brands = HeadphonePresetLibrary.Brands();
        Assert.Contains("Sennheiser", brands);

        var models = HeadphonePresetLibrary.ByBrand("sennheiser");
        Assert.NotEmpty(models);
        Assert.All(models, m => Assert.Equal("Sennheiser", m.Brand, ignoreCase: true));
        Assert.Equal(models.Count,
            HeadphonePresetLibrary.LoadAll().Count(m =>
                string.Equals(m.Brand, "Sennheiser", StringComparison.OrdinalIgnoreCase)));
    }

    [Fact]
    public void FrenchLabels_AreProvided()
    {
        var m = HeadphonePresetLibrary.LoadAll().First(x => x.Type == "gaming");
        Assert.Equal("Casque gaming", m.TypeLabel);

        var b = HeadphonePresetLibrary.LoadAll().First(x => x.Gamme == "budget");
        Assert.Equal("Petit prix", b.GammeLabel);
    }
}
