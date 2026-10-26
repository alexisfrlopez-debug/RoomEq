using RoomEq.Audio.Playback;
using RoomEq.Audio.Routing;
using Xunit;

namespace RoomEq.Audio.Tests;

/// <summary>
/// Catalogue des signaux de test intégrés + politique de volume Windows
/// (specs I-AUD-02 / I-AUD-03) — portage des tests legacy v2.2.2.
/// </summary>
public class SignalCatalogTests
{
    // ------------------------------------------------------------------ catalogue intégré

    [Fact]
    public void BuiltIn_NotEmpty_WithUniqueIds()
    {
        Assert.NotEmpty(TestSignalCatalog.BuiltIn);
        Assert.Equal(
            TestSignalCatalog.BuiltIn.Select(i => i.Id).Count(),
            TestSignalCatalog.BuiltIn.Select(i => i.Id).Distinct().Count());
    }

    [Fact]
    public void BuiltIn_DisplayNamesAreUnique()
    {
        Assert.Equal(
            TestSignalCatalog.BuiltIn.Select(i => i.DisplayName).Count(),
            TestSignalCatalog.BuiltIn.Select(i => i.DisplayName).Distinct().Count());
    }

    [Fact]
    public void BuiltIn_DefaultItemExists()
    {
        Assert.Contains(TestSignalCatalog.BuiltIn, i => i.Id == TestSignalCatalog.DefaultItemId);
        Assert.Contains(TestSignalCatalog.BuiltIn, i => i.Id == TestSignalCatalog.DefaultItemId && i.Signal == TestSignal.PinkNoise);
    }

    [Fact]
    public void BuiltIn_CoversBruitsSinusEtBalayages()
    {
        Assert.Contains(TestSignalCatalog.BuiltIn, i => i.Signal == TestSignal.WhiteNoise);
        Assert.Contains(TestSignalCatalog.BuiltIn, i => i.Signal == TestSignal.PinkNoise);
        Assert.Contains(TestSignalCatalog.BuiltIn, i => i.Signal == TestSignal.BrownNoise);
        Assert.Contains(TestSignalCatalog.BuiltIn, i => i.Signal == TestSignal.VioletNoise);
        Assert.Contains(TestSignalCatalog.BuiltIn, i => i.Signal == TestSignal.Sine && i.SineFrequency == 1000.0);
        Assert.Contains(TestSignalCatalog.BuiltIn, i => i.Signal == TestSignal.LogSweep);
        // au moins 3 familles distinctes
        Assert.True(TestSignalCatalog.BuiltIn.Select(i => i.Category).Distinct().Count() >= 3);
    }

    [Fact]
    public void BuiltIn_AmplitudesAreSafe()
    {
        // amplitude > 0 et <= 1 : jamais de saturation par défaut du générateur
        Assert.All(TestSignalCatalog.BuiltIn, i =>
        {
            Assert.True(i.Amplitude > 0 && i.Amplitude <= 1.0, $"Amplitude incorrecte pour {i.Id}");
        });
    }

    [Fact]
    public void BuiltIn_SineFrequenciesAreAudible()
    {
        Assert.All(TestSignalCatalog.BuiltIn.Where(i => i.Signal == TestSignal.Sine), i =>
        {
            Assert.True(i.SineFrequency is >= 5 and <= 20000, $"Fréquence hors audible pour {i.Id}");
        });
    }

    [Fact]
    public void BuiltIn_SweepSpecsAreDescending()
    {
        Assert.All(TestSignalCatalog.BuiltIn.Where(i => i.SweepSpec != null), i =>
        {
            var spec = i.SweepSpec!;
            Assert.True(spec.F1 < spec.F2, $"Balayage inversé pour {i.Id}");
            Assert.True(spec.DurationSeconds > 0);
        });
    }

    // ------------------------------------------------------- curseur volume → endpoint

    [Theory]
    [InlineData(0.0, 0.0)]
    [InlineData(0.35, 0.35)]
    [InlineData(1.0, 1.0)]
    [InlineData(1.5, 1.0)]   // au-delà de 100 % : la carte reste au maximum
    [InlineData(2.0, 1.0)]
    [InlineData(-0.2, 0.0)]  // sous 0 : silence
    public void SliderToEndpoint_ClampsToWindowsRange(double slider, double expected)
    {
        Assert.Equal(expected, EndpointVolumeController.SliderToEndpoint(slider), 6);
    }

