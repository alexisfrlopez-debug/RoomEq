using System.Linq;

namespace RoomEq.Audio.Routing;

/// <summary>Type d'infrastructure de carte son virtuelle.</summary>
public enum VirtualCardKind
{
    /// <summary>Pilote de câble virtuel : Windows joue dans un périphérique render,
    /// RoomEq lit le périphérique capture associé (VB-CABLE, VoiceMeeter, VAC…).</summary>
    VirtualCable,
    /// <summary>Sans pilote : boucle WASAPI directe d'une sortie existante.</summary>
    Loopback
}

/// <summary>
/// Spécification d'installation d'un fournisseur de carte virtuelle : archive à
/// télécharger (zip contenant un installateur, ou installateur .exe direct) depuis
/// des domaines officiels verrouillés, exécutable à lancer en élévation.
/// </summary>
public sealed record VirtualInstallerSpec
{
    /// <summary>Format du téléchargement : « zip » (à extraire) ou « exe » (direct).</summary>
    public string Container { get; init; } = "zip";
    /// <summary>URL candidates dans l'ordre de préférence (HTTPS, domaines officiels uniquement).</summary>
    public string[] DownloadUrls { get; init; } = Array.Empty<string>();
    /// <summary>Nom de l'exécutable d'installation à rechercher dans le conteneur.</summary>
    public string[] SetupExecutableNames { get; init; } = Array.Empty<string>();
    /// <summary>Page produit officielle (téléchargement manuel, achat, documentation).</summary>
    public string ProductPageUrl { get; init; } = "";
    /// <summary>
    /// Page officielle à analyser pour découvrir l'URL de la version courante
    /// (les éditeurs renomment leurs archives à chaque version). Vide = pas de
    /// découverte, les <see cref="DownloadUrls"/> statiques suffisent.
    /// </summary>
    public string DiscoveryPageUrl { get; init; } = "";
    /// <summary>Motif regex des liens de téléchargement dans la page de découverte.</summary>
    public string DiscoveryPattern { get; init; } = "";
    /// <summary>Conseil affiché juste après le lancement de l'installateur (clic attendu…).</summary>
    public string PostLaunchHint { get; init; } = "";
    /// <summary>Note de licence affichée avant installation.</summary>
    public string LicenseNote { get; init; } = "";
    /// <summary>
    /// Taille minimale plausible du téléchargement (anti-corruption). Doit rester
    /// INFÉRIEURE à la taille réelle de l'archive officielle : un seuil trop élevé
    /// rejetait l'archive VB-CABLE (1,3 Mo) et bloquait l'installation (bug v1.2).
    /// </summary>
    public long MinBytes { get; init; } = 1024 * 1024;

    /// <summary>Une installation automatique est-elle prise en charge ?</summary>
    public bool Supported => DownloadUrls.Length > 0 && SetupExecutableNames.Length > 0;
}

/// <summary>
/// Fournisseur d'infrastructure de carte son virtuelle : détection des périphériques
/// par motifs de noms, informations d'installation et guide utilisateur.
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
    /// <summary>Motifs de noms (insensibles à la casse) du périphérique render du câble
    /// — celui dans lequel Windows joue.</summary>
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

    public bool IsRenderEndpoint(string friendlyName) =>
        Matches(friendlyName, RenderNamePatterns);

    public bool IsCaptureEndpoint(string friendlyName) =>
        Matches(friendlyName, CaptureNamePatterns);

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
/// VB-CABLE Virtual Audio Device (VB-Audio Software, gratuit) — standard du secteur.
/// Téléchargement officiel : download.vb-audio.com, archive Driver Pack.
/// </summary>
public sealed class VBCableProvider : VirtualAudioProviderBase
{
    public override string Id => "vbcable";
    public override string DisplayName => "VB-CABLE (gratuit)";
    public override string Description =>
        "Câble virtuel gratuit de VB-Audio : « CABLE Input » côté Windows, « CABLE Output » côté RoomEq.";
    public override VirtualCardKind Kind => VirtualCardKind.VirtualCable;

    public override VirtualInstallerSpec Installer { get; } = new()
    {
        Container = "zip",
        DownloadUrls = new[]
        {
            "https://download.vb-audio.com/Download_CABLE/VBCABLE_Driver_Pack45.zip",
            "https://download.vb-audio.com/Download_CABLE/VBCABLE_Driver_Pack44.zip",
            "https://download.vb-audio.com/Download_CABLE/VBCABLE_Driver_Pack43.zip"
        },
        SetupExecutableNames = new[] { "VBCABLE_Setup_X64.exe", "VBCABLE_Setup.exe" },
        ProductPageUrl = "https://vb-audio.com/Cable/",
        DiscoveryPageUrl = "https://vb-audio.com/Cable/",
        DiscoveryPattern = "https://download\\.vb-audio\\.com/Download_CABLE/VBCABLE_Driver_Pack[0-9]+\\.zip",
        PostLaunchHint = "Dans la fenêtre VB-CABLE, cliquez « Install Driver ».",
        LicenseNote = "Gratuit (donation suggérée à l'éditeur). Téléchargement depuis le site officiel vb-audio.com uniquement.",
        MinBytes = 1024 * 1024 // archive officielle réelle ≈ 1,3 Mo (Pack45)
    };

