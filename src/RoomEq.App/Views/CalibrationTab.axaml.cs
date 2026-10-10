using Avalonia.Controls;
using RoomEq.App.Controls;
using RoomEq.App.ViewModels;

namespace RoomEq.App.Views;

public partial class CalibrationTab : UserControl
{
    public CalibrationTab()
    {
        InitializeComponent();
        DataContextChanged += (_, _) => Attach();
    }

    private void Attach()
    {
        if (Plot == null || DataContext is not CalibrationViewModel vm) return;
        vm.Attach(Plot);

        // afficher immédiatement le dernier résultat s'il existe
        if (vm.Result != null)
        {
            Plot.ShowSeries(vm.BuildSeries(vm.Result));
        }
    }
}
