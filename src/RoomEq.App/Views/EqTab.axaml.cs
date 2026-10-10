using Avalonia.Controls;
using RoomEq.App.Controls;
using RoomEq.App.ViewModels;

namespace RoomEq.App.Views;

public partial class EqTab : UserControl
{
    public EqTab()
    {
        InitializeComponent();
        DataContextChanged += (_, _) => Attach();
        Loaded += (_, _) => Attach();
    }

    private void Attach()
    {
        if (EqPlot == null || DataContext is not EqViewModel vm) return;

        EqPlot.Bands = vm.Bands;
        EqPlot.BandDragged = vm.OnBandDragged;
        EqPlot.BandQChanged = vm.OnBandQChanged;
        EqPlot.BandSelected = idx => vm.SelectedIndex = idx;

        // rafraîchit le graphe à chaque recalcul de la courbe composite
        vm.CurveChanged -= OnCurveChanged;
        vm.CurveChanged += OnCurveChanged;

        // suit la liste des bandes (ajout / suppression / preset)
        vm.PropertyChanged -= OnVmPropertyChanged;
        vm.PropertyChanged += OnVmPropertyChanged;

        if (vm.Bands.Count > 0)
        {
            EqPlot.SetCurve(vm.CurrentCurve());
        }
    }

    private void OnCurveChanged(List<Core.Dsp.CurvePoint> curve) => EqPlot.SetCurve(curve);

    private void OnVmPropertyChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(EqViewModel.Bands) && DataContext is EqViewModel vm)
            EqPlot.Bands = vm.Bands;
    }
}
