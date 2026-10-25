namespace RoomEq.Core.Io;

/// <summary>Catégories d'exécutable RoomEq — spec I-IO-01.</summary>
public enum AppFlavorKind
{
    /// <summary>Version de test : journalisation complète, envoi Drive si configuré.</summary>
    Test,

    /// <summary>Version publique : aucun journal écrit, aucun envoi, aucune trace.</summary>
    Public
}

/// <summary>
/// Détermine la « version » de l'exécutable en cours (test ou public) à partir
/// de son NOM DE FICHIER (spec I-IO-01) : le SILENCE est la valeur par défaut
/// (respect de la vie privée) — seul un nom contenant « test » ou la variable
/// ROOMEQ_TEST=1 active journalisation et télémétrie.
/// </summary>
public static class AppFlavor
{
    /// <summary>Version détectée au chargement de la classe (jamais modifiée ensuite).</summary>
    public static AppFlavorKind Current { get; } = Detect();

    public static bool IsPublic => Current == AppFlavorKind.Public;
    public static bool IsTest => Current == AppFlavorKind.Test;

    /// <summary>Nom affichable de la version courante : « test » ou « public ».</summary>
    public static string DisplayName => Current == AppFlavorKind.Test ? "test" : "public";

    /// <summary>Chemin présumé de l'exécutable public voisin — peut ne pas exister.</summary>
    public static string PublicExePath { get; } =
        Path.Combine(AppContext.BaseDirectory, "RoomEq.exe");

    /// <summary>L'exécutable public voisin est-il présent ?</summary>
    public static bool PublicExeAvailable => File.Exists(PublicExePath);

    /// <summary>
    /// Règle de décision par nom de fichier (fonction pure, testée) : un nom
    /// contenant « test » = version de test ; tout le reste — y compris le
    /// « RoomEq.exe » installé — est public (silencieux). ROOMEQ_TEST=1 force
    /// la version test. Ne lève jamais.
    /// </summary>
    public static AppFlavorKind FromExecutableName(string executablePathOrName)
    {
        string name = Path.GetFileName(executablePathOrName ?? "")?.ToLowerInvariant() ?? "";
        if (name.Contains("test", StringComparison.Ordinal))
            return AppFlavorKind.Test;
        try
        {
            if (Environment.GetEnvironmentVariable("ROOMEQ_TEST") == "1")
                return AppFlavorKind.Test;
        }
        catch
        {
            // environnement illisible : rester public (silencieux)
        }
        return AppFlavorKind.Public;
    }

    private static AppFlavorKind Detect()
    {
        try
        {
            string? exe = Environment.ProcessPath;
            return exe is null ? AppFlavorKind.Public : FromExecutableName(exe);
        }
        catch
        {
            // en cas d'imprévu, la version publique (silencieuse) est le comportement sûr
            return AppFlavorKind.Public;
        }
    }
}
