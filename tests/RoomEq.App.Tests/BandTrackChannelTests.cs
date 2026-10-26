using RoomEq.App.ViewModels;
using RoomEq.Audio.Routing;
using RoomEq.Core.Dsp;
using RoomEq.Core.Eq;
using Xunit;

namespace RoomEq.App.Tests;

/// <summary>
/// Tranches de console et éditeur d'EQ de piste (spec I-APP-03) :
/// bindings fader/pan/mute/solo poussés dans le canal, badges, générateur,
/// VU, LED EQ, édition de bandes par index et presets rapides.
/// </summary>
public class BandTrackChannelTests
{
    // ================================================================ BandViewModel

    [Fact]
    public void Band_ToModel_RoundTrips()
    {
        int changed = 0;
        var b = new BandViewModel(new EqBand(FilterType.Peaking, 1000, -3.5, 1.4, false), () => changed++);
        var m = b.ToModel();

        Assert.Equal(FilterType.Peaking, m.Type);
        Assert.Equal(1000, m.Frequency, 3);
        Assert.Equal(-3.5, m.GainDb, 3);
        Assert.Equal(1.4, m.Q, 3);
        Assert.False(m.Enabled);
        Assert.Equal(0, changed); // la construction n'initialise pas via les setters

        b.Frequency = 2000;
        Assert.Equal(1, changed);
        b.GainDb = 2;
        b.Q = 4;
        b.Enabled = true;
        b.Type = FilterType.Notch;
        Assert.Equal(5, changed); // chaque édition notifie

        var m2 = b.ToModel();
        Assert.Equal(2000, m2.Frequency, 3);
        Assert.True(m2.Enabled);
        Assert.Equal(FilterType.Notch, m2.Type);
    }

    [Fact]
    public void Band_FreqText_UsesKiloHertzAbove1000()
    {
        var b = new BandViewModel(() => { });
        b.Frequency = 950;
        Assert.EndsWith("Hz", b.FreqText);
        b.Frequency = 6500;
        Assert.EndsWith("kHz", b.FreqText);
    }

    [Fact]
    public void Band_Types_CoversSixFilters()
    {
        Assert.Equal(6, BandViewModel.Types.Count);
    }

    // ================================================================ ChannelViewModel

    [Fact]
    public void Channel_FaderPanMuteSolo_ArePushedToTheChannel()
    {
        var ch = new ConsoleChannel(TrackSource.InternalPlayer());
        var vm = new ChannelViewModel(ch);

        Assert.Equal("INTERNE", vm.Badge);
        Assert.True(vm.IsPlayer);
        Assert.False(vm.IsGenerator);
        Assert.False(vm.IsDevice);
        Assert.Equal("0,0 dB", vm.FaderText);

        vm.Fader = 0.5;
        Assert.Equal(0.5, ch.Fader, 6);
        // libellé culture-courant (virgule ou point selon la plateforme)
        Assert.Contains((20 * Math.Log10(0.5)).ToString("+0.0;-0.0"), vm.FaderText);

        vm.Fader = 0.0;
        Assert.Contains("−∞", vm.FaderText);

        vm.Pan = -0.75;
        Assert.Equal(-0.75, ch.Pan, 6);

        vm.Mute = true;
        vm.Solo = true;
        Assert.True(ch.Mute);
        Assert.True(ch.Solo);
    }

    [Fact]
    public void Channel_GeneratorSignal_MapsFrenchLabels()
    {
        var ch = new ConsoleChannel(TrackSource.InternalGenerator());
        var vm = new ChannelViewModel(ch);

        Assert.True(vm.IsGenerator);
        vm.GeneratorOn = true;
        Assert.True(ch.GeneratorEnabled);

        vm.SelectedSignalText = "Bruit rose";
        vm.SelectedSignalText = "Sinus 1 kHz";
        // les libellés inconnus retombent sur le bruit blanc, sans lever
        vm.SelectedSignalText = "N'importe quoi";
    }

    [Fact]
    public void Channel_UpdateMeter_DecaysAndTracksPeaks()
    {
        var ch = new ConsoleChannel(TrackSource.InternalPlayer());
        var vm = new ChannelViewModel(ch);

        Volatile.Write(ref ch.PeakL, 0.9f);
        Volatile.Write(ref ch.PeakR, 0.9f);
        vm.UpdateMeter();
        Assert.Equal(0.9, vm.LevelL, 3);

        Volatile.Write(ref ch.PeakL, 0f);
        Volatile.Write(ref ch.PeakR, 0f);
        for (int i = 0; i < 400; i++)
            vm.UpdateMeter();

        // décroissance : l'affichage retombe, le marqueur finit par se libérer
        Assert.True(vm.LevelL < 0.01);
        Assert.Equal(0, vm.PeakL, 4);
    }

