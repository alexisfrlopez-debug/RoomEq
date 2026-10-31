using System.Text;
using RoomEq.Core.Dsp;
using RoomEq.Core.Eq;

namespace RoomEq.Core.Io;

/// <summary>
/// Écriture de presets FabFilter Pro-Q 3 (fichiers .ffp, spec I-IO-06) :
/// convertit un preset RoomEq (bandes actives + préampli) en fichier binaire
/// directement chargeable dans le greffon (bouton des presets ▸ Load…, ou
/// glisser-déposer du fichier).
/// </summary>
/// <remarks>
/// <para>Format binaire (petit-boutiste) : en-tête ASCII « FQ3p », version 4,
/// nombre de paramètres 334, puis 334 flottants 32 bits — 24 bandes de 13 valeurs
/// suivies de 22 paramètres globaux (12 + 334×4 = 1348 octets).</para>
/// <para>Encodages : fréquence = log2(Hz) ; Q = log10(Q)·0,312098175 + 0,5
/// (0 ↔ Q 0,025 et 1 ↔ Q 40) ; gain de bande en dB ; gain de sortie normalisé
/// (valeur × 36 = dB). Les valeurs hors plages du greffon sont ramenées aux bornes :
/// 10–30 000 Hz, Q 0,025–40, gain de bande ±30 dB, gain de sortie ±36 dB.</para>
/// <para>Mapping des types : Peaking→Bell, LowShelf/HighShelf identiques,
/// HighPass→LowCut, LowPass→HighCut, Notch→Notch. Les coupes sont exportées en
/// 12 dB/oct (pente réelle des biquads RoomEq). Les bandes décochées ne sont pas
/// exportées : le fichier sonne exactement comme l'égaliseur RoomEq à l'écran.</para>
/// </remarks>
public static class FabFilterProQ3
{
    /// <summary>Nombre maximal de bandes d'un preset Pro-Q 3.</summary>
    public const int MaxBands = 24;

    private const int FormatVersion = 4;
    private const int ParamCount = 334;          // 24 bandes × 13 + 22 globaux
    private const double QScale = 0.312098175;   // log10(Q) → [0,1] sur 0,025..40
    private const double OutputGainScale = 36.0; // normalisé → dB (plage -inf..+36 dB)

    // Plages acceptées par le greffon (les valeurs hors plages sont ramenées aux bornes).
    private const double MinFrequencyHz = 10.0;
    private const double MaxFrequencyHz = 30000.0;
    private const double MinGainDb = -30.0;
    private const double MaxGainDb = 30.0;
    private const double MinQ = 0.025;
    private const double MaxQ = 40.0;

