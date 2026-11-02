namespace RoomEq.App.ViewModels;

/// <summary>
/// Politique PURE de suivi de la sortie par défaut de Windows (v3.9.7 —
/// correctif « la modification du volume Windows n'a aucun effet sur la
/// barre de volume de l'appli, qui elle ne modifie pas le volume entendu »).
///
/// FAIT ÉTABLI (retour terrain) : l'écoute RoomEq (volume physique, preset
/// de la sortie) restait branchée sur la sortie mémorisée ou la première
/// énumérée — les haut-parleurs Realtek — pendant que l'utilisateur écoutait
/// le casque JBL devenu sortie PAR DÉFAUT : les deux curseurs pilotaient une
/// carte muette, et le volume Windows du casque ne touchait pas l'appli.
///
/// Règle : la sortie d'écoute EST la sortie par défaut de Windows. Si
/// l'utilisateur choisit DÉLIBÉRÉMENT une autre sortie dans l'application,
/// son choix prime (divergence) — jusqu'à ce qu'il revienne à la sortie par
/// défaut, qui redevient suivie (changements suivis à nouveau).
/// </summary>
public static class OutputFollowPolicy
{
    /// <summary>
    /// La sélection actuelle diverge-t-elle du défaut Windows ? (divergence =
    /// choix délibéré de l'utilisateur : ne plus suivre les changements du
    /// défaut tant qu'il n'est pas re-sélectionné). Un défaut inconnu (null)
    /// ne fait jamais diverger.
    /// </summary>
    public static bool Diverges(string? selectedOutputId, string? defaultOutputId)
        => defaultOutputId != null
           && selectedOutputId != null
           && !string.Equals(selectedOutputId, defaultOutputId, StringComparison.Ordinal);

    /// <summary>
    /// Faut-il RE-CIBLER la sortie d'écoute sur le défaut Windows qui vient
    /// de changer ? Oui ssi l'utilisateur n'a pas divergé, que le défaut est
    /// connu, et qu'il diffère de la sélection courante.
    /// </summary>
    public static bool ShouldFollow(string? selectedOutputId, string? defaultOutputId, bool diverged)
        => !diverged
           && defaultOutputId != null
           && !string.Equals(selectedOutputId, defaultOutputId, StringComparison.Ordinal);
}