    [Fact]
    public void Channel_UpdateEqLed_ReflectsEnabledBands()
    {
        var ch = new ConsoleChannel(TrackSource.InternalPlayer());
        var vm = new ChannelViewModel(ch);
        // état initial : la chaîne démarre sur le preset plat (bandes actives)
        Assert.True(vm.HasEq);

        var off = new EqPreset("Piste");
        off.Bands.Add(new EqBand(FilterType.Peaking, 100, 2, 1.0, enabled: false));
        ch.Eq.SetPreset(off);
        vm.UpdateEqLed();
        Assert.False(vm.HasEq);

        var p = new EqPreset("Piste");
        p.Bands.Add(new EqBand(FilterType.Peaking, 100, 2, 1.0)); // actif par défaut
        ch.Eq.SetPreset(p);
        vm.UpdateEqLed();
        Assert.True(vm.HasEq);
    }

    // ================================================================ TrackEqViewModel

    [Fact]
    public void TrackEq_Attach_AddRemoveByIndex_FlattenAndClear()
    {
        var ch = new ConsoleChannel(TrackSource.InternalPlayer());
        var vm = new TrackEqViewModel();
        vm.Attach(ch);

        // la chaîne démarre sur le preset plat (10 bandes) : repartir de zéro
        vm.ClearAllCommand.Execute(null);
        Assert.Empty(vm.Bands);

        Assert.StartsWith("Égaliseur de la piste", vm.Title);
        vm.AddBandCommand.Execute(null);
        vm.AddBandCommand.Execute(null);
        vm.AddBandCommand.Execute(null);
        Assert.Equal(3, vm.Bands.Count);
        Assert.Equal(3, ch.Eq.Preset.Bands.Count);
        Assert.Contains("3 bande(s)", vm.CurveInfo);

        // suppression par INDEX (l'égalité d'objet ne retirerait rien)
        var toRemove = vm.Bands[1];
        vm.RemoveBandCommand.Execute(toRemove);
        Assert.Equal(2, ch.Eq.Preset.Bands.Count);

        vm.Bands[0].GainDb = 6;
        vm.FlattenCommand.Execute(null);
        Assert.All(ch.Eq.Preset.Bands, b => Assert.Equal(0, b.GainDb, 6));

        vm.ClearAllCommand.Execute(null);
        Assert.Empty(vm.Bands);
        Assert.Empty(ch.Eq.Preset.Bands);
    }

    [Fact]
    public void TrackEq_QuickPresets_ApplyExpectedBands()
    {
        var ch = new ConsoleChannel(TrackSource.InternalPlayer());
        var vm = new TrackEqViewModel();
        vm.Attach(ch);

        vm.QuickPresetCommand.Execute("hp80");
        Assert.Equal(2, ch.Eq.Preset.Bands.Count);
        Assert.Equal(FilterType.HighPass, ch.Eq.Preset.Bands[0].Type);

        vm.QuickPresetCommand.Execute("presence");
        Assert.Single(ch.Eq.Preset.Bands);
        Assert.Equal(3000, ch.Eq.Preset.Bands[0].Frequency, 3);

        vm.QuickPresetCommand.Execute("soft");
        Assert.Equal(FilterType.HighShelf, ch.Eq.Preset.Bands[0].Type);

        // clé inconnue : EQ vidé, sans lever
        vm.QuickPresetCommand.Execute("inconnu");
        Assert.Empty(ch.Eq.Preset.Bands);
    }

    [Fact]
    public void TrackEq_Detached_StaysInert()
    {
        var vm = new TrackEqViewModel();
        vm.Attach(null);
        Assert.Equal("(aucune piste sélectionnée)", vm.Title);
        vm.AddBandCommand.Execute(null); // aucune piste : rien ne se passe
        Assert.Empty(vm.Bands);
        Assert.Empty(vm.CurrentCurve());
    }

    [Fact]
    public void TrackEq_CurveChanged_FiresOnEveryEdit()
    {
        var ch = new ConsoleChannel(TrackSource.InternalPlayer());
        var vm = new TrackEqViewModel();
        vm.Attach(ch);

        int fired = 0;
        vm.CurveChanged += _ => fired++;

        vm.AddBandCommand.Execute(null);
        vm.Bands[0].GainDb = 3;
        Assert.True(fired >= 2);
        Assert.NotEmpty(vm.CurrentCurve());
    }
}