    // ------------------------------------------------------- synchro des deux curseurs

    [Theory]
    [InlineData(0.0, true)]
    [InlineData(0.5, true)]
    [InlineData(1.0, true)]
    [InlineData(-1.0, false)]   // sentinelle « carte indisponible »
    [InlineData(-0.001, false)]
    [InlineData(double.NaN, false)]
    [InlineData(1.0001, false)] // hors plage Windows : pas affichable comme volume carte
    public void IsUsable_RejectsSentinelAndOutOfRange(double value, bool expected)
    {
        Assert.Equal(expected, VolumeSyncPolicy.IsUsable(value));
    }

    [Fact]
    public void HasChangedSinceLastSeen_IgnoresStartupSentinel()
    {
        // au démarrage aucune valeur valide n'a été vue : le premier sondage
        // ne doit PAS écraser les affichages
        Assert.False(VolumeSyncPolicy.HasChangedSinceLastSeen(-1, 0.42));
        // un vrai changement externe est détecté
        Assert.True(VolumeSyncPolicy.HasChangedSinceLastSeen(0.50, 0.55));
        // le bruit d'arrondi du pilote (float) n'est pas un changement
        Assert.False(VolumeSyncPolicy.HasChangedSinceLastSeen(0.50000, 0.50002));
    }

    [Fact]
    public void ShouldWriteToEndpoint_SkipsRoundOffAndRejectsBadTargets()
    {
        // écart infime : pas d'écriture (anti-spam pendant un glissement)
        Assert.False(VolumeSyncPolicy.ShouldWriteToEndpoint(0.5000, 0.5010));
        // vrai écart : écriture
        Assert.True(VolumeSyncPolicy.ShouldWriteToEndpoint(0.50, 0.60));
        // carte illisible (sentinelle −1) : on tente quand même l'écriture
        Assert.True(VolumeSyncPolicy.ShouldWriteToEndpoint(-1, 0.60));
        // consignes invalides : jamais écrites
        Assert.False(VolumeSyncPolicy.ShouldWriteToEndpoint(0.5, -1));
        Assert.False(VolumeSyncPolicy.ShouldWriteToEndpoint(0.5, double.NaN));
    }

    [Fact]
    public void ShouldSyncTopBar_AvoidsNotificationLoops()
    {
        // la barre du haut est déjà à la position de la carte : rien à faire
        Assert.False(VolumeSyncPolicy.ShouldSyncTopBar(0.42, 0.4200001));
        // la carte a bougé (curseur du Mixage, clavier, Windows) : recaler
        Assert.True(VolumeSyncPolicy.ShouldSyncTopBar(1.0, 0.42));
        // carte illisible : rien à recaler
        Assert.False(VolumeSyncPolicy.ShouldSyncTopBar(1.0, -1));
    }

    [Theory]
    [InlineData(-1.0, 0.0)]      // sentinelle → silence (jamais propagée à la carte)
    [InlineData(double.NaN, 0.0)]
    [InlineData(0.0, 0.0)]
    [InlineData(0.37, 0.37)]
    [InlineData(1.0, 1.0)]
    [InlineData(1.5, 1.0)]       // boost logiciel : la carte reste à 100 %
    [InlineData(-0.4, 0.0)]
    public void ClampToEndpointRange_BringsAnySliderValueIntoWindowsRange(double value, double expected)
    {
        Assert.Equal(expected, VolumeSyncPolicy.ClampToEndpointRange(value), 6);
    }

    [Fact]
    public void VolumeSyncEpsilons_AreTheLegacyValues()
    {
        // verrouillé par spec I-AUD-03 : sépare « vrai changement » et « écrit utile »
        Assert.Equal(0.0005, VolumeSyncPolicy.ChangeEpsilon, 12);
        Assert.Equal(0.0035, VolumeSyncPolicy.WriteEpsilon, 12);
    }
}
