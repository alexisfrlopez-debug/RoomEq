using Avalonia.Controls;
using Avalonia.Data.Converters;
using RoomEq.App.Controls;
using RoomEq.App.ViewModels;

namespace RoomEq.App.Views;

public partial class AnalyzerTab : UserControl
{
    public static readonly IValueConverter StartStop =
        new FuncValueConverter<bool, string>(running => running ? "Arrêter le micro" : "Démarrer le micro");

    public AnalyzerTab()
    {
        InitializeComponent();
        DataContextChanged += (_, _) => Attach();
    }

    private void Attach()
    {
        if (Plot == null || DataContext is not AnalyzerViewModel vm) return;
        vm.Attach(Plot);
    }
}
