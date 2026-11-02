namespace RoomEq.Audio.SystemApo;

/// <summary>
/// Photographie de la chaîne EQ système pour le panneau (correctif v3.9.5 —
/// « l'EQ est activé et chargé mais je n'entends pas de différence ») :
/// les maillons y sont visibles séparément, chacun avec son état :
/// (0) les prérequis MACHINE sans lesquels le moteur audio ne charge JAMAIS
///     la DLL (v3.9.6 : politique « audio protégé », inscription moteur,
///     inscription COM — null = inconnu/hors Windows, liens non affichés) ;
/// (1) la courbe que l'application a écrite dans active.json ;
/// (2) l'écriture elle-même (erreur éventuelle, permissions %ProgramData%) ;
/// (3) ce que le moteur audio (audiodg) en a fait — son journal.
/// </summary>
public sealed record ApoChainState(
    ApoConfigStore.ActiveCurve? Curve,
    string? CurveWriteError,
    IReadOnlyList<string> ApoLogTail,
    DateTime? ApoLogLastWriteUtc,
    bool? EnginePolicyOk = null,
    bool? EngineRegistered = null,
    bool? ComRegistered = null);

/// <summary>
/// Composeur PUR du texte d'état de la chaîne EQ système (v3.9.5) — même
/// philosophie que le diagnostic par maillon du takeover (v3.9.3) : après
/// cette version, une capture d'écran du panneau nomme le maillon défaillant
/// SANS aller-retour terrain : courbe jamais écrite / écriture refusée /
/// bypass actif / APO qui ne publie aucune courbe / chaîne complète.
/// Testable sous Linux (aucune dépendance Windows).
/// </summary>
public static class ApoChainText
{
    /// <summary>Texte du panneau quand au moins une sortie est activée.</summary>
    public static string Compose(ApoChainState state)
    {
        ArgumentNullException.ThrowIfNull(state);
        var lines = new List<string>
        {
            "EQ système actif — tout le son des sorties marquées ● traverse l'égaliseur.",
        };

        // ---- maillon 1 : la courbe écrite par l'application (active.json) ----
        if (state.CurveWriteError != null)
        {
            lines.Add("⚠ Écriture de la courbe IMPOSSIBLE : " + state.CurveWriteError
                + " — l'onglet Égaliseur n'atteint pas l'APO. Vérifiez les permissions de "
                + "%ProgramData%\\RoomEq\\apo (les Utilisateurs doivent avoir « Modifier »), puis réparez.");
        }
        else if (state.Curve == null)
        {
            lines.Add("⚠ AUCUNE courbe écrite (active.json absent) : la courbe de l'onglet "
                + "Égaliseur n'a jamais été envoyée à l'APO — réactivez une sortie ou cliquez « Réparer ».");
        }
        else if (state.Curve.Bypass)
        {
            lines.Add("⚠ Courbe CONTOURNÉE (bypass de l'onglet Égaliseur actif) : l'APO laisse "
                + "passer le son tel quel — décochez « Bypass » dans l'onglet Égaliseur pour entendre la correction.");
        }
        else
        {
            int actives = state.Curve.Bands.Count(b => b.Enabled && Math.Abs(b.GainDb) > 0.001);
            lines.Add(actives == 0
                ? "⚠ Courbe envoyée mais PLATE : " + state.Curve.Bands.Count + " bandes, aucune active — "
                  + "chargez une correction dans l'onglet Égaliseur (l'APO applique exactement cette courbe)."
                : "Courbe envoyée à l'APO : " + actives + " bandes actives · préampli "
                  + state.Curve.PreampDb.ToString("+0.#;-0.#;0") + " dB · révision " + state.Curve.Revision
                  + " (onglet Égaliseur).");
        }

        // ---- maillon 2 : le moteur audio a-t-il publié cette courbe ? (apo.log) ----
        string? lastPublish = LastLine(state.ApoLogTail, "nouvelle courbe publiee");
        string? lastLoad = LastLine(state.ApoLogTail, "LockForProcess");
        if (state.ApoLogTail.Count == 0 && state.ApoLogLastWriteUtc == null)
        {
            // v3.9.6 : la DLL n'a jamais été chargée — NOMMER le prérequis
            // manquant au lieu d'un générique « pilote ? » : trois causes
            // machine, toutes réparables par le bouton « Réparer ».
            if (state.EnginePolicyOk == false)
                lines.Add("⚠ Windows refuse de charger la DLL de l'égaliseur (APO non signée, "
                    + "moteur audio protégé) : cliquez « Réparer » — RoomEq appliquera le réglage "
                    + "éprouvé d'Equalizer APO (vérification de signature désactivée, réversible).");
            else if (state.EngineRegistered == false)
                lines.Add("⚠ DLL jamais chargée : l'égaliseur n'est pas inscrit auprès du moteur "
                    + "audio (clé AudioEngine\\AudioProcessingObjects) — cliquez « Réparer ».");
            else if (state.ComRegistered == false)
                lines.Add("⚠ DLL jamais chargée : inscription COM absente (la DLL déployée n'est "
                    + "pas enregistrée) — cliquez « Réparer ».");
            else
                lines.Add("⚠ Journal de l'APO absent : la DLL n'a JAMAIS été chargée par le moteur audio "
                    + "(sortie réellement utilisée ? pilote ?) — cliquez « Réparer ».");
        }
        else if (state.ApoLogTail.Count == 0)
        {
            lines.Add("⚠ Journal de l'APO illisible (permissions %ProgramData%\\RoomEq\\apo).");
        }
        else if (state.Curve != null && TryRevision(lastPublish, out long published)
                 && published == state.Curve.Revision && !state.Curve.Bypass)
        {
            lines.Add("✓ APO : courbe publiée et appliquée par le moteur audio (révision "
                + published + ") — la chaîne est complète de bout en bout.");
        }
        else if (state.Curve != null && TryRevision(lastPublish, out long older)
                 && older < state.Curve.Revision)
        {
            lines.Add("… APO : il applique encore la révision " + older + " (dernière envoyée : "
                + state.Curve.Revision + ") — le moteur audio n'a pas repris la dernière courbe "
                + "(relancez la lecture, ou « Réparer » si ça dure).");
        }
        else if (lastLoad != null && lastPublish == null)
        {
            lines.Add("⚠ APO chargé par le moteur audio mais AUCUNE courbe publiée : il ne lit pas "
                + "%ProgramData%\\RoomEq\\apo\\active.json (permissions) — vérifiez que les "
                + "Utilisateurs y ont accès en lecture, puis réparez.");
        }
        else if (lastPublish != null)
        {
            lines.Add("APO : " + lastPublish + " — (rév. appliquée par le moteur audio).");
        }
        else
        {
            lines.Add("APO : journal présent (dernière ligne : « "
                + state.ApoLogTail[^1].Trim() + " »).");
        }

        return string.Join(Environment.NewLine, lines);
    }

    private static string? LastLine(IReadOnlyList<string> lines, string marker)
    {
        for (int i = lines.Count - 1; i >= 0; i--)
            if (lines[i].Contains(marker, StringComparison.Ordinal))
                return lines[i];
        return null;
    }

    /// <summary>Extrait la révision d'une ligne « nouvelle courbe publiee (rev N, …) ».</summary>
    private static bool TryRevision(string? line, out long revision)
    {
        revision = 0;
        if (line == null)
            return false;
        int i = line.IndexOf("rev ", StringComparison.Ordinal);
        if (i < 0)
            return false;
        int start = i + 4;
        int end = start;
        while (end < line.Length && (char.IsDigit(line[end]) || line[end] == ','))
            end++;
        return long.TryParse(line[start..end].TrimEnd(','), out revision) && revision >= 0;
    }
}
