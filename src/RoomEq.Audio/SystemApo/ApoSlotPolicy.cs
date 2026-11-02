namespace RoomEq.Audio.SystemApo;

/// <summary>
/// Position FxProperties dans laquelle l'APO RoomEq s'inscrit sur une sortie
/// (v3.9.8).
/// </summary>
public enum ApoFxSlot
{
    /// <summary>
    /// EFX — PKEY_FX_EndpointEffectClsid : position par défaut (après mixage
    /// et conversion, avant limiteur ; appliquée même aux flux « raw »).
    /// </summary>
    Efx,

    /// <summary>
    /// MFX — PKEY_FX_ModeEffectClsid : position de repli des périphériques
    /// COMBINÉS (Bluetooth sous Windows 11), où la position EFX ne
    /// fonctionne pas (constat Equalizer APO, cf. <see cref="ApoSlotPolicy"/>).
    /// </summary>
    Mfx,
}

/// <summary>
/// Politique PURE du choix de position FxProperties (v3.9.8 — correctif
/// « EQ activé = sortie muette » sur casque Bluetooth). Source terrain :
/// Equalizer APO, DeviceAPOInfo.cpp (10+ ans de recul) —
/// « bluetooth devices may be combined in Windows 11, EFX will not work
/// then » : quand l'endpoint porte la propriété « périphérique combiné »
/// (valeur <c>{b3f8fa53-…},41</c> dans sa sous-clé Properties), la référence
/// n'installe PAS en EFX mais en SFX/MFX. RoomEq fait de même (en une seule
/// position : MFX — notre APO ne chaîne pas d'enfants, une position suffit).
/// Aucune dépendance Windows : testable sous Linux.
/// </summary>
public static class ApoSlotPolicy
{
    /// <summary>
    /// Choisis la position d'inscription pour un endpoint.
    /// </summary>
    /// <param name="combinedDevice">Vrai si l'endpoint porte la propriété
    /// « périphérique combiné » (Bluetooth Windows 11 notamment).</param>
    public static ApoFxSlot Choose(bool combinedDevice)
        => combinedDevice ? ApoFxSlot.Mfx : ApoFxSlot.Efx;

    /// <summary>Nom de la valeur CLSID du slot (« ,7 » EFX ou « ,6 » MFX).</summary>
    public static string ClsidValueName(ApoFxSlot slot)
        => slot == ApoFxSlot.Mfx ? ApoConstants.FxModeEffectClsid
                                 : ApoConstants.FxEndpointEffectClsid;

    /// <summary>Nom de la valeur des modes de traitement du slot
    /// (PKEY_?FX_ProcessingModes_Supported_For_Streaming).</summary>
    public static string ModesValueName(ApoFxSlot slot)
        => slot == ApoFxSlot.Mfx ? ApoConstants.FxMfxProcessingModes
                                 : ApoConstants.FxEfxProcessingModes;

    /// <summary>
    /// Nom persisté dans la sauvegarde (null = EFX — compatibilité avec les
    /// sauvegardes v3.7–v3.9.7 qui n'ont pas le champ).
    /// </summary>
    public static string? SlotName(ApoFxSlot slot)
        => slot == ApoFxSlot.Mfx ? "MFX" : "EFX";

    /// <summary>
    /// Lit le nom persisté (null, « EFX » ou toute valeur inconnue = EFX —
    /// jamais d'exception, jamais d'échec de restauration).
    /// </summary>
    public static ApoFxSlot ParseSlot(string? persisted)
        => string.Equals(persisted, "MFX", StringComparison.OrdinalIgnoreCase)
            ? ApoFxSlot.Mfx
            : ApoFxSlot.Efx;

    /// <summary>
    /// Une valeur CLSID d'origine est-elle « à nous » ? Une activation
    /// précédente qui a perdu sa sauvegarde a pu laisser notre CLSID dans un
    /// slot : le restituer comme « origine » à la désinstallation serait faux
    /// (comportement Equalizer APO : ses propres GUID comptent comme absence).
    /// </summary>
    public static string? NotOurs(string? clsid)
        => string.Equals(clsid, ApoConstants.Clsid, StringComparison.OrdinalIgnoreCase)
            ? null
            : clsid;
}
