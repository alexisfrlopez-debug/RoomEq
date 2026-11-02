using System.Text.Json;

namespace RoomEq.Audio.SystemApo;

/// <summary>Une valeur d'inscription de l'APO auprès du moteur audio
/// (REG_SZ ou REG_DWORD — cf. ComponentizedApoSample.inx, sample SysVAD).</summary>
/// <param name="Name">Nom de la valeur (ex. « FriendlyName », « Flags »…).</param>
/// <param name="Dword">Valeur DWORD (null = valeur chaîne).</param>
/// <param name="Text">Valeur chaîne (null = valeur DWORD).</param>
public sealed record ApoEngineRegValue(string Name, int? Dword, string? Text)
{
    public static ApoEngineRegValue Sz(string name, string text) => new(name, null, text);
    public static ApoEngineRegValue Dw(string name, int value) => new(name, value, null);
}

/// <summary>
/// Logique PURE de l'inscription de l'APO auprès du moteur audio et de la
/// politique « audio protégé » (v3.9.6 — correctif « la DLL n'a jamais été
/// chargée par le moteur audio »). Deux volets :
///
/// (1) VALEURS D'INSCRIPTION : la clé
///     HKLM\SOFTWARE\Classes\AudioEngine\AudioProcessingObjects\{CLSID} doit
///     exister avec des valeurs nommées — c'est ce que fait la fonction
///     RegisterAPO du DDK (audioenginebaseapo.h), exécutée par
///     l'installateur Equalizer APO via regsvr32 (DllRegisterServer) et
///     déclarée OBLIGATOIRE par sa documentation développeur. Les noms des
///     valeurs viennent du sample officiel Microsoft SysVAD
///     (ComponentizedApoSample.inx) ; leur CONTENU reflète exactement ce que
///     la DLL native déclare dans GetRegistrationProperties
///     (natif/roomeq-apo/apo/RoomEqApo.cpp) — le test
///     <c>ApoEnginePolicyTests</c> verrouille chaque champ.
///
/// (2) POLITIQUE « AUDIO PROTÉGÉ » : sans la DWORD DisableProtectedAudioDG=1
///     sous HKLM\SOFTWARE\Microsoft\Windows\CurrentVersion\Audio, audiodg
///     (moteur audio Windows) tourne en mode protégé et REFUSE DE CHARGER
///     TOUTE APO NON SIGNÉE — silencieusement : pas de journal, pas de
///     message, la DLL n'est même jamais ouverte. C'est le réglage qu'utilise
///     Equalizer APO depuis 10+ ans (DeviceAPOInfo::checkProtectedAudioDG),
///     avec sa contrepartie honnête : les applications exigeant un chemin
///     audio protégé (certaines DRM) peuvent refuser de jouer. La valeur
///     d'ORIGINE est sauvegardée au premier changement (JSON) et restituée à
///     la désinstallation — invariant 2 appliqué à la machine entière.
///
/// Testable sous Linux (aucune dépendance Windows) — les écritures réelles
/// vivent dans <see cref="WindowsApoEndpointStore"/> (processus élevé).
/// </summary>
public static class ApoEnginePolicy
{
    /// <summary>
    /// IID de IAudioProcessingObject déclarée par la DLL native (apo_abi.h :
    /// 0xFD7F2B29-24D0-4B5C-B177-592C39F9CA10) — v3.9.7 : les DEUX références
    /// terrain (sample SysVAD ComponentizedApoSample.inx et Equalizer APO,
    /// CRegAPOProperties avec __uuidof(IAudioProcessingObject)) déclarent
    /// l'interface NON temps réel ici ; la valeur registre et
    /// GetRegistrationProperties restent miroir exact.
    /// </summary>
    public const string ApoInterface0 = "{FD7F2B29-24D0-4B5C-B177-592C39F9CA10}";

    /// <summary>APO_FLAG_DEFAULT | APO_FLAG_INPLACE = 0xE | 0x1 = 15
    /// (enum APO_FLAG du SDK — identique au natif).</summary>
    public const int RegFlags = 15;

    /// <summary>
    /// Les 13 valeurs d'inscription de l'APO RoomEq auprès du moteur audio —
    /// miroir EXACT de RoomEqApo::GetRegistrationProperties (toute divergence
    /// entre le registre et l'objet instancié serait une incohérence).
    /// </summary>
    public static IReadOnlyList<ApoEngineRegValue> RegistrationValues() => new[]
    {
        ApoEngineRegValue.Sz("FriendlyName", ApoConstants.ComFriendlyName),
        ApoEngineRegValue.Sz("Copyright", "RoomEq"),
        ApoEngineRegValue.Dw("MajorVersion", 1),
        ApoEngineRegValue.Dw("MinorVersion", 0),
        ApoEngineRegValue.Dw("Flags", RegFlags),
        ApoEngineRegValue.Dw("MinInputConnections", 1),
        ApoEngineRegValue.Dw("MaxInputConnections", 1),
        ApoEngineRegValue.Dw("MinOutputConnections", 1),
        ApoEngineRegValue.Dw("MaxOutputConnections", 1),
        ApoEngineRegValue.Dw("MaxInstances", 64),
        ApoEngineRegValue.Dw("NumAPOInterfaces", 1),
        ApoEngineRegValue.Sz("APOInterface0", ApoInterface0),
    };

    /// <summary>La politique actuelle autorise-t-elle le chargement de notre
    /// DLL non signée ? (valeur absente ou ≠ 1 = NON — le moteur protégé
    /// refusera la DLL sans le moindre message).</summary>
    public static bool IsDllLoadAllowed(int? disableProtectedAudioDg)
        => disableProtectedAudioDg == 1;

    // ------------------------------------------------ sauvegarde de la politique

    /// <summary>Sauvegarde JSON de la valeur d'origine de la politique.</summary>
    /// <param name="Original">Valeur d'origine : null = la valeur n'existait
    /// pas (la désinstallation la supprimera) ; entier = valeur à restituer.</param>
    public sealed record Backup(int? Original);

    /// <summary>Charge la sauvegarde (null si absente ou illisible : nous
    /// n'avons jamais modifié la politique → ne rien restituer).</summary>
    public static Backup? LoadBackup(string path)
    {
        try
        {
            if (!File.Exists(path))
                return null;
            return JsonSerializer.Deserialize<Backup>(File.ReadAllText(path));
        }
        catch
        {
            return null; // illisible : agir comme absente, ne jamais lever
        }
    }

    /// <summary>Écrit la sauvegarde (atomique : temp + renommage).</summary>
    public static void SaveBackup(string path, int? original)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        var tmp = path + ".tmp";
        File.WriteAllText(tmp, JsonSerializer.Serialize(new Backup(original)));
        File.Move(tmp, path, overwrite: true);
    }

    /// <summary>Supprime la sauvegarde (après restitution).</summary>
    public static void DeleteBackup(string path)
    {
        try
        {
            File.Delete(path);
        }
        catch
        {
            // un fichier restant est sans effet (la valeur est déjà restituée)
        }
    }
}
