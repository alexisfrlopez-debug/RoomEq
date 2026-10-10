using System.Collections.Generic;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Platform.Storage;
using RoomEq.App.Controls;
using RoomEq.App.ViewModels;
using RoomEq.Core.Dsp;

namespace RoomEq.App.Views;

public partial class MicTab : UserControl
{
    private MicViewModel? _vm;

    public MicTab()
    {
        InitializeComponent();
        DataContextChanged += (_, _) => Attach();
    }

    private void Attach()
    {
        if (Editor == null || DataContext is not MicViewModel vm) return;

        // se désabonner de l'ancien VM (changement de DataContext)
        if (_vm != null)
        {
            _vm.SelectionChanged -= Redraw;
            _vm.ProfileListChanged -= Redraw;
            _vm.CurveEdited -= OnCurveEdited;
        }

        _vm = vm;
        vm.SelectionChanged += Redraw;
        vm.ProfileListChanged += Redraw;
        vm.CurveEdited += OnCurveEdited;

        // se désabonner d'abord : les handlers ne doivent jamais s'empiler
        Editor.PointDragged -= OnPointDragged;
        Editor.PointAddRequested -= OnPointAddRequested;
        Editor.PointDeleteRequested -= OnPointDeleteRequested;
        Editor.SelectionChanged -= OnEditorSelectionChanged;
        Editor.PointDragged += OnPointDragged;
        Editor.PointAddRequested += OnPointAddRequested;
        Editor.PointDeleteRequested += OnPointDeleteRequested;
        Editor.SelectionChanged += OnEditorSelectionChanged;

        Redraw();
    }

    private void Redraw()
    {
        if (_vm == null) return;
        var curve = _vm.SelectedCurve();
        int sel = _vm.SelectedPointIndex;
        if (curve.Count >= 2)
        {
            Editor.SetCurve(curve, sel);
        }
        else
        {
            Editor.SetCurve(new List<CurvePoint>(), -1);
        }
    }

    private void OnCurveEdited()
    {
        // les éditions venues des boutons du VM : resynchroniser l'affichage
        if (_vm == null) return;
        Editor.SetCurve(_vm.SelectedCurve(), _vm.SelectedPointIndex);
    }

    private void OnPointDragged(int index, double freq, double gain)
    {
        // pas de resynchronisation complète pendant le glissement (le contrôle
        // garde son miroir local) : le VM suit en silence
        _vm?.MovePoint(index, freq, gain);
    }

    private void OnPointAddRequested(double freq, double gain)
    {
        if (_vm == null) return;
        int idx = _vm.AddPointAt(freq, gain);
        Editor.SetCurve(_vm.SelectedCurve(), idx);
    }

    private void OnPointDeleteRequested(int index)
    {
        if (_vm == null) return;
        _vm.RemovePointAt(index);
        Editor.SetCurve(_vm.SelectedCurve(), -1);
    }

    private void OnEditorSelectionChanged(int index)
    {
        _vm?.SelectPoint(index);
    }

    private async void OnImportClick(object? sender, RoutedEventArgs e)
    {
        if (DataContext is not MicViewModel vm) return;

        var topLevel = TopLevel.GetTopLevel(this);
        if (topLevel?.StorageProvider == null) return;

        var files = await topLevel.StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
        {
            Title = "Importer un fichier de calibration micro",
            AllowMultiple = false,
            FileTypeFilter = new[]
            {
                new FilePickerFileType("Calibration (*.cal, *.csv, *.txt)")
                {
                    Patterns = new[] { "*.cal", "*.csv", "*.txt" }
                },
                new FilePickerFileType("Tous les fichiers")
                {
                    Patterns = new[] { "*" }
                }
            }
        });

        var file = files.Count > 0 ? files[0] : null;
        if (file != null)
        {
            vm.ImportFromFile(file.Path.LocalPath);
        }
    }
}
