using Avalonia;

namespace RoomEq.App;

internal static class Program
{
    [STAThread]
    public static int Main(string[] args)
    {
        // Mode d'installation APO (spec I-AUD-09) : RoomEq.exe --apo-setup …
        // est relancé par l'application ELLE-MÊME en administrateur (invite
        // UAC unique) pour inscrire l'égaliseur système sur une sortie —
        // ce mode est sans fenêtre et ne touche jamais à Avalonia. Il passe
        // AVANT le garde d'instance unique : l'élévation doit toujours
        // pouvoir s'exécuter à côté de l'application ouverte.
        if (args.Length > 0 && args[0] == "--apo-setup")
        {
            if (!OperatingSystem.IsWindows())
                return 2; // mode d'installation de l'EQ système : Windows uniquement
            return RoomEq.Audio.SystemApo.ApoSetupCommands.Run(
                args.Length > 1 ? args[1] : "",
                args.Skip(2).ToArray(),
                AppContext.BaseDirectory);
        }

        // Instance unique (D26) : l'installateur lance l'application à la fin
        // de l'installation ; toute instance surnuméraire (Setup exécuté deux
        // fois, raccourci ouvert pendant un démarrage lent) se ferme
        // immédiatement et silencieusement — plus jamais de fenêtres en
        // double ni de mixer.json corrompu par écritures concurrentes.
        using var instance = SingleInstanceGuard.Acquire("RoomEq.App.SingleInstance");
        if (instance == null)
            return 0;

        BuildAvaloniaApp().StartWithClassicDesktopLifetime(args);
        return 0;
    }

    public static AppBuilder BuildAvaloniaApp()
        => AppBuilder.Configure<App>()
            .UsePlatformDetect()
            .LogToTrace();
}
