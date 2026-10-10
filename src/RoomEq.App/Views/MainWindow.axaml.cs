using System.Linq;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Platform.Storage;
using Avalonia.Threading;
using RoomEq.App.Controls;
using RoomEq.App.ViewModels;
using RoomEq.Core.Io;

namespace RoomEq.App.Views;

public partial class MainWindow : Window
{
    private readonly MainViewModel _vm;
    private readonly DispatcherTimer _osdTimer;

    public MainWindow(MainViewModel vm)
    {
        InitializeComponent();
        _vm = vm;
        DataContext = vm;

        // la version en cours est visible dans la barre de titre (« test » ou « public »)
        string version = typeof(App).Assembly.GetName().Version?.ToString(3) ?? "?";
        Title = $"RoomEq {version} — version {AppFlavor.DisplayName} · Égaliseur, analyseur & calibration";

        // sauvegarde de la configuration + dernier envoi de journaux à la fermeture
        Closed += (_, _) => vm.OnAppClosing();

        // OSD du volume de la carte physique (raccourcis Ctrl+Alt+↑ / ↓ / M)
        vm.Mixer.RoutedVolumeFeedback += ShowVolumeOsd;
        _osdTimer = new DispatcherTimer(TimeSpan.FromSeconds(1.6), DispatcherPriority.Background,
            (_, _) =>
            {
                _osdTimer.Stop();
                VolumeOsd.IsVisible = false;
            });

        // catalogue des signaux de test supplémentaires (téléchargement)
        vm.SignalCatalogRequested += () => SignalCatalogDialog.Show(this, vm.RefreshSignalChoices);

        // routage automatique au lancement si configuré
        Dispatcher.UIThread.Post(() => vm.Mixer.AutoStartIfConfigured());
    }

    private void ShowVolumeOsd(string text)
    {
        VolumeOsdText.Text = text;
        VolumeOsd.IsVisible = true;
        _osdTimer.Stop();
        _osdTimer.Start();
    }

    private async void OnOpenFileClick(object? sender, RoutedEventArgs e)
    {
        var files = await StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
        {
            Title = "Ouvrir un fichier audio",
            AllowMultiple = false,
            FileTypeFilter = new[]
            {
                new FilePickerFileType("Audio (wav, mp3, flac, aiff, wma, m4a)")
                {
                    Patterns = new[] { "*.wav", "*.mp3", "*.flac", "*.aiff", "*.aif", "*.wma", "*.m4a" }
                },
                new FilePickerFileType("Tous les fichiers")
                {
                    Patterns = new[] { "*" }
                }
            }
        });

        var file = files.FirstOrDefault();
        if (file != null)
        {
            try
            {
                _vm.PlayFile(file.Path.LocalPath);
            }
            catch (Exception ex)
            {
                _vm.StatusText = "Lecture impossible : " + ex.Message;
            }
        }
    }
}
