using CommunityToolkit.Mvvm.ComponentModel;
using RoomEq.Audio.Measurement;
using RoomEq.Audio.Playback;
using RoomEq.Core.Calibration;
using RoomEq.Core.Dsp;

namespace RoomEq.App.ViewModels;

/// <summary>Option de signal cochée pour la mesure multi-signaux — spec I-APP-02.</summary>
public partial class SignalOption : ObservableObject
{
    public MeasurementSignalKind Kind { get; }
    public string Name { get; }
    public string Description { get; }

    [ObservableProperty] private bool isSelected = true;

    public SignalOption(MeasurementSignalKind kind)
    {
        Kind = kind;
        Name = MeasurementSignalCatalog.DisplayName(kind);
        Description = MeasurementSignalCatalog.Description(kind);
    }
}

/// <summary>
/// Micro de mesure proposé dans la liste : les cartes virtuelles (RoomEq
/// Output…) sont signalées — elles ne captent pas la pièce et ne doivent
/// être choisies que dans des cas très particuliers.
/// </summary>
public sealed class MicDeviceOption
{
    public OutputDeviceInfo Device { get; }
    public bool IsVirtual { get; }
    public string DisplayName { get; }

    public MicDeviceOption(OutputDeviceInfo device)
    {
        Device = device;
        string n = device.DisplayName;
        // détection des cartes virtuelles : union des motifs des fournisseurs
        // pris en charge (carte RoomEq intégrée), pas de noms tiers codés en dur
        IsVirtual = ContainsAny(n, "roomeq output", "roomeq input", "virtual");
        DisplayName = IsVirtual ? n + "  — carte virtuelle (ne capte pas la pièce)" : n;
    }

    private static bool ContainsAny(string text, params string[] needles)
    {
        string lower = text.ToLowerInvariant();
        return needles.Any(lower.Contains);
    }
}

/// <summary>Option de canal du micro : mixage ou canal unique (mono).</summary>
public sealed class ChannelOption
{
    /// <summary>Index du canal (0 = canal 1) ou ChannelMixer.MixFirstTwo pour le mixage.</summary>
    public int Index { get; }
    public string Label { get; }

    public ChannelOption(int index, string label)
    {
        Index = index;
        Label = label;
    }
}

/// <summary>Option de courbe cible avec libellé français.</summary>
public sealed class TargetOption
{
    public TargetType Value { get; }
    public string Label { get; }

    public TargetOption(TargetType value)
    {
        Value = value;
        Label = value switch
        {
            TargetType.Flat => "Plate (neutre)",
            TargetType.BassShelf => "Plate + shelf de graves",
            TargetType.StudioTilt => "Studio (pente -1 dB/oct)",
            _ => value.ToString()
        };
    }
}
