using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;

namespace RoomEq.App.Controls;

/// <summary>
/// Boîte de dialogue d'aide contextuelle : titre + explication détaillée d'une
/// fonctionnalité (ouverte par les boutons « ? » de l'interface). Thème sombre
/// de l'application appliqué automatiquement (styles globaux).
/// </summary>
public static class HelpDialog
{
    public static void Show(Control owner, string title, string text)
    {
        var window = new Window
        {
            Title = "Aide — " + title,
            Width = 560,
            MinHeight = 160,
            SizeToContent = SizeToContent.Height,
            CanResize = false,
            ShowInTaskbar = false,
            WindowStartupLocation = WindowStartupLocation.CenterOwner,
            MaxWidth = 640
        };

        var stack = new StackPanel { Margin = new Thickness(20, 18, 20, 16), Spacing = 12 };

        var heading = new TextBlock
        {
            Text = title,
            FontSize = 16,
            FontWeight = FontWeight.Bold,
            Foreground = new SolidColorBrush(Color.Parse("#FF9F1C")), // accent du thème
            TextWrapping = TextWrapping.Wrap
        };

        var body = new TextBlock
        {
            Text = string.IsNullOrWhiteSpace(text)
                ? "(Aucune description disponible pour cette fonctionnalité.)"
                : text,
            FontSize = 13,
            LineHeight = 20,
            TextWrapping = TextWrapping.Wrap
        };

        var close = new Button { Content = "Fermer", HorizontalAlignment = HorizontalAlignment.Right, MinWidth = 90 };
        close.Click += (_, _) => window.Close();

        stack.Children.Add(heading);
        stack.Children.Add(body);
        stack.Children.Add(close);
        window.Content = stack;

        // Échap ferme la fenêtre
        window.KeyDown += (_, e) =>
        {
            if (e.Key == global::Avalonia.Input.Key.Escape) window.Close();
        };

        var parent = TopLevel.GetTopLevel(owner) as Window;
        if (parent != null && parent.IsVisible)
            window.ShowDialog(parent);
        else
            window.Show();
    }
}
