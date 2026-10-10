using System.Linq;
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Platform.Storage;
using RoomEq.App.Controls;
using RoomEq.App.ViewModels;
using RoomEq.Core.Dsp;

namespace RoomEq.App.Views;

public partial class MixerTab : UserControl
{
    private ChannelViewModel? _lastFileChannel;

    public MixerTab()
    {
        InitializeComponent();
        DataContextChanged += (_, _) => Attach();
        Loaded += (_, _) => Attach();
    }

    private MixerViewModel? Vm => DataContext as MixerViewModel;

    private void Attach()
    {
        var vm = Vm;
        if (vm == null) return;

        // suit l'évolution de l'éditeur d'EQ de piste (changement de piste, édition)
        vm.TrackEq.CurveChanged -= OnTrackCurveChanged;
        vm.TrackEq.CurveChanged += OnTrackCurveChanged;
        vm.PropertyChanged -= OnVmPropertyChanged;
        vm.PropertyChanged += OnVmPropertyChanged;

        if (vm.TrackEq.Bands.Count > 0)
            OnTrackCurveChanged(vm.TrackEq.CurrentCurve());
        else
            OnTrackCurveChanged(new System.Collections.Generic.List<CurvePoint>());
    }

    private void OnVmPropertyChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(MixerViewModel.TrackEq) && Vm is { } vm)
        {
            vm.TrackEq.CurveChanged -= OnTrackCurveChanged;
            vm.TrackEq.CurveChanged += OnTrackCurveChanged;
            OnTrackCurveChanged(vm.TrackEq.CurrentCurve());
        }
    }

    private void OnTrackCurveChanged(System.Collections.Generic.List<CurvePoint> curve)
    {
        var plot = TrackEqPlot;
        if (plot == null) return;

        var series = new System.Collections.Generic.List<PlotSeries>();
        if (curve.Count > 0)
            series.Add(PlotSeries.Create("Réponse de la piste", curve, Color.Parse("#A78BFA"), fill: true));

        // rappel discret de la réponse du bus master (EQ principal), pour comparer
        var mixer = Vm?.Mixer;
        if (mixer != null)
        {
            var master = mixer.MasterEq.ResponseCurve(20, 20000, 240);
            if (master.Any(p => Math.Abs(p.Db) > 0.05))
                series.Add(PlotSeries.Create("EQ principal (master)", master, Color.Parse("#22D3EE"), dashed: true));
        }

        plot.ShowSeries(series);
    }

    // ------------------------------------------------------------ lecteur de tranche

    private async void OnPickChannelFile(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        if (Vm == null) return;
        if (sender is Button { DataContext: ChannelViewModel chvm })
            _lastFileChannel = chvm;

        var target = _lastFileChannel;
        if (target == null) return;

        try
        {
            var top = TopLevel.GetTopLevel(this)
                ?? throw new InvalidOperationException("Fenêtre indisponible.");
            var files = await top.StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
            {
                Title = "Charger un fichier dans la tranche Lecteur",
                AllowMultiple = false,
                FileTypeFilter = new[]
                {
                    new FilePickerFileType("Audio (wav, mp3, flac, aiff, wma, m4a)")
                    {
                        Patterns = new[] { "*.wav", "*.mp3", "*.flac", "*.aiff", "*.aif", "*.wma", "*.m4a" }
                    },
                    new FilePickerFileType("Tous les fichiers") { Patterns = new[] { "*" } }
                }
            });

            var file = files.FirstOrDefault();
            if (file == null) return;

            target.Channel.LoadFile(file.Path.LocalPath);
            target.RefreshIdentity();
            Vm.UpdateEqLedFor(target);
            Vm.StatusText = "Fichier chargé dans la tranche Lecteur : " + target.Title;
        }
        catch (Exception ex)
        {
            Vm.StatusText = "Chargement impossible : " + ex.Message;
        }
    }

    private void OnEjectChannelFile(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        if (sender is Button { DataContext: ChannelViewModel chvm })
        {
            chvm.EjectFile();
            if (Vm != null) Vm.StatusText = "Tranche Lecteur vidée.";
        }
    }
}
