using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using RoomEq.Audio.Routing;
using RoomEq.Core.Dsp;
using RoomEq.Core.Eq;

namespace RoomEq.App.ViewModels;

/// <summary>
/// Éditeur d'égaliseur dédié à une piste de la console : liste de bandes,
/// courbe de réponse et actions rapides. Le preset est poussé dans la EqChain
/// de la piste, qui applique les filtres sans interruption audio —
/// spec I-APP-03.
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
        _channel.Eq.SetPreset(BuildPreset());
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
