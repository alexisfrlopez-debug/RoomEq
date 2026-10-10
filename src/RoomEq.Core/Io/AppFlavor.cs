namespace RoomEq.Core.Io;

/// <summary>Catégories d'exécutable RoomEq.</summary>
public enum AppFlavorKind
{
    /// <summary>Version de test : journalisation complète, envoi Drive si configuré.</summary>
    Test,

    /// <summary>Version publique : aucun journal écrit, aucun envoi, aucune trace.</summary>
    Public
}

/// <summary>
/// Détermine la « version » de l'exécutable en cours (test ou public) à partir de
/// son NOM DE FICHIER : les deux versions sont des copies du même binaire publiées
/// côte à côte — « RoomEq-test.exe » et « RoomEq-public.exe » chargent les mêmes
/// DLL et ne diffèrent que par leur comportement de journalisation/télémétrie.
/// Tout nom ne contenant pas « public » est traité comme la version de test
/// (y compris le nom de développement « RoomEq.App.exe ») : règle d'or, une
/// version publique doit toujours s'annoncer explicitement.
/// </summary>
public static class AppFlavor
{
    /// <summary>Version détectée au chargement de la classe (jamais modifiée ensuite).</summary>
    public static AppFlavorKind Current { get; } = Detect();

    public static bool IsPublic => Current == AppFlavorKind.Public;
    public static bool IsTest => Current == AppFlavorKind.Test;

    /// <summary>Nom affichable de la version courante : « test » ou « public ».</summary>
    public static string DisplayName => Current == AppFlavorKind.Public ? "public" : "test";

    /// <summary>
    /// Chemin présumé de l'exécutable public voisin (à côté de l'exécutable courant) ;
    /// peut ne pas exister — voir <see cref="PublicExeAvailable"/>.
    /// </summary>
    public static string PublicExePath { get; } =
        Path.Combine(AppContext.BaseDirectory, "RoomEq-public.exe");

    /// <summary>L'exécutable public voisin est-il présent (bascule possible depuis la version test) ?</summary>
    public static bool PublicExeAvailable => File.Exists(PublicExePath);

    /// <summary>
    /// Règle de décision par nom de fichier (fonction pure, testée) :
    /// un exécutable dont le nom contient « public » est la version publique.
    /// </summary>
    public static AppFlavorKind FromExecutableName(string executablePathOrName)
    {
        string name = Path.GetFileName(executablePathOrName)?.ToLowerInvariant() ?? "";
        return name.Contains("public") ? AppFlavorKind.Public : AppFlavorKind.Test;
    }

    private static AppFlavorKind Detect()
    {
        try
        {
            string? exe = Environment.ProcessPath;
            return exe is null ? AppFlavorKind.Test : FromExecutableName(exe);
        }
        catch
        {
            // en cas d'imprévu, la version de test est le comportement historique
            return AppFlavorKind.Test;
        }
    }
}