    public override string[] RenderNamePatterns { get; } =
        { "CABLE Input", "VB-CABLE" };
    public override string[] CaptureNamePatterns { get; } =
        { "CABLE Output", "VB-CABLE" };

    public override string[] GuideLines { get; } =
    {
        "1 · Cliquez « Installer automatiquement » : RoomEq télécharge l'archive officielle (download.vb-audio.com), la vérifie, l'extrait et lance VBCABLE_Setup_X64.exe — acceptez l'élévation Windows (UAC) puis le bouton « Install Driver ».",
        "2 · Dès que les périphériques sont détectés, RoomEq bascule tout seul : « CABLE Input » devient la sortie par défaut de Windows et une piste « CABLE Output » est ajoutée à la console (option « Router dès l'installation »).",
        "3 · Cliquez « Router Windows vers l'EQ » : RoomEq capte CABLE Output, applique l'égaliseur principal (onglet Égaliseur, synchronisé en temps réel) et rend le son sur la sortie physique choisie dans la tranche MASTER.",
        "4 · Pendant le routage, les périphériques par défaut d'origine de Windows (sortie et, si l'option est cochée, entrée) sont mémorisés — ils sont restaurés automatiquement quand vous arrêtez le routage.",
        "5 · Si les périphériques n'apparaissent pas après l'installation, redémarrez Windows (demande fréquent de l'installateur de pilotes), puis « ⟳ »."
    };
}

/// <summary>
/// VoiceMeeter (VB-Audio, gratuit) : sa table de mixage inclut des E/S virtuelles
/// (Vaio / Aux Vaio / VAIO3) utilisables comme câble par RoomEq.
/// </summary>
public sealed class VoiceMeeterProvider : VirtualAudioProviderBase
{
    public override string Id => "voicemeeter";
    public override string DisplayName => "VoiceMeeter (gratuit)";
    public override string Description =>
        "Console de mixage VB-Audio dont les entrées/sorties virtuelles (Vaio, Aux, VAIO3) servent de câble audio.";
    public override VirtualCardKind Kind => VirtualCardKind.VirtualCable;

    public override VirtualInstallerSpec Installer { get; } = new()
    {
        // VB-Audio ne publie plus l'installateur en .exe direct (404) : l'officiel est
        // désormais un ZIP versionné (ex. VoicemeeterSetup_v1130.zip) référencé par la
        // page produit — découverte automatique de la version courante prise en charge.
        Container = "zip",
        DownloadUrls = new[]
        {
            "https://download.vb-audio.com/Download_CABLE/VoicemeeterSetup_v1130.zip"
        },
        SetupExecutableNames = new[] { "voicemeetersetup.exe" },
        ProductPageUrl = "https://vb-audio.com/Voicemeeter/",
        DiscoveryPageUrl = "https://vb-audio.com/Voicemeeter/",
        DiscoveryPattern = "https://download\\.vb-audio\\.com/Download_CABLE/VoicemeeterSetup_v[0-9]+\\.zip",
        PostLaunchHint = "Suivez l'assistant Voicemeeter jusqu'au bout (Suivant → Installer → Terminer).",
        LicenseNote = "Gratuit (donation suggérée). Téléchargement depuis le site officiel vb-audio.com uniquement.",
        MinBytes = 8 * 1024 * 1024 // archive officielle réelle ≈ 22 Mo
    };

    public override string[] RenderNamePatterns { get; } =
        { "VoiceMeeter Input", "VoiceMeeter Aux Input", "VoiceMeeter VAIO3 Input" };
    public override string[] CaptureNamePatterns { get; } =
        { "VoiceMeeter Output", "VoiceMeeter Aux Output", "VoiceMeeter VAIO3 Output" };

    public override string[] GuideLines { get; } =
    {
        "1 · Cliquez « Installer automatiquement » : RoomEq télécharge l'archive officielle versionnée (la version courante est découverte sur la page produit), extrait voicemeetersetup.exe et le lance — acceptez l'élévation (UAC) puis suivez l'assistant.",
        "2 · Dès que les périphériques apparaissent, RoomEq ajoute la piste de capture et bascule Windows vers « VoiceMeeter Input » (option « Router dès l'installation ») ; vos défauts d'origine sont restaurés à l'arrêt du routage.",
        "3 · Cliquez « Router Windows vers l'EQ » : RoomEq capte la sortie VoiceMeeter correspondante, applique l'égaliseur et rend le son sur vos enceintes (sortie physique choisie dans la tranche MASTER).",
        "4 · Si plusieurs paires sont détectées (Vaio, Aux, VAIO3), la première trouvée est utilisée : vous pouvez ajouter les autres via « + Ajouter une piste ».",
        "5 · VoiceMeeter embarque sa propre console : elle n'est pas nécessaire au routage, mais permet en plus des mixages avancés."
    };
}

