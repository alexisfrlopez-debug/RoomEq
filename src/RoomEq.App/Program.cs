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
        // ce mode est sans fenêtre et ne touche jamais à Avalonia.
        if (args.Length > 0 && args[0] == "--apo-setup")
        {
            return RoomEq.Audio.SystemApo.ApoSetupCommands.Run(
                args.Length > 1 ? args[1] : "",
                args.Skip(2).ToArray(),
                AppContext.BaseDirectory);
        }

        BuildAvaloniaApp().StartWithClassicDesktopLifetime(args);
        return 0;
    }

    public static AppBuilder BuildAvaloniaApp()
        => AppBuilder.Configure<App>()
            .UsePlatformDetect()
            .LogToTrace();
}
