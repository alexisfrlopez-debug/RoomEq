using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using RoomEq.Audio.Playback;
using RoomEq.Audio.Routing;
using RoomEq.Core.Dsp;
using RoomEq.Core.Eq;

namespace RoomEq.App.ViewModels;

/// <summary>
/// ViewModel d'une tranche de la table de mixage : bindings pour fader, pan, mute,
/// solo, VU (avec décroissance), commutateurs internes (lecteur / générateur) et
/// LED d'EQ actif. Les crêtes sont lues depuis le moteur via un minuteur d'interface.
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

/// <summary>
/// Éditeur d'égaliseur dédié à une piste de la console : liste de bandes, courbe de
/// réponse et actions rapides. Le preset est poussé dans la EqChain de la piste,
/// qui applique les filtres sans interruption audio.
/// </summary>
public partial class TrackEqViewModel : ObservableObject
{
    private ConsoleChannel? _channel;

    [ObservableProperty] private string title = "(aucune piste sélectionnée)";
    [ObservableProperty] private ObservableCollection<BandViewModel> bands = new();
    [ObservableProperty] private int selectedIndex = -1;
    [ObservableProperty] private string curveInfo = "";

    public event Action<List<CurvePoint>>? CurveChanged;

    private List<CurvePoint> _lastCurve = new();
    public List<CurvePoint> CurrentCurve() => _lastCurve;

    /// <summary>Attache l'éditeur à la piste sélectionnée (null = détache).</summary>
    public void Attach(ConsoleChannel? channel)
    {
        _channel = channel;
        Title = channel == null
            ? "(aucune piste sélectionnée)"
            : "Égaliseur de la piste : " + channel.Title;
        ReloadFromChannel();
    }

    private void ReloadFromChannel()
    {
        var preset = _channel?.Eq.Preset ?? new EqPreset("Aucun");
        Bands = new ObservableCollection<BandViewModel>(
            preset.Bands.Select(b => new BandViewModel(b, BandChanged)));
        SelectedIndex = Bands.Count > 0 ? 0 : -1;
        RecomputeCurve();
    }

    private void PushToChannel()
    {
        if (_channel == null) return;
        var p = new EqPreset("Piste");
        foreach (var b in Bands) p.Bands.Add(b.ToModel());
        _channel.Eq.SetPreset(p);
    }

    private void BandChanged()
    {
        PushToChannel();
        RecomputeCurve();
    }

    private void RecomputeCurve()
    {
        var chain = _channel?.Eq;
        _lastCurve = chain == null ? new List<CurvePoint>() : chain.ResponseCurve(20, 20000, 240);
        CurveChanged?.Invoke(_lastCurve);
        double peak = _lastCurve.Count > 0 ? _lastCurve.Max(c => Math.Abs(c.Db)) : 0;
        CurveInfo = _channel == null
            ? ""
            : $"{Bands.Count} bande(s) · amplitude max {peak:0.0} dB";
    }

    [RelayCommand]
    private void AddBand()
    {
        if (_channel == null) return;
        var current = BuildPreset();
        current.Bands.Add(new EqBand(FilterType.Peaking, 1000, 0, 1.0));
        ApplyPreset(current);
        SelectedIndex = Bands.Count - 1;
    }

    [RelayCommand]
    private void RemoveBand(BandViewModel? band)
    {
        if (band == null || _channel == null) return;
        // suppression par INDEX : BandViewModel.ToModel() crée une nouvelle instance,
        // un Remove par égalité d'objet ne retirerait jamais rien
        int idx = Bands.IndexOf(band);
        if (idx < 0) return;
        var current = BuildPreset();
        if (idx < current.Bands.Count) current.Bands.RemoveAt(idx);
        ApplyPreset(current);
    }

    [RelayCommand]
    private void Flatten()
    {
        if (_channel == null) return;
        var current = BuildPreset();
        foreach (var b in current.Bands) b.GainDb = 0;
        ApplyPreset(current);
    }

    /// <summary>Aucun EQ : liste de bandes vide (tranche transparente).</summary>
    [RelayCommand]
    private void ClearAll()
    {
        if (_channel == null) return;
        ApplyPreset(new EqPreset("Aucun"));
    }

    /// <summary>Presets d'ornement rapides pour une piste.</summary>
    [RelayCommand]
    private void QuickPreset(string? key)
    {
        if (_channel == null) return;
        EqPreset p = key switch
        {
            "hp80" => Quick(new EqBand(FilterType.HighPass, 80, 0, 0.71),
                            new EqBand(FilterType.LowShelf, 120, -1.5, 0.7), "Coupe-bas 80 Hz"),
            "presence" => Quick(new EqBand(FilterType.Peaking, 3000, 3.0, 1.4), "Présence 3 kHz"),
            "soft" => Quick(new EqBand(FilterType.HighShelf, 8000, -3.0, 0.71), "Aigus adoucis"),
            _ => new EqPreset("Aucun")
        };
        ApplyPreset(p);
    }

    private static EqPreset Quick(EqBand band, string name)
    {
        var p = new EqPreset(name);
        p.Bands.Add(band);
        return p;
    }

    private static EqPreset Quick(EqBand b1, EqBand b2, string name)
    {
        var p = new EqPreset(name);
        p.Bands.Add(b1);
        p.Bands.Add(b2);
        return p;
    }

    private EqPreset BuildPreset()
    {
        var p = new EqPreset("Piste");
        foreach (var b in Bands) p.Bands.Add(b.ToModel());
        return p;
    }

    private void ApplyPreset(EqPreset preset)
    {
        _channel?.Eq.SetPreset(preset);
        ReloadFromChannel();
    }
}
