using CommunityToolkit.Mvvm.ComponentModel;
using RoomEq.Core.Dsp;
using RoomEq.Core.Eq;

namespace RoomEq.App.ViewModels;

/// <summary>Bande d'égaliseur éditable (panneau latéral + poignées de l'EqControl) — spec I-APP-04.</summary>
public partial class BandViewModel : ObservableObject
{
    private readonly Action _onChanged;

    [ObservableProperty] private double frequency;
    [ObservableProperty] private double gainDb;
    [ObservableProperty] private double q;
    [ObservableProperty] private bool enabled;
    [ObservableProperty] private FilterType type;

    public static IReadOnlyList<FilterType> Types { get; } = new[]
    {
        FilterType.Peaking, FilterType.LowShelf, FilterType.HighShelf,
        FilterType.LowPass, FilterType.HighPass, FilterType.Notch
    };

    public BandViewModel(Action onChanged)
    {
        _onChanged = onChanged;
    }

    public BandViewModel(EqBand model, Action onChanged)
        : this(onChanged)
    {
        frequency = model.Frequency;
        gainDb = model.GainDb;
        q = model.Q;
        enabled = model.Enabled;
        type = model.Type;
    }

    public string FreqText => Frequency >= 1000
        ? (Frequency / 1000).ToString("0.00") + " kHz"
        : Frequency.ToString("0") + " Hz";

    public EqBand ToModel() => new(Type, Frequency, GainDb, Q, Enabled);

    partial void OnFrequencyChanged(double value)
    {
        OnPropertyChanged(nameof(FreqText));
        _onChanged();
    }
    partial void OnGainDbChanged(double value) => _onChanged();
    partial void OnQChanged(double value) => _onChanged();
    partial void OnEnabledChanged(bool value) => _onChanged();
    partial void OnTypeChanged(FilterType value) => _onChanged();
}
