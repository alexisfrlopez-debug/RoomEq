using Avalonia;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Media;

namespace RoomEq.App.Controls;

/// <summary>
/// Petit bouton circulaire « ? » à placer à côté d'une fonctionnalité :
/// l'infobulle (survol) et la boîte de dialogue (clic) affichent la même
/// explication détaillée. Propriétés : <see cref="HelpTitle"/> (titre de la
/// fonctionnalité) et <see cref="HelpText"/> (explication, retours à la ligne
/// autorisés).
/// </summary>
public sealed class HelpButton : Button
{
    public static readonly StyledProperty<string> HelpTitleProperty =
        AvaloniaProperty.Register<HelpButton, string>(nameof(HelpTitle), "Aide");

    public static readonly StyledProperty<string> HelpTextProperty =
        AvaloniaProperty.Register<HelpButton, string>(nameof(HelpText), "");

    public string HelpTitle
    {
        get => GetValue(HelpTitleProperty);
        set => SetValue(HelpTitleProperty, value);
    }

    public string HelpText
    {
        get => GetValue(HelpTextProperty);
        set => SetValue(HelpTextProperty, value);
    }

    public HelpButton()
    {
        Content = "?";
        Width = 21;
        Height = 21;
        Padding = new Thickness(0);
        CornerRadius = new CornerRadius(10.5);
        FontSize = 11;
        FontWeight = FontWeight.Bold;
        VerticalAlignment = Avalonia.Layout.VerticalAlignment.Center;
        HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Left;
        Classes.Add("help");

        // infobulle = texte d'aide (survol) ; clic = boîte de dialogue complète
        UpdateTip(this);
        Click += OnClick;
    }

    private static void UpdateTip(HelpButton b)
    {
        string tip = b.HelpText;
        if (tip.Length > 0) ToolTip.SetTip(b, tip);
    }

    private void OnClick(object? sender, RoutedEventArgs e)
        => HelpDialog.Show(this, HelpTitle, HelpText);

    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);
        if (change.Property == HelpTextProperty) UpdateTip(this);
    }
}
