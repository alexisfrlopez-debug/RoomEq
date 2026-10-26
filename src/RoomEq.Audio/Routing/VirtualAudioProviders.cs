namespace RoomEq.Audio.Routing;

/// <summary>Type d'infrastructure de carte son virtuelle (spec I-AUD-03).</summary>
public enum VirtualCardKind
{
    /// <summary>
    /// Pilote de câble virtuel intégré : Windows joue dans le périphérique render
    /// « RoomEq Input », RoomEq lit le périphérique capture « RoomEq Output ».
    /// Le pilote noyau (roomeqvad.sys) est un bloc natif scellé (N-DRV-01).
    /// </summary>
    VirtualCable,

    /// <summary>Sans pilote : boucle WASAPI directe d'une sortie existante.</summary>
    Loopback,
}

/// <summary>
/// Paquet pilote RoomEq : les fichiers (INF/SYS/CAT) sont livrés par
/// l'installateur à côté de l'application (sous-répertoire « driver ») —
/// aucun téléchargement, aucune application tierce.
/// </summary>
public sealed record VirtualInstallerSpec
{
    /// <summary>Sous-répertoire du paquet pilote, relatif à l'application.</summary>
    public string DriverFolder { get; init; } = "driver";

    /// <summary>Nom du fichier INF du pilote RoomEq.</summary>
    public string InfFileName { get; init; } = "roomeqvad.inf";

    /// <summary>Identifiant PnP du périphérique racine créé par l'installation.</summary>
    public string HardwareId { get; init; } = "ROOT\\ROOMEQVAD";

    /// <summary>Nom du service pilote.</summary>
    public string ServiceName { get; init; } = "roomeqvad";

    /// <summary>Le paquet est-il présent sur le disque ?</summary>
    public bool PackagePresent { get; init; }
}

/// <summary>
/// Fournisseur d'infrastructure de carte son virtuelle : détection des
/// périphériques par motifs de noms, informations d'installation, guide
/// utilisateur.
/// </summary>
public interface IVirtualAudioProvider
{
    /// <summary>Identifiant stable (persisté dans mixer.json).</summary>
    string Id { get; }

    /// <summary>Nom affiché dans le sélecteur.</summary>
    string DisplayName { get; }

    /// <summary>Description d'une ligne (info-bulle du sélecteur).</summary>
    string Description { get; }

    VirtualCardKind Kind { get; }

    /// <summary>Vrai si aucune installation n'est nécessaire (boucle WASAPI native).</summary>
    bool IsPreinstalled { get; }

    VirtualInstallerSpec Installer { get; }

    /// <summary>Motifs de noms (insensibles à la casse) du périphérique render du câble — celui dans lequel Windows joue.</summary>
    string[] RenderNamePatterns { get; }

    /// <summary>Motifs du périphérique capture du câble — celui que RoomEq lit.</summary>
    string[] CaptureNamePatterns { get; }

    /// <summary>Guide pas-à-pas spécifique (affiché dans l'onglet Mixage).</summary>
    string[] GuideLines { get; }

    /// <summary>Ce nom de périphérique est-il le côté render du câble ?</summary>
    bool IsRenderEndpoint(string friendlyName);

    /// <summary>Ce nom de périphérique est-il le côté capture du câble ?</summary>
    bool IsCaptureEndpoint(string friendlyName);
}

/// <summary>Base partagée : correspondance de motifs insensible à la casse.</summary>
public abstract class VirtualAudioProviderBase : IVirtualAudioProvider
{
    public abstract string Id { get; }
    public abstract string DisplayName { get; }
    public abstract string Description { get; }
    public abstract VirtualCardKind Kind { get; }
    public virtual bool IsPreinstalled => false;
    public abstract VirtualInstallerSpec Installer { get; }
    public abstract string[] RenderNamePatterns { get; }
    public abstract string[] CaptureNamePatterns { get; }
    public abstract string[] GuideLines { get; }

    public bool IsRenderEndpoint(string friendlyName) => Matches(friendlyName, RenderNamePatterns);

    public bool IsCaptureEndpoint(string friendlyName) => Matches(friendlyName, CaptureNamePatterns);

