namespace RoomEq.Audio.Routing;

/// <summary>
/// Politique PURE de synchronisation du volume entre les multiples commandes qui
/// pilotent la MÊME carte physique (spec I-AUD-03) : curseur « Volume » de la
/// barre supérieure, curseur « Volume carte physique » de l'onglet Mixage,
/// raccourcis clavier — et les changements faits HORS de l'application
/// (mixeur Windows, touches du casque…).
///
/// Règles : une seule source de vérité (le volume Windows 0..1) ; toute
/// commande y écrit PUIS tous les affichages relisent la carte ; la sentinelle
/// −1 (« pas de carte accessible ») n'est JAMAIS écrite ni affichée.
/// </summary>
public static class VolumeSyncPolicy
{
    /// <summary>
    /// Écart minimal entre deux volumes Windows au-delà duquel on considère un
    /// VRAI changement (et non le bruit d'arrondi du float du pilote).
    /// </summary>
    public const double ChangeEpsilon = 0.0005;

    /// <summary>
    /// Écart minimal appliqué pendant un glissement CONTINU de curseur avant de
    /// réécrire la carte : évite de bombarder l'API Windows à chaque pixel.
    /// </summary>
    public const double WriteEpsilon = 0.0035;

    /// <summary>
    /// Un scalaire lu de la carte est-il utilisable comme volume affichable ?
    /// Faux pour la sentinelle −1 (« indisponible »), NaN et hors plage.
    /// </summary>
    public static bool IsUsable(double volumeScalar)
        => !double.IsNaN(volumeScalar) && volumeScalar >= 0.0 && volumeScalar <= 1.0;

    /// <summary>
    /// La carte a-t-elle changé depuis la dernière valeur vue ? Faux tant
    /// qu'aucune valeur valide n'a été vue (sentinelle −1 au démarrage) : le
    /// premier sondage ne doit pas écraser les affichages avant leur population.
    /// </summary>
    public static bool HasChangedSinceLastSeen(double lastSeen, double current)
        => IsUsable(lastSeen) && IsUsable(current) && Math.Abs(current - lastSeen) > ChangeEpsilon;

    /// <summary>
    /// Faut-il écrire la consigne dans la carte ? Oui si l'état courant est
    /// illisible (sentinelle −1 : on tente quand même), ou si l'écart dépasse
    /// <see cref="WriteEpsilon"/> (pas d'écriture pour un diff d'arrondi).
    /// </summary>
    public static bool ShouldWriteToEndpoint(double currentScalar, double target)
    {
        if (double.IsNaN(target) || target < 0.0) return false;
        if (currentScalar < 0.0) return true;           // illisible : on tente l'écriture
        return Math.Abs(currentScalar - target) > WriteEpsilon;
    }

    /// <summary>
    /// Le curseur de la barre supérieure doit-il être recalé sur la valeur de la
    /// carte ? Inutile si déjà à la même position (évite une boucle de
    /// notifications PropertyChanged entre les deux curseurs).
    /// </summary>
    public static bool ShouldSyncTopBar(double topBarVolume, double endpointScalar)
        => IsUsable(endpointScalar) && Math.Abs(topBarVolume - endpointScalar) > ChangeEpsilon;

    /// <summary>
    /// Consigne d'un curseur bornée à la plage Windows : les entrées hors plage
    /// (glissement, valeur initiale −1, boosts logiciels) reviennent dans 0..1.
    /// </summary>
    public static double ClampToEndpointRange(double value)
        => double.IsNaN(value) ? 0.0 : Math.Clamp(value, 0.0, 1.0);
}
