namespace RoomEq.Audio.SystemApo;

/// <summary>Verdict de santé d'une activation EQ système (v3.9.7).</summary>
public enum ApoActivationHealth
{
    /// <summary>Sortie vivante + journal de l'APO écrit : chaîne complète.</summary>
    Healthy,

    /// <summary>Sortie vivante mais journal muet (ou sortie introuvable au
    /// moment de la vérification) : pas de rollback — l'audio n'est pas
    /// menacé, information seulement.</summary>
    AliveNoLog,

    /// <summary>La sortie ne répond plus au rendu WASAPI : le moteur audio
    /// n'a pas construit son graphe (APO refusée) — ROLLBACK immédiat,
    /// le son passe avant l'égaliseur.</summary>
    DeadEndpoint,
}

/// <summary>
/// Politique PURE du verdict d'activation (v3.9.7) : croise la sonde WASAPI
/// de la sortie et la trace de chargement laissée par l'APO (journal),
/// sans aucune dépendance Windows — testable sous Linux, elle verrouille la
/// décision de ROLLBACK AUTOMATIQUE (« le son passe avant l'égaliseur »).
/// </summary>
public static class ApoActivationPolicy
{
    /// <summary>Verdict croisé sonde × journal.</summary>
    public static ApoActivationHealth Evaluate(ApoProbeResult probe, bool logShowsLoad)
        => probe switch
        {
            ApoProbeResult.Alive => logShowsLoad
                ? ApoActivationHealth.Healthy
                : ApoActivationHealth.AliveNoLog,
            ApoProbeResult.DeviceAbsent => ApoActivationHealth.AliveNoLog, // débranché : jamais un motif de rollback
            _ => ApoActivationHealth.DeadEndpoint,
        };

    /// <summary>
    /// Faut-il RETENTER la sonde ? On ne conclut « morte » qu'après plusieurs
    /// échecs — et si un témoin (une autre sortie) répond, la mort est
    /// immédiatement avérée (sinon le service audio redémarre peut-être
    /// encore : on retente).
    /// </summary>
    public static bool ShouldRetryProbe(int failedAttempts, int maxAttempts, bool witnessAlive)
        => failedAttempts < maxAttempts && !witnessAlive;
}
