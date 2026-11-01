namespace RoomEq.Audio.SystemApo;

/// <summary>
/// Constantes du module « EQ système par APO » (spec I-AUD-09) : CLSID de
/// l'objet COM, noms de valeurs registre FxProperties, emplacements de
/// fichiers. TOUT ce qui est inscrit sur la machine vit ici, en un seul
/// endroit, pour l'audit et la désinstallation propre.
/// </summary>
public static class ApoConstants
{
    /// <summary>
    /// CLSID de l'APO RoomEq — identique à CLSID_RoomEqApo du cœur natif
    /// (natif/roomeq-apo/apo/apo_abi.h). Généune fois pour toutes : le
    /// changer signifie que les activations existantes deviennent orphelines.
    /// </summary>
    public const string Clsid = "{EAA24610-59C6-4591-A23E-9D0A2C4E1A15}";

    /// <summary>Nom affiché de l'objet COM (inscription CLSID).</summary>
    public const string ComFriendlyName = "RoomEq - egaliseur systeme";

    /// <summary>
    /// Valeur EFX (endpoint effect) de FxProperties : CLSID de l'APO à charger
    /// pour TOUT ce qui sort par ce périphérique (appliquée même aux flux
    /// « raw » — Microsoft Learn, Audio Processing Object Architecture).
    /// FMTID {d04e05a6-594b-4fb6-a80d-01af5eed7d1d}, PID 7.
    /// </summary>
    public const string FxEndpointEffectClsid = "{d04e05a6-594b-4fb6-a80d-01af5eed7d1d},7";

    /// <summary>
    /// PKEY_AudioEndpoint_Disable_SysFx : 1 = « désactiver tous les effets
    /// audio » (case à cocher Windows). Si présente à 1, notre APO ne serait
    /// jamais chargé : l'activation la sauvegarde puis la met à 0, la
    /// désactivation restaure l'origine.
    /// </summary>
    public const string FxDisableSysFx = "{1da5d803-d492-4edd-8c23-e0c0ffee7f0e},5";

    /// <summary>Racine registre des endpoints de rendu (par GUID d'endpoint).</summary>
    public const string RenderEndpointsKey =
        @"SOFTWARE\Microsoft\Windows\CurrentVersion\MMDevices\Audio\Render";

    /// <summary>Sous-clé des propriétés d'effets d'un endpoint.</summary>
    public const string FxPropertiesSubkey = "FxProperties";

    /// <summary>Inscription COM de la DLL (HKLM, machine entière).</summary>
    public static string ComClsidKey => @"SOFTWARE\Classes\CLSID\" + Clsid;

    /// <summary>
    /// Sous-répertoire de données partagées (audiodg tourne en LocalService :
    /// PAS d'accès aux profils utilisateurs). La courbe active, le journal de
    /// l'APO et les sauvegardes y vivent ; les DLL déployées dans « bin ».
    /// </summary>
    public static string ProgramDataDir { get; } = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData),
        "RoomEq", "apo");

    /// <summary>DLL déployées (lecture pour tous, modification aux admins).</summary>
    public static string DeployedBinDir => Path.Combine(ProgramDataDir, "bin");

    /// <summary>Courbe active partagée avec l'APO.</summary>
    public static string ActiveCurvePath => Path.Combine(ProgramDataDir, "active.json");

    /// <summary>Sauvegarde des valeurs FxProperties d'origine (JSON).</summary>
    public static string BackupPath => Path.Combine(ProgramDataDir, "backups.json");

    /// <summary>Journal de l'APO (écrit par audiodg — diagnostic « chargé »).</summary>
    public static string ApoLogPath => Path.Combine(ProgramDataDir, "apo.log");

    /// <summary>Résultat d'une commande élevée, lu par l'application.</summary>
    public static string SetupResultPath => Path.Combine(ProgramDataDir, "setup-result.json");

    /// <summary>
    /// Sous-répertoire de l'application contenant les DLL APO livrées par
    /// l'installateur (source du déploiement vers ProgramData).
    /// </summary>
    public const string AppApoSubdir = "apo";

    /// <summary>Extrait la clé registre d'un endpoint depuis son ID MMDevice.</summary>
    /// <example>"{0.0.0.00000000}.{GUID}" → "{GUID}"</example>
    public static string EndpointKeyFromMmdeviceId(string mmdeviceId)
    {
        int dot = mmdeviceId.LastIndexOf('.');
        return dot >= 0 && dot + 1 < mmdeviceId.Length ? mmdeviceId[(dot + 1)..] : mmdeviceId;
    }
}
