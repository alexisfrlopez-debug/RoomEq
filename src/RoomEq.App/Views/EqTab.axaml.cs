using Avalonia.Controls;
using Avalonia.Platform.Storage;
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

        // export .ffp FabFilter Pro-Q 3 : la vue possède le dialogue d'enregistrement
        vm.FabFilterExportRequested -= OnFabFilterExportRequested;
        vm.FabFilterExportRequested += OnFabFilterExportRequested;

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

    /// <summary>Ouvre le dialogue d'enregistrement puis laisse le ViewModel écrire le .ffp.</summary>
    private async void OnFabFilterExportRequested()
    {
        if (DataContext is not EqViewModel vm) return;

        var topLevel = TopLevel.GetTopLevel(this);
        if (topLevel?.StorageProvider == null) return;

        var file = await topLevel.StorageProvider.SaveFilePickerAsync(new FilePickerSaveOptions
        {
            Title = "Exporter le preset vers FabFilter Pro-Q 3",
            SuggestedFileName = vm.SuggestedFabFilterFileName(),
            DefaultExtension = "ffp",
            FileTypeChoices = new[]
            {
                new FilePickerFileType("Preset FabFilter Pro-Q 3 (*.ffp)")
                {
                    Patterns = new[] { "*.ffp" }
                }
            }
        });

        if (file == null) return;   // dialogue annulé

        string path;
        try
        {
            path = file.Path.LocalPath;
        }
        catch (Exception ex)
        {
            vm.ExportStatus = "Export impossible : " + ex.Message;
            return;
        }
        vm.SaveFabFilterExport(path);
    }
}