    /// <summary>Correspondance de sous-chaîne insensible à la casse.</summary>
    public static bool Matches(string? friendlyName, IReadOnlyList<string> patterns)
    {
        if (string.IsNullOrEmpty(friendlyName)) return false;
        foreach (var p in patterns)
            if (friendlyName.Contains(p, StringComparison.OrdinalIgnoreCase))
                return true;
        return false;
    }
}

/// <summary>
/// Carte son virtuelle RoomEq — pilote intégré à l'application. Sur Windows :
/// pilote noyau roomeqvad (« RoomEq Input / RoomEq Output », pontés en interne).
/// Sur macOS : greffon système RoomEqHAL (périphérique « RoomEq Virtual »,
/// boucle interne publiée dans le pont intégré). Aucun logiciel tiers.
/// </summary>
public sealed class RoomEqVirtualProvider : VirtualAudioProviderBase
{
    private static readonly bool Mac = OperatingSystem.IsMacOS();

    public override string Id => "roomeq";

    public override string DisplayName => Mac
        ? "Périphérique virtuel RoomEq (greffon intégré)"
        : "Carte virtuelle RoomEq (pilote intégré)";

    public override string Description => Mac
        ? "Périphérique « RoomEq Virtual » créé par le greffon RoomEqHAL : macOS joue dedans, " +
          "l'application lit la boucle interne, applique l'égaliseur et rend le son sur la vraie sortie. Aucun logiciel tiers."
        : "Paire virtuelle « RoomEq Input / RoomEq Output » créée par le pilote RoomEq : " +
          "Windows joue dans RoomEq Input, l'application lit RoomEq Output. Aucun logiciel tiers.";

    public override VirtualCardKind Kind => VirtualCardKind.VirtualCable;

    public override VirtualInstallerSpec Installer { get; } = new()
    {
        DriverFolder = "driver",
        InfFileName = "roomeqvad.inf",
        HardwareId = Mac ? "" : "ROOT\\ROOMEQVAD",
        ServiceName = Mac ? "" : "roomeqvad",
        PackagePresent = true,
    };

    public override string[] RenderNamePatterns { get; } =
        Mac ? new[] { "RoomEq Virtual" }   // périphérique unique du greffon
            : new[] { "RoomEq Input" };    // render Windows de la paire pilote

    public override string[] CaptureNamePatterns { get; } =
        Mac ? new[] { "RoomEq Virtual" }   // boucle interne du greffon
            : new[] { "RoomEq Output" };   // capture Windows de la paire pilote

    public override string[] GuideLines => Mac
        ? new[]
        {
            "1 · Le greffon s'installe avec le paquet RoomEq (.pkg) : le périphérique « RoomEq Virtual » apparaît après un redémarrage de coreaudiod (automatique à la fin de l'installation).",
            "2 · Dans Réglages Système › Son › Sortie, choisissez « RoomEq Virtual » : tout le son de macOS y arrive (RoomEq ne peut pas changer la sortie par défaut à votre place — macOS interdit la bascule automatique).",
            "3 · Cliquez « Router le son vers l'EQ » : RoomEq capte la boucle du périphérique, applique l'égaliseur principal (onglet Égaliseur, synchronisé en temps réel) et rend le son sur la sortie physique choisie dans la tranche MASTER.",
            "4 · Pour un studio : les DAW macOS (Logic, Live, Cubase…) peuvent aussi sortir directement sur « RoomEq Virtual » — même chemin, même égaliseur.",
            "5 · Désinstallation : panneau Launchpad ou « RoomEq › Désinstaller le greffon » — les deux restaurent la sortie par défaut choisie avant routage.",
        }
        : new[]
        {
            "1 · Si la carte n'est pas encore installée : cliquez « Installer le pilote RoomEq » — l'installateur du pilote intégré s'exécute (invite administrateur), aucun téléchargement externe.",
            "2 · Dès que « RoomEq Input / RoomEq Output » apparaissent (ou « ⟳ » après installation), RoomEq peut basculer tout seul : RoomEq Input devient la sortie par défaut de Windows et une piste « RoomEq Output » est ajoutée à la console.",
            "3 · Cliquez « Router Windows vers l'EQ » : RoomEq capte RoomEq Output, applique l'égaliseur principal (onglet Égaliseur, synchronisé en temps réel) et rend le son sur la sortie physique choisie dans la tranche MASTER.",
            "4 · Pendant le routage, les périphériques par défaut d'origine de Windows (sortie et, si l'option est cochée, entrée) sont mémorisés — ils sont restaurés automatiquement quand vous arrêtez le routage.",
            "5 · Pour un studio : votre DAW peut aussi sortir directement sur « RoomEq ASIO » (pilote ASIO intégré) — ajoutez la piste « DAW (ASIO) », elle traverse le même égaliseur.",
        };
}

