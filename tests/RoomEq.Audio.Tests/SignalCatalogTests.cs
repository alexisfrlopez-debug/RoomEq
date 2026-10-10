using System.Linq;
using RoomEq.Audio.Routing;
using RoomEq.Audio.Playback;
using Xunit;

namespace RoomEq.Audio.Tests;

/// <summary>
/// Tests du catalogue des signaux de test intégrés (menu déroulant) et de la
/// conversion curseur → volume Windows de la carte physique.
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
}
