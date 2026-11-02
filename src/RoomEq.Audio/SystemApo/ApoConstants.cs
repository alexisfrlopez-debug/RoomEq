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

    /// <summary>
    /// PKEY_FX_…ProcessingModes (v3.9.6) : modes de traitement couverts par
    /// l'effet de position EFX — Equalizer APO écrit le MULTI_SZ
    /// « mode par défaut » à cette valeur quand elle est absente (recette
    /// éprouvée DeviceAPOInfo::install, INSTALL_SFX_EFX). Sans elle, l'EFX
    /// peut n'être rattaché à AUCUN mode et donc jamais instancié.
    /// FMTID {d3993a3f-99c2-4402-b5ec-a92a0367664b}, PID 7 (EFX).
    /// </summary>
    public const string FxEfxProcessingModes = "{d3993a3f-99c2-4402-b5ec-a92a0367664b},7";

    /// <summary>
    /// Mode de traitement « par défaut » (audio DEFAULT mode) — le GUID que
    /// Equalizer APO inscrit dans les valeurs ProcessingModes.
    /// </summary>
    public const string DefaultProcessingMode = "{C18E2F7E-933D-4965-B7D1-1EEF228D2AF3}";

    /// <summary>Racine registre des endpoints de rendu (par GUID d'endpoint).</summary>
    public const string RenderEndpointsKey =
        @"SOFTWARE\Microsoft\Windows\CurrentVersion\MMDevices\Audio\Render";

    /// <summary>Sous-clé des propriétés d'effets d'un endpoint.</summary>
    public const string FxPropertiesSubkey = "FxProperties";

    /// <summary>Inscription COM de la DLL (HKLM, machine entière).</summary>
    public static string ComClsidKey => @"SOFTWARE\Classes\CLSID\" + Clsid;

    /// <summary>
    /// Inscription de l'APO auprès du MOTEUR AUDIO (v3.9.6) — ce que fait la
    /// fonction RegisterAPO du DDK (audioenginebaseapo.h) et que l'installateur
    /// Equalizer APO exécute via regsvr32 : la clé
    /// HKLM\SOFTWARE\Classes\AudioEngine\AudioProcessingObjects\{CLSID}
    /// expose les propriétés d'enregistrement (nom, copyright, versions,
    /// drapeaux, connexions, interfaces) sous forme de VALEURS NOMMÉES (cf.
    /// ComponentizedApoSample.inx du sample SysVAD de Microsoft).
    /// </summary>
    public static string EngineApoRegistrationKey =>
        @"SOFTWARE\Classes\AudioEngine\AudioProcessingObjects\" + Clsid;

    /// <summary>
    /// Politique du moteur audio (v3.9.6) : sous cette clé, la DWORD
    /// <c>DisableProtectedAudioDG</c> à 1 désactive la VÉRIFICATION DE
    /// SIGNATURE des APO — sinon audiodg (mode audio protégé) refuse de
    /// charger toute DLL non signée, SILENCIEUSEMENT. C'est le réglage
    /// utilisé depuis 10+ ans par Equalizer APO (doc développeur officielle,
    /// DeviceAPOInfo::checkProtectedAudioDG). Contrepartie honnête : les
    /// rares applications exigeant un chemin audio protégé (DRM) peuvent
    /// refuser de jouer — réversible à la désinstallation (valeur d'origine
    /// sauvegardée et restituée).
    /// </summary>
    public const string AudioPolicyKey =
        @"SOFTWARE\Microsoft\Windows\CurrentVersion\Audio";

    /// <summary>Nom de la valeur de politique audio (DWORD, 1 = signature APO non vérifiée).</summary>
    public const string DisableProtectedAudioDgValue = "DisableProtectedAudioDG";

    /// <summary>
    /// Sauvegarde de la valeur d'origine de <c>DisableProtectedAudioDG</c>
    /// (JSON, %ProgramData%\RoomEq\apo\audio-policy.json) : null = la
    /// valeur n'existait pas à la première activation (la désinstallation la
    /// supprimera) ; un entier = la restituer tel quel. Absent = nous n'avons
    /// jamais modifié la politique (un tiers ou personne) → ne rien toucher.
    /// </summary>
    public static string AudioPolicyBackupPath => Path.Combine(ProgramDataDir, "audio-policy.json");

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

    /// <summary>
    /// Nom de la DLL APO déployée pour l'architecture de CETTE machine
    /// (x64 par défaut, ARM64 si le processeur est ARM) — même règle pour le
    /// déploiement (processus élevé) et la vérification COM du panneau.
    /// </summary>
    public static string DeployedDllName
    {
        get
        {
            bool arm = Environment.Is64BitOperatingSystem
                && Environment.GetEnvironmentVariable("PROCESSOR_ARCHITECTURE")
                    ?.StartsWith("ARM", StringComparison.OrdinalIgnoreCase) == true;
            return arm ? "RoomEqApo_arm64.dll" : "RoomEqApo_x64.dll";
        }
    }

    /// <summary>Extrait la clé registre d'un endpoint depuis son ID MMDevice.</summary>
    /// <example>"{0.0.0.00000000}.{GUID}" → "{GUID}"</example>
    public static string EndpointKeyFromMmdeviceId(string mmdeviceId)
    {
        int dot = mmdeviceId.LastIndexOf('.');
        return dot >= 0 && dot + 1 < mmdeviceId.Length ? mmdeviceId[(dot + 1)..] : mmdeviceId;
    }
}
