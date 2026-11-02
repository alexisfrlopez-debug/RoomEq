namespace RoomEq.Audio.SystemApo;

/// <summary>
/// Décodage LISIBLE des codes d'erreur Windows/HRESULT (v3.9.8 — correctif
/// « EQ activé = sortie muette ») : le verdict d'activation et l'auto-test de
/// la DLL nomment désormais l'échec EXACT (accès refusé, module introuvable,
/// mauvaise architecture, classe non inscrite…) au lieu d'un générique
/// « n'a pas réussi à charger ». LOGIQUE PURE — testable sous Linux.
/// </summary>
public static class ApoHresult
{
    /// <summary>
    /// Hexadécimal signé 8 chiffres, préfixe 0x (« 0x80070005 ») — forme
    /// stable à transmettre telle quelle dans un verdict.
    /// </summary>
    public static string Hex(int hr)
        => "0x" + unchecked((uint)hr).ToString("X8");

    /// <summary>Texte français d'un HRESULT (ou d'un code GetLastError).</summary>
    public static string Describe(int hr)
    {
        return hr switch
        {
            0 => "succès",
            unchecked((int)0x80004002) => "interface non supportée (E_NOINTERFACE — l'objet ne répond pas à une interface exigée par l'appelant)",
            unchecked((int)0x80004003) => "pointeur invalide (E_POINTER)",
            unchecked((int)0x80004005) => "échec non spécifié (E_FAIL)",
            unchecked((int)0x80070005) => "accès refusé (Win32 5 — ACL ou privilège manquant)",
            unchecked((int)0x80070006) => "descripteur invalide (E_HANDLE)",
            unchecked((int)0x8007000E) => "mémoire insuffisante (E_OUTOFMEMORY)",
            unchecked((int)0x80070057) => "argument invalide (E_INVALIDARG)",
            unchecked((int)0x8007007E) => "module introuvable (Win32 126 — DLL ou DÉPENDANCE absente du chemin)",
            unchecked((int)0x8007007F) => "procédure introuvable (Win32 127 — point d'entrée absent de la DLL)",
            unchecked((int)0x800700C1) => "mauvais format binaire (Win32 193 — architecture DLL ≠ architecture du processus)",
            unchecked((int)0x800704B0) => "plus de fichiers (erreur E/S inattendue)",
            unchecked((int)0x800704EC) => "programme bloqué par la stratégie (SmartScreen/AppLocker)",
            unchecked((int)0x800736B1) => "configuration side-by-side incorrecte (VC++ runtime manquant)",
            unchecked((int)0x8007045A) => "échec de l'initialisation de la DLL (DllMain a refusé le chargement)",
            unchecked((int)0x80040154) => "classe non inscrite (REGDB_E_CLASSNOTREG — inscription COM absente ou invisible du moteur)",
            unchecked((int)0x80040110) => "cette classe ne gère pas l'agrégation (CLASS_E_NOAGGREGATION)",
            unchecked((int)0x800401F3) => "chaîne de classe invalide (CLSID illisible)",
            unchecked((int)0x88890008) => "AUDCLNT_E_DEVICE_INVALIDATED — le périphérique audio est invalidé (graphe non construit)",
            unchecked((int)0x8889000A) => "AUDCLNT_E_DEVICE_IN_USE — le périphérique est utilisé en mode exclusif",
            unchecked((int)0x88890018) => "AUDCLNT_E_UNSUPPORTED_FORMAT — format non géré par le périphérique",
            unchecked((int)0x88890004) => "AUDCLNT_E_ENDPOINT_CREATE_FAILED — le point de terminaison n'a pas pu être créé (graphe non construit)",
            _ => "code inconnu",
        };
    }

    /// <summary>Forme complète : « 0x80070005 — accès refusé (…) ».</summary>
    public static string DescribeFull(int hr)
        => Hex(hr) + " — " + Describe(hr);
}
