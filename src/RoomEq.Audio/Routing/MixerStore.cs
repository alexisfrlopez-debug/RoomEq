using System.Text.Json;
using System.Text.Json.Serialization;
using RoomEq.Core.Eq;

namespace RoomEq.Audio.Routing;

/// <summary>DTO d'une tranche sauvegardée.</summary>
public sealed class StoredChannel
{
    public string Kind { get; set; } = "capture"; // capture | loopback | player | generator
    public string DeviceId { get; set; } = "";
    public string Name { get; set; } = "";
    public double Fader { get; set; } = 1.0;
    public double Pan { get; set; }
    public bool Mute { get; set; }
    public bool Solo { get; set; }
    public bool GeneratorEnabled { get; set; }
    public string? GeneratorSignal { get; set; }
    public string? FilePath { get; set; }
    public EqPreset? Eq { get; set; }
}

/// <summary>DTO complet de la configuration de la console.</summary>
public sealed class StoredMixer
{
    public string? MasterOutputId { get; set; }
    public double MasterFader { get; set; } = 1.0;
    public bool MasterMute { get; set; }
    public bool AutoStart { get; set; }
    /// <summary>Protocole de carte son virtuelle sélectionné (vbcable, voicemeeter, vac, loopback).</summary>
    public string? VirtualProviderId { get; set; }
    /// <summary>Après une installation réussie : Windows bascule vers la carte virtuelle et le routage démarre.</summary>
    public bool AutoRouteOnInstall { get; set; } = true;
    /// <summary>Pendant le routage : bascule aussi l'entrée par défaut vers la sortie de la carte virtuelle.</summary>
    public bool SwitchDefaultCapture { get; set; } = true;
    /// <summary>Sortie par défaut de Windows mémorisée avant bascule (restaurée à l'arrêt du routage).</summary>
    public string? SavedDefaultRenderId { get; set; }
    /// <summary>Entrée par défaut de Windows mémorisée avant bascule (restaurée à l'arrêt du routage).</summary>
    public string? SavedDefaultCaptureId { get; set; }
    public List<StoredChannel> Channels { get; set; } = new();
}

/// <summary>
/// Persistance de la configuration de la table de mixage dans
/// %APPDATA%\RoomEq\mixer.json : pistes, faders, EQ par piste, sortie master.
/// </summary>
public sealed class MixerStore
{
    private static readonly JsonSerializerOptions Options = new()
    {
        WriteIndented = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
    };

    public string Directory { get; }
    public string FilePath { get; }

    /// <summary>Magasin par défaut : %APPDATA%\RoomEq\mixer.json.</summary>
    public MixerStore()
        : this(Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "RoomEq"))
    {
    }

    /// <summary>Magasin dans un répertoire imposé (tests, mode portable…).</summary>
    public MixerStore(string directory)
    {
        Directory = directory;
        FilePath = Path.Combine(directory, "mixer.json");
    }

    public void Save(StoredMixer mixer)
    {
        System.IO.Directory.CreateDirectory(Directory);
        var json = JsonSerializer.Serialize(mixer, Options);
        File.WriteAllText(FilePath, json);
    }

    public StoredMixer Load()
    {
        try
        {
            if (!File.Exists(FilePath)) return new StoredMixer();
            var json = File.ReadAllText(FilePath);
            return JsonSerializer.Deserialize<StoredMixer>(json, Options) ?? new StoredMixer();
        }
        catch
        {
            return new StoredMixer();
        }
    }
}
