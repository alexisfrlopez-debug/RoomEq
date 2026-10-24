using System.Diagnostics;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Layout;
using Avalonia.Media;
using RoomEq.Core.Io;

namespace RoomEq.App.Controls;

/// <summary>
/// Boîte de dialogue montrée au lancement de la VERSION TEST (lorsque l'exécutable
/// public « RoomEq.exe » est présent à côté) : elle propose de lancer la version
/// publique à la place. « Lancer la version publique » démarre RoomEq.exe puis
/// arrête cette instance de test ; tout autre choix (bouton, croix, Échap)
/// continue en version test. Thème sombre de l'application appliqué par les
/// styles globaux.
/// </summary>
public static class FlavorDialog
{
    public static void Show(Window owner)
    {
        var window = new Window
        {
            Title = "RoomEq — version test",
            Width = 540,
            SizeToContent = SizeToContent.Height,
            CanResize = false,
            ShowInTaskbar = false,
            WindowStartupLocation = WindowStartupLocation.CenterOwner,
            MaxWidth = 620
        };

        var stack = new StackPanel { Margin = new Thickness(20, 18, 20, 16), Spacing = 12 };

        var heading = new TextBlock
        {
            Text = "Vous utilisez la version TEST",
            FontSize = 16,
            FontWeight = FontWeight.Bold,
            Foreground = new SolidColorBrush(Color.Parse("#FF9F1C")),
            TextWrapping = TextWrapping.Wrap
        };

        var body = new TextBlock
        {
            Text = "Cette version enregistre des journaux de diagnostic et les envoie "
                 + "automatiquement vers le Drive de développement (pour corriger et "
                 + "améliorer RoomEq).\n\n"
                 + "Si vous n'êtes pas en train de tester l'application, il est "
                 + "préférable d'utiliser la version publique : elle ne journalise "
                 + "rien et n'envoie rien.\n\n"
                 + $"La version publique se trouve à côté de ce programme "
                 + $"({System.IO.Path.GetFileName(AppFlavor.PublicExePath)}).",
            FontSize = 13,
            LineHeight = 20,
            TextWrapping = TextWrapping.Wrap
        };

        var buttons = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            HorizontalAlignment = HorizontalAlignment.Right,
            Spacing = 8
        };

        var launchPublic = new Button
        {
            Content = "Lancer la version publique",
            MinWidth = 190,
            Classes = { "accent" }
        };
        var stayTest = new Button { Content = "Continuer en version test", MinWidth = 150 };

        launchPublic.Click += (_, _) =>
        {
            DiagnosticsLog.Info("Version publique demandée depuis la version test — lancement puis arrêt.");
            try
            {
                var psi = new ProcessStartInfo(AppFlavor.PublicExePath)
                {
                    UseShellExecute = true,
                    WorkingDirectory = AppContext.BaseDirectory
                };
                Process.Start(psi);
            }
            catch (Exception ex)
            {
                DiagnosticsLog.Error("Lancement de la version publique impossible", ex);
            }
            finally
            {
                window.Close();
                Shutdown();
            }
        };
        stayTest.Click += (_, _) =>
        {
            DiagnosticsLog.Info("L'utilisateur reste en version test.");
            window.Close();
        };

        // Échap = rester en version test (la croix de la fenêtre agit de même)
        window.KeyDown += (_, e) =>
        {
            if (e.Key == global::Avalonia.Input.Key.Escape) window.Close();
        };

        buttons.Children.Add(launchPublic);
        buttons.Children.Add(stayTest);
        stack.Children.Add(heading);
        stack.Children.Add(body);
        stack.Children.Add(buttons);
        window.Content = stack;

        // modal si le propriétaire est visible, sinon fenêtre simple
        if (owner.IsVisible)
            window.ShowDialog(owner);
        else
            window.Show();
    }

    private static void Shutdown()
    {
        if (Application.Current?.ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
            desktop.Shutdown();
    }
}