/// <summary>
/// Virtual Audio Cable (E. Muzychenko, commercial) : câbles « Line 1..N (Virtual
/// Audio Cable) ». Installation manuelle depuis la page produit (logiciel payant).
/// </summary>
public sealed class VacProvider : VirtualAudioProviderBase
{
    public override string Id => "vac";
    public override string DisplayName => "Virtual Audio Cable (VAC, payant)";
    public override string Description =>
        "Câbles multiples « Line N (Virtual Audio Cable) » — logiciel commercial, installation manuelle depuis la page produit.";
    public override VirtualCardKind Kind => VirtualCardKind.VirtualCable;

    public override VirtualInstallerSpec Installer { get; } = new()
    {
        Container = "zip",
        DownloadUrls = Array.Empty<string>(),   // payant : pas de téléchargement automatique
        SetupExecutableNames = Array.Empty<string>(),
        ProductPageUrl = "https://software.muzychenko.net/eng/vac.html",
        LicenseNote = "Shareware commercial (période d'essai avec voix ajoutée). Installation manuelle.",
        MinBytes = 1024 * 1024
    };

    public override string[] RenderNamePatterns { get; } = { "Virtual Audio Cable" };
    public override string[] CaptureNamePatterns { get; } = { "Virtual Audio Cable" };

    public override string[] GuideLines { get; } =
    {
        "1 · Ouvrez la « Page du produit » et achetez/téléchargez Virtual Audio Cable (E. Muzychenko).",
        "2 · Lancez l'installateur VAC en administrateur, puis « ⟳ » dans RoomEq : les câbles « Line 1 (Virtual Audio Cable) », « Line 2 »… apparaissent.",
        "3 · Cliquez « Définir par défaut » après avoir choisi le câble de sortie voulu dans le sélecteur de sortie master.",
        "4 · Ajoutez la piste correspondante (côté capture « Line N ») depuis « + Ajouter une piste ».",
        "5 · VAC permet jusqu'à N câbles indépendants : idéal pour router plusieurs applications vers des EQ différents."
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
        Container = "zip",
        DownloadUrls = Array.Empty<string>(),
        SetupExecutableNames = Array.Empty<string>(),
        ProductPageUrl = "",
        LicenseNote = "",
        MinBytes = 1024 * 1024
    };

    public override string[] RenderNamePatterns { get; } = Array.Empty<string>();
    public override string[] CaptureNamePatterns { get; } = Array.Empty<string>();

    public override string[] GuideLines { get; } =
    {
        "1 · Aucune installation : la boucle WASAPI est intégrée à Windows et à RoomEq.",
        "2 · Ajoutez la piste « <votre sortie> (boucle) » depuis « + Ajouter une piste » : elle capte tout ce que Windows joue sur cette sortie.",
        "3 · Choisissez une sortie master DIFFÉRENTE de la sortie captée (casque ≠ enceintes, ou câble virtuel) : RoomEq refuse toute boucle sur elle-même (protection anti-larsen).",
        "4 · Avantage : latence minimale, rien à installer. Limite : seule la sortie captée passe par l'EQ (pas le son d'une autre sortie).",
        "5 · Pour router TOUT le système via une carte virtuelle dédiée, préférez VB-CABLE ou VoiceMeeter."
    };
}

/// <summary>
/// Registre des fournisseurs de cartes son virtuelles pris en charge, avec
/// résolution par identifiant persisté.
/// </summary>
public static class VirtualAudioCatalog
{
    /// <summary>Fournisseurs pris en charge, dans l'ordre d'affichage.</summary>
    public static IReadOnlyList<IVirtualAudioProvider> All { get; } = new IVirtualAudioProvider[]
    {
        new VBCableProvider(),
        new VoiceMeeterProvider(),
        new VacProvider(),
        new LoopbackProvider()
    };

    /// <summary>Fournisseur par défaut (VB-CABLE).</summary>
    public static IVirtualAudioProvider Default { get; } = All[0];

    /// <summary>Résout un fournisseur par identifiant ; retombe sur VB-CABLE si inconnu.</summary>
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
    public static bool IsVirtualRender(string friendlyName) =>
        All.Any(p => p.Kind == VirtualCardKind.VirtualCable && p.IsRenderEndpoint(friendlyName));

    /// <summary>Un des fournisseurs de câble virtuel reconnaît-il cette capture ?</summary>
    public static bool IsVirtualCapture(string friendlyName) =>
        All.Any(p => p.Kind == VirtualCardKind.VirtualCable && p.IsCaptureEndpoint(friendlyName));
}
