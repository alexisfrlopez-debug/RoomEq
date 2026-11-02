namespace RoomEq.Audio.SystemApo;

/// <summary>
/// Niveau de trace du moteur audio dans le journal de l'APO (v3.9.9).
/// </summary>
public enum ApoEngineTrace
{
    /// <summary>Aucune ligne de chargement : le moteur n'a jamais créé l'APO.</summary>
    None,

    /// <summary>Lignes de cycle de vie présentes mais SANS preuve
    /// d'instanciation agrégée (une création directe les écrit aussi —
    /// l'auto-test pré-vol notamment, retour terrain v3.9.8 : le verdict
/// affichait « instanciée par le moteur » alors que les lignes venaient
    /// de NOTRE auto-test dans le processus élevé).</summary>
    DirectOnly,

    /// <summary>Instanciation AGRÉGÉE vue : « APO instanciee (AGREGEE par le
    /// moteur audio) » — seule la création AVEC contrôleur d'agrégation
    /// (audiodg) écrit cette ligne : preuve directe du moteur.</summary>
    Aggregated,
}

/// <summary>
/// Classification PURE du journal de l'APO (v3.9.9 — correctif du faux
/// « instanciée par le moteur audio » du verdict v3.9.8). L'auto-test
/// pré-vol s'exécute DANS le processus élevé et écrit exactement les mêmes
/// lignes de cycle de vie que le moteur (« APO instanciee », « LockForProcess »,
/// « UnlockForProcess ») : chercher ces mots dans TOUT le journal ne prouve
/// rien. L'auto-test appose donc un MARQUEUR à la fin de son exécution — ne
/// compte que ce qui est écrit APRÈS le dernier marqueur. Et l'instanciation
/// agrégée (v3.9.9 : le moteur audio crée l'APO avec un contrôleur COM) porte
/// une mention propre : c'est la seule preuve irréfutable du moteur.
/// Aucune dépendance Windows : testable sous Linux.
/// </summary>
public static class ApoLogTrace
{
    /// <summary>
    /// Ligne apposée par l'auto-test à la fin de son exécution (processus
    /// élevé). Tout ce qui suit le DERNIER marqueur est postérieur à
    /// l'auto-test : seul le moteur audio écrit alors dans le journal.
    /// </summary>
    public const string SelfTestMarker = "=== auto-test termine (processus eleve) ===";

    /// <summary>La création agrégée ne s'écrit QUE côté moteur (v3.9.9).</summary>
    private const string AggregatedMarker = "AGREGEE";

    /// <summary>
    /// Classe un texte de journal : que sait-on du chargement PAR LE MOTEUR ?
    /// </summary>
    /// <param name="logText">Contenu du journal (null = aucun journal).</param>
    public static ApoEngineTrace Classify(string? logText)
    {
        if (string.IsNullOrEmpty(logText))
            return ApoEngineTrace.None;

        // ne considérer que ce qui suit le DERNIER marqueur d'auto-test
        // (absent = journal d'avant v3.9.9 : comportement historique, tout le texte)
        string relevant = logText;
        int marker = logText.LastIndexOf(SelfTestMarker, StringComparison.Ordinal);
        if (marker >= 0)
            relevant = logText.Substring(marker + SelfTestMarker.Length);

        if (relevant.Contains(AggregatedMarker, StringComparison.Ordinal))
            return ApoEngineTrace.Aggregated;
        if (relevant.Contains("APO instancie", StringComparison.Ordinal)
            || relevant.Contains("LockForProcess", StringComparison.Ordinal)
            || relevant.Contains("surveillant actif", StringComparison.Ordinal))
            return ApoEngineTrace.DirectOnly;
        return ApoEngineTrace.None;
    }

    /// <summary>L'APO a-t-elle été instanciée après le dernier auto-test ?</summary>
    public static bool ShowsLoad(string? logText)
        => Classify(logText) != ApoEngineTrace.None;
}
