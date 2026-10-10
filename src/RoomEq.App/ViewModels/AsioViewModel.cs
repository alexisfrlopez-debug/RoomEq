using System.Collections.ObjectModel;
using System.Linq;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using RoomEq.Audio.Asio;

namespace RoomEq.App.ViewModels;

/// <summary>
/// Gestion des pilotes ASIO : énumération, informations de capacité,
/// ouverture du panneau de contrôle du pilote.
/// </summary>
public partial class AsioViewModel : ObservableObject
{
    private readonly Action _stopPlayback;

    [ObservableProperty] private ObservableCollection<string> drivers = new();
    [ObservableProperty] private string? selectedDriver;
    [ObservableProperty] private string info = "";
    [ObservableProperty] private ObservableCollection<int> supportedRates = new();

    public bool NoDriver => Drivers.Count == 0;

    public AsioViewModel(Action stopPlayback)
    {
        _stopPlayback = stopPlayback;
        Refresh();
    }

    public void Refresh()
    {
        Drivers = new ObservableCollection<string>(AsioManager.DriverNames);
        SelectedDriver = Drivers.FirstOrDefault();
        OnPropertyChanged(nameof(NoDriver));
    }

    partial void OnSelectedDriverChanged(string? value) => RefreshInfo();

    private void RefreshInfo()
    {
        if (SelectedDriver == null)
        {
            Info = Drivers.Count == 0
                ? "Aucun pilote ASIO détecté.\n\nInstallez le pilote ASIO de votre interface audio ou ASIO4ALL, puis cliquez sur Rafraîchir."
                : "Sélectionnez un pilote.";
            SupportedRates = new ObservableCollection<int>();
            return;
        }

        _stopPlayback(); // libérer le pilote s'il est utilisé par la lecture

        try
        {
            int outCh = AsioManager.OutputChannels(SelectedDriver);
            int inCh = AsioManager.InputChannels(SelectedDriver);
            int frames = AsioManager.FramesPerBuffer(SelectedDriver);
            int latency = AsioManager.PlaybackLatency(SelectedDriver);
            var rates = AsioManager.SupportedSampleRates(SelectedDriver);
            SupportedRates = new ObservableCollection<int>(rates);

            Info =
                $"Pilote : {SelectedDriver}\n" +
                $"Canaux de sortie : {outCh} · canaux d'entrée : {inCh}\n" +
                $"Buffer : {frames} frames · latence de lecture : {latency} frames\n" +
                $"Taux d'échantillonnage supportés : {string.Join(", ", rates)} Hz\n\n" +
                "En mode ASIO, RoomEq joue en exclusif sur ce pilote : le reste du système " +
                "ne sort plus par cette interface pendant la lecture.";
        }
        catch (Exception ex)
        {
            Info = "Pilote indisponible : " + ex.Message;
        }
    }

    [RelayCommand]
    private void OpenControlPanel()
    {
        if (SelectedDriver == null) return;
        try
        {
            _stopPlayback();
            AsioManager.ShowControlPanel(SelectedDriver);
            RefreshInfo();
        }
        catch (Exception ex)
        {
            Info = "Panneau de contrôle indisponible : " + ex.Message;
        }
    }
}
