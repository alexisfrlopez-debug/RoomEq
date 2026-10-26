using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using Avalonia.Threading;
using RoomEq.App.ViewModels;
using RoomEq.App.Views;
using RoomEq.Core.Io;

namespace RoomEq.App;

public class App : Application
{
    public override void Initialize()
    {
        AvaloniaXamlLoader.Load(this);
    }

    public override void OnFrameworkInitializationCompleted()
    {
        // version publique : AUCUN journal écrit, AUCUN envoi — à régler AVANT
        // la première trace ; version test : journalisation complète
        DiagnosticsLog.Enabled = !AppFlavor.IsPublic;

        // journal de diagnostic : session + exceptions globales (jamais silencieuses)
        string version = typeof(App).Assembly.GetName().Version?.ToString(3) ?? "?";
        DiagnosticsLog.Info($"─── RoomEq v{version} — version {AppFlavor.DisplayName} — session démarrée ───");
        AppDomain.CurrentDomain.UnhandledException += (_, e) =>
            DiagnosticsLog.Error("Exception non gérée (processus)", e.ExceptionObject as Exception);
        Avalonia.Threading.Dispatcher.UIThread.UnhandledException += (_, e) =>
        {
            DiagnosticsLog.Error("Exception non gérée (interface)", e.Exception);
            e.Handled = true; // l'application reste utilisable, l'erreur est journalisée
        };

        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            var vm = new MainViewModel();
            desktop.MainWindow = new MainWindow(vm);
            desktop.ShutdownRequested += (_, _) => DiagnosticsLog.Info("Session terminée.");

            // version test avec version publique disponible à côté : proposer la
            // bascule dès l'ouverture (une seule fois, fenêtre modale)
            if (AppFlavor.IsTest && AppFlavor.PublicExeAvailable)
            {
                var main = desktop.MainWindow;
                main.Opened += (_, _) => Dispatcher.UIThread.Post(
                    () => Controls.FlavorDialog.Show(main), DispatcherPriority.ApplicationIdle);
            }
        }

        base.OnFrameworkInitializationCompleted();
    }
}