    /// <summary>
    /// Encode le preset en octets .ffp Pro-Q 3 : bandes actives (max 24) + préampli
    /// (devenu gain de sortie), les autres réglages aux valeurs d'usine du greffon.
    /// </summary>
    /// <exception cref="InvalidOperationException">plus de 24 bandes actives.</exception>
    public static byte[] Encode(EqPreset preset)
    {
        ArgumentNullException.ThrowIfNull(preset);

        var active = preset.Bands.Where(b => b.Enabled).ToList();
        if (active.Count > MaxBands)
            throw new InvalidOperationException(
                $"Le preset contient {active.Count} bandes actives ; FabFilter Pro-Q 3 en accepte " +
                $"au maximum {MaxBands}. Désactivez ou supprimez des bandes (ou réduisez la précision " +
                "de la correction avant l'import de calibration).");

        using var ms = new MemoryStream(12 + ParamCount * 4);
        using var w = new BinaryWriter(ms, Encoding.UTF8, leaveOpen: true);

        w.Write(Encoding.ASCII.GetBytes("FQ3p"));
        w.Write(FormatVersion);
        w.Write(ParamCount);

        foreach (var b in active)
            WriteBand(w, enabled: true, b.Frequency, b.GainDb, b.Q, TypeCode(b.Type));
        for (int i = active.Count; i < MaxBands; i++)
            WriteVirginSlot(w);

        // ----- 22 paramètres globaux : réglages d'usine du greffon,
        // ----- sauf le gain de sortie qui porte le préampli RoomEq.
        w.Write(0f);                                                            // traitement : zéro latence
        w.Write(1f);                                                            // phase linéaire : moyenne (sans effet ici)
        w.Write(1f);                                                            // échelle de gain : 100 %
        w.Write((float)Math.Clamp(preset.PreampDb / OutputGainScale, -1.0, 1.0)); // gain de sortie = préampli
        w.Write(0f);                                                            // panoramique de sortie
        w.Write(0f);
        w.Write(0f);                                                            // bypass général
        w.Write(0f);                                                            // inversion de phase
        w.Write(0f);                                                            // gain automatique
        w.Write(1f);                                                            // analyseur : pré-traitement
        w.Write(1f);                                                            // analyseur : post-traitement
        w.Write(-1f);                                                           // analyseur : spectre externe coupé
        w.Write(1f);                                                            // analyseur : plage 90 dB
        w.Write(2f);                                                            // analyseur : résolution haute
        w.Write(2f);                                                            // analyseur : vitesse moyenne
        w.Write(3f);                                                            // analyseur : inclinaison 4,5 dB/oct
        w.Write(0f);
        w.Write(1f);                                                            // collisions de spectre
        w.Write(1f);                                                            // spectrum grab
        w.Write(2f);                                                            // plage d'affichage : 12 dB
        w.Write(0f);                                                            // réception MIDI activée (valeur inversée)
        w.Write(0f);

        w.Flush();
        return ms.ToArray();
    }

    /// <summary>Écrit le preset dans un fichier .ffp (écrase un fichier existant).</summary>
    public static void Write(EqPreset preset, string path)
    {
        File.WriteAllBytes(path, Encode(preset));
    }

    /// <summary>Code de type de filtre Pro-Q 3 : Bell 0, LowShelf 1, LowCut 2, HighShelf 3, HighCut 4, Notch 5.</summary>
    private static float TypeCode(FilterType type) => type switch
    {
        FilterType.Peaking => 0f,
        FilterType.LowShelf => 1f,
        FilterType.HighPass => 2f,   // coupe-haut côté greffon = LowCut
        FilterType.HighShelf => 3f,
        FilterType.LowPass => 4f,    // coupe-bas côté greffon = HighCut
        FilterType.Notch => 5f,
        _ => 0f,
    };

    /// <summary>Écrit une bande active : 13 flottants dans l'ordre Pro-Q 3.</summary>
    private static void WriteBand(BinaryWriter w, bool enabled, double freqHz, double gainDb, double q, float typeCode)
    {
        w.Write(enabled ? 1f : 0f);                                    // bande activée
        w.Write(1f);                                                   // non bypassée
        w.Write((float)Math.Log2(Math.Clamp(freqHz, MinFrequencyHz, MaxFrequencyHz)));
        w.Write((float)Math.Clamp(gainDb, MinGainDb, MaxGainDb));
        w.Write(0f);                                                   // plage dynamique (EQ statique)
        w.Write(1f);                                                   // dynamique activée (valeur d'usine)
        w.Write(1f);                                                   // seuil dynamique (valeur d'usine)
        w.Write((float)(Math.Log10(Math.Clamp(q, MinQ, MaxQ)) * QScale + 0.5));
        w.Write(typeCode);
        w.Write(1f);                                                   // pente des coupes : 12 dB/oct
        w.Write(2f);                                                   // placement stéréo
        w.Write(1f);                                                   // valeur d'usine
        w.Write(0f);                                                   // valeur d'usine
    }

    /// <summary>Écrit un emplacement inutilisé, identique à l'octet près au preset vierge d'usine.</summary>
    private static void WriteVirginSlot(BinaryWriter w) =>
        WriteBand(w, enabled: false, 1000.0, 0.0, 1.0, 0f);
}