/// <summary>
/// Boucle WASAPI native : aucun pilote, RoomEq capte directement ce que Windows
/// joue sur une sortie existante. Solution de secours sans installation.
/// </summary>
public sealed class LoopbackProvider : VirtualAudioProviderBase
{
    public override string Id => "loopback";
    public override string DisplayName => "Boucle WASAPI (sans pilote)";
    public override string Description =>
        "Aucune installation : RoomEq capte en boucle une sortie Windows existante (la sortie captée doit différer de la sortie master).";

    public override VirtualCardKind Kind => VirtualCardKind.Loopback;
    public override bool IsPreinstalled => true;

    public override VirtualInstallerSpec Installer { get; } = new()
    {
        DriverFolder = "",
        InfFileName = "",
        HardwareId = "",
        ServiceName = "",
    };

    public override string[] RenderNamePatterns { get; } = Array.Empty<string>();
    public override string[] CaptureNamePatterns { get; } = Array.Empty<string>();

    public override string[] GuideLines { get; } =
    {
        "1 · Aucune installation : la boucle WASAPI est intégrée à Windows et à RoomEq.",
        "2 · Ajoutez la piste « <votre sortie> (boucle) » depuis « + Ajouter une piste » : elle capte tout ce que Windows joue sur cette sortie.",
        "3 · Choisissez une sortie master DIFFÉRENTE de la sortie captée (casque ≠ enceintes) : RoomEq refuse toute boucle sur elle-même (protection anti-larsen).",
        "4 · Avantage : latence minimale, rien à installer. Limite : seule la sortie captée passe par l'EQ (pas le son d'une autre sortie).",
        "5 · Pour router TOUT le système via une carte virtuelle dédiée, revenez à « Carte virtuelle RoomEq » (pilote intégré).",
    };
}

/// <summary>
/// Registre des fournisseurs de cartes son virtuelles pris en charge, avec
/// résolution par identifiant persisté (les ids héritées de configurations
/// anciennes retombent sur le fournisseur par défaut).
/// </summary>
public static class VirtualAudioCatalog
{
    /// <summary>Fournisseurs pris en charge, dans l'ordre d'affichage.</summary>
    public static IReadOnlyList<IVirtualAudioProvider> All { get; } = new IVirtualAudioProvider[]
    {
        new RoomEqVirtualProvider(),
        new LoopbackProvider(),
    };

    /// <summary>Fournisseur par défaut (carte virtuelle RoomEq).</summary>
    public static IVirtualAudioProvider Default { get; } = All[0];

    /// <summary>Résout un fournisseur par identifiant ; retombe sur la carte RoomEq si inconnu.</summary>
    public static IVirtualAudioProvider GetById(string? id)
    {
        if (!string.IsNullOrWhiteSpace(id))
        {
            foreach (var p in All)
                if (string.Equals(p.Id, id, StringComparison.OrdinalIgnoreCase))
                    return p;
        }
        return Default;
    }

    /// <summary>Un des fournisseurs de câble virtuel reconnaît-il ce render ?</summary>
    public static bool IsVirtualRender(string friendlyName)
        => All.Any(p => p.Kind == VirtualCardKind.VirtualCable && p.IsRenderEndpoint(friendlyName));

    /// <summary>Un des fournisseurs de câble virtuel reconnaît-il cette capture ?</summary>
    public static bool IsVirtualCapture(string friendlyName)
        => All.Any(p => p.Kind == VirtualCardKind.VirtualCable && p.IsCaptureEndpoint(friendlyName));
}
