using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using RoomEq.Audio.Playback;
using RoomEq.Audio.Routing;
using RoomEq.Core.Dsp;
using RoomEq.Core.Eq;

namespace RoomEq.App.ViewModels;

/// <summary>
/// ViewModel d'une tranche de la table de mixage : bindings pour fader, pan,
/// mute, solo, VU (avec décroissance), commutateurs internes (lecteur /
/// générateur) et LED d'EQ actif. Les crêtes sont lues depuis le moteur via
/// un minuteur d'interface — spec I-APP-03.
/// </summary>
public partial class ChannelViewModel : ObservableObject
{
    private readonly Action? _onConfigChanged;
    private float _dispL, _dispR;

    public ConsoleChannel Channel { get; }

    [ObservableProperty] private string title = "";
    [ObservableProperty] private string badge = "";
    [ObservableProperty] private string badgeColor = "#22D3EE";
    [ObservableProperty] private double fader = 1.0;
    [ObservableProperty] private double pan;
    [ObservableProperty] private bool mute;
    [ObservableProperty] private bool solo;
    [ObservableProperty] private bool isSelected;
    [ObservableProperty] private bool hasEq;
    [ObservableProperty] private string faderText = "0,0 dB";
    [ObservableProperty] private string? playerFileName;
    [ObservableProperty] private bool generatorOn;
    [ObservableProperty] private string selectedSignalText = "Bruit blanc";
    [ObservableProperty] private double levelL;
    [ObservableProperty] private double levelR;
    [ObservableProperty] private double peakL;
    [ObservableProperty] private double peakR;
    [ObservableProperty] private bool sourceError;

    public static IReadOnlyList<string> SignalTexts { get; } =
        new[] { "Bruit blanc", "Bruit rose", "Sinus 1 kHz" };

    public ChannelViewModel(ConsoleChannel channel, Action? onConfigChanged = null)
    {
        Channel = channel;
        _onConfigChanged = onConfigChanged;
        title = channel.Title;
        (Badge, BadgeColor) = channel.Kind switch
        {
            TrackSourceKind.Capture => AudioDeviceCatalog.IsVirtualCapture(channel.Source.Name)
                ? ("CARTE VIRTUELLE", "#A78BFA")
                : ("ENTRÉE", "#22D3EE"),
            TrackSourceKind.Loopback => ("BOUCLE WINDOWS", "#4ADE80"),
            TrackSourceKind.Player => ("INTERNE", "#FF9F1C"),
            _ => ("INTERNE", "#FF9F1C")
        };
        RefreshIdentity();
        UpdateEqLed();
    }

    public void RefreshIdentity()
    {
        Title = Channel.Title;
        PlayerFileName = Channel.PlayerFileName;
        OnPropertyChanged(nameof(HasFile));
    }

    public TrackSourceKind Kind => Channel.Kind;

    /// <summary>Affichage conditionnel dans le gabarit de tranche.</summary>
    public bool IsPlayer => Kind == TrackSourceKind.Player;
    public bool IsGenerator => Kind == TrackSourceKind.Generator;
    public bool IsDevice => Kind is TrackSourceKind.Capture or TrackSourceKind.Loopback;
    public bool HasFile => IsPlayer && PlayerFileName != null;

    // ------------------------------------------------------------ bindings

    partial void OnFaderChanged(double value)
    {
        Channel.Fader = value;
        FaderText = value <= 0.001 ? "−∞ dB" : (20 * Math.Log10(value)).ToString("+0.0;-0.0") + " dB";
        _onConfigChanged?.Invoke();
    }

    partial void OnPanChanged(double value)
    {
        Channel.Pan = value;
        _onConfigChanged?.Invoke();
    }

    partial void OnMuteChanged(bool value) => Channel.Mute = value;
    partial void OnSoloChanged(bool value) => Channel.Solo = value;

    partial void OnGeneratorOnChanged(bool value)
    {
        Channel.GeneratorEnabled = value;
        _onConfigChanged?.Invoke();
    }

    partial void OnSelectedSignalTextChanged(string value) => ApplyGeneratorSignal();

    private void ApplyGeneratorSignal()
    {
        var sig = SelectedSignalText switch
        {
            "Bruit rose" => TestSignal.PinkNoise,
            "Sinus 1 kHz" => TestSignal.Sine,
            _ => TestSignal.WhiteNoise
        };
        Channel.SetGeneratorSignal(sig, 0.25, 1000);
        _onConfigChanged?.Invoke();
    }

    // ------------------------------------------------------------ commandes

    public void EjectFile()
    {
        Channel.UnloadFile();
        RefreshIdentity();
    }

    /// <summary>Recalcule la LED « EQ actif » (appelé après édition de l'EQ de piste).</summary>
    public void UpdateEqLed()
        => HasEq = Channel.Eq.Preset.Bands.Any(b => b.Enabled);

    // ------------------------------------------------------------ VU

    /// <summary>Actualise l'affichage des crêtes (thread UI, ~80 ms).</summary>
    public void UpdateMeter()
    {
        float l = Volatile.Read(ref Channel.PeakL);
        float r = Volatile.Read(ref Channel.PeakR);
        _dispL = Math.Max(l, _dispL * 0.80f);
        _dispR = Math.Max(r, _dispR * 0.80f);
        LevelL = _dispL;
        LevelR = _dispR;

        // marqueurs de crête tenus ~1,2 s
        double hold = 0.985;
        PeakL = Math.Max(l, PeakL * hold);
        PeakR = Math.Max(r, PeakR * hold);
        if (PeakL < 0.004) PeakL = 0;
        if (PeakR < 0.004) PeakR = 0;

        SourceError = Channel.CaptureStoppedWithErrors != null;
    }
}
