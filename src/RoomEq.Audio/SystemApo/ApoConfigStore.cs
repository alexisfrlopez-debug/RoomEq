using System.Text.Json;
using System.Text.Json.Serialization;
using RoomEq.Core.Eq;

namespace RoomEq.Audio.SystemApo;

/// <summary>
/// Courbe active partagée avec l'APO (spec I-AUD-09) : écrit/relit
/// <c>%ProgramData%\RoomEq\apo\active.json</c> — le fichier que le thread de
/// fond de <c>RoomEqApo.dll</c> sonde toutes les 100 ms (dans audiodg.exe,
/// LocalService : PAS d'accès aux profils utilisateurs).
///
/// Format : preset de l'application (champs PascalCase, énumérations en
/// chaînes — mêmes conventions que le magasin de presets, relues par le cœur
/// natif D23) + drapeaux applicatifs « Bypass » et « Revision » ignorés par
/// le parseur de preset mais lus par la couche APO. Écriture atomique
/// (temp + renommage) : l'APO ne voit jamais un fichier à moitié écrit.
/// </summary>
public sealed class ApoConfigStore(string? directoryOverride = null)
{
    /// <summary>Contenu persisté du fichier de courbe active.</summary>
    public sealed record ActiveCurve(
        string Name,
        double PreampDb,
        List<EqBand> Bands,
        bool Bypass,
        long Revision);

    private readonly object _sync = new();
    private ActiveCurve? _cached;   // dernier état connu (push continu de l'app)

    /// <summary>Répertoire de configuration (override pour les tests).</summary>
    public string ConfigDirectory { get; } =
        directoryOverride ?? ApoConstants.ProgramDataDir;

    public string ActiveCurvePath => Path.Combine(ConfigDirectory, "active.json");

    /// <summary>Dernière courbe connue (lue ou écrite) — null si aucune.</summary>
    public ActiveCurve? Current
    {
        get { lock (_sync) return _cached ??= ReadFile(); }
    }

    /// <summary>Écrit la courbe active (révision incrémentée) — atomique.</summary>
    public void WriteCurve(EqPreset preset, bool bypass)
    {
        if (preset == null)
            throw new ArgumentNullException(nameof(preset));
        lock (_sync)
        {
            long revision = (_cached ?? ReadFile())?.Revision ?? 0;
            var curve = new ActiveCurve(
                preset.Name,
                preset.PreampDb,
                preset.Bands.Select(b => new EqBand(b.Type, b.Frequency, b.GainDb, b.Q, b.Enabled)).ToList(),
                bypass,
                revision + 1);
            WriteFile(curve);
            _cached = curve;
        }
    }

    /// <summary>
    /// Bascule le bypass SANS perdre la courbe courante (mesure de
    /// calibration : l'APO laisse passer le balayage — on mesure la pièce,
    /// pas la correction ; invariant 9).
    /// </summary>
    public void SetBypass(bool bypass)
    {
        lock (_sync)
        {
            var current = _cached ?? ReadFile() ?? new ActiveCurve("Sans nom", 0, new List<EqBand>(), false, 0);
            if (current.Bypass == bypass)
                return; // idempotent : ne réveille pas l'APO pour rien
            var curve = current with { Bypass = bypass, Revision = current.Revision + 1 };
            WriteFile(curve);
            _cached = curve;
        }
    }

    /// <summary>Relit le fichier (après activation, pour vérifier l'état réel).</summary>
    public ActiveCurve? Refresh()
    {
        lock (_sync)
        {
            _cached = ReadFile();
            return _cached;
        }
    }

    /// <summary>Date du dernier écrit du journal de l'APO (null si absent) —
    /// un journal récent après activation prouve que audiodg a chargé la DLL.</summary>
    public DateTime? ApoLogLastWriteUtc
    {
        get
        {
            try
            {
                var p = Path.Combine(ConfigDirectory, "apo.log");
                return File.Exists(p) ? File.GetLastWriteTimeUtc(p) : null;
            }
            catch
            {
                return null;
            }
        }
    }

    /// <summary>
    /// Dernières lignes du journal de l'APO (v3.9.5) : écrit par le thread de
    /// fond de la DLL dans audiodg — « nouvelle courbe publiee (rev N, …) »
    /// prouve que le moteur audio a LU active.json et applique la courbe,
    /// « LockForProcess » prouve le chargement. Liste vide si absent ou
    /// illisible — jamais d'exception (le diagnostic du maillon reste
    /// possible : un journal illisible est lui-même un symptôme de
    /// permissions, et il est distingé de « absent » par la date).
    /// </summary>
    public IReadOnlyList<string> ReadApoLogTail(int maxLines = 8)
    {
        try
        {
            var path = Path.Combine(ConfigDirectory, "apo.log");
            if (!File.Exists(path))
                return Array.Empty<string>();
            // queue du fichier (le journal est capé à 1 Mo par la DLL —
            // on ne charge que ce qui est nécessaire aux dernières lignes)
            const int tailBytes = 4096;
            using var fs = new FileStream(path, FileMode.Open, FileAccess.Read,
                FileShare.ReadWrite | FileShare.Delete);
            long start = Math.Max(0, fs.Length - tailBytes);
            fs.Seek(start, SeekOrigin.Begin);
            using var reader = new StreamReader(fs);
            string text = reader.ReadToEnd();
            var lines = text.Split('\n', StringSplitOptions.RemoveEmptyEntries
                                                 | StringSplitOptions.TrimEntries);
            return lines.Length <= maxLines ? lines : lines[^maxLines..];
        }
        catch
        {
            return Array.Empty<string>();
        }
    }

    // ---------------------------------------------------------------- interne

    private ActiveCurve? ReadFile()
    {
        try
        {
            if (!File.Exists(ActiveCurvePath))
                return null;
            return JsonSerializer.Deserialize<ActiveCurve>(File.ReadAllText(ActiveCurvePath), JsonOpts());
        }
        catch
        {
            return null; // fichier illisible : l'APO garde sa courbe courante
        }
    }

    private void WriteFile(ActiveCurve curve)
    {
        System.IO.Directory.CreateDirectory(ConfigDirectory);
        var json = JsonSerializer.Serialize(curve, JsonOpts());
        var tmp = ActiveCurvePath + ".tmp";
        File.WriteAllText(tmp, json);
        File.Move(tmp, ActiveCurvePath, overwrite: true);
    }

    private static JsonSerializerOptions JsonOpts()
        => new()
        {
            WriteIndented = true,
            Converters = { new JsonStringEnumConverter() },
        };
}
