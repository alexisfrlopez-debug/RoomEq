using Avalonia.Controls;

namespace RoomEq.App.Views;

/// <summary>
/// Onglet Mixage (D26, v3.9.0) : la console a été retirée — l'onglet est le
/// poste de pilotage de l'EQ système (sorties, verdict, volume d'écoute,
/// preset). Tout est piloté par bindings : plus aucun code-behind nécessaire.
/// </summary>
public partial class MixerTab : UserControl
{
    public MixerTab()
    {
        InitializeComponent();
    }
}
