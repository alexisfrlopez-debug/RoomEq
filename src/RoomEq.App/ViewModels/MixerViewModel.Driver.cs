using System.Diagnostics;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using RoomEq.Audio.Routing;

namespace RoomEq.App.ViewModels;

/// <summary>
/// MixerViewModel — pilote de carte virtuelle, volet hérité (D25, v3.8.0) :
/// l'INSTALLATION a été retirée avec le routage Windows (l'EQ système APO
/// traite le son sans carte virtuelle). Reste la SUPPRESSION de maintenance :
/// une machine ayant connu v3.6/v3.7 peut encore porter le pilote
/// « RoomEq Input / RoomEq Output » — le proposer à retirer évite le périphérique
/// fantôme dans le panneau son (et le risque de le choisir par erreur).
/// Ouverture du panneau son conservée — spec I-APP-03 v2.
/// </summary>
public partial class MixerViewModel
{
    [ObservableProperty] private bool isInstalling;
    [ObservableProperty] private string installPhaseText = "";

    partial void OnAutoStartChanged(bool value) => SaveConfig();
    partial void OnAutoOutputPresetChanged(bool value) => SaveConfig();

    // ================================================================ pilote hérité

    /// <summary>
    /// Supprime le pilote de la carte virtuelle laissé par une ancienne version
    /// (maintenance D25) : le son du système ne l'utilise plus, l'EQ système
    /// fonctionne sans pilote. Invite administrateur.
    /// </summary>
    [RelayCommand]
    private async Task UninstallDriverAsync(CancellationToken ct)
    {
        if (IsMacOS)
        {
            StatusText = "Supprimez /Library/Audio/Plug-Ins/HAL/RoomEqHAL.driver puis redémarrez coreaudiod (le désinstalleur du paquet .pkg le fait).";
            return;
        }

        IsInstalling = true;
        InstallPhaseText = "Suppression de l'ancien pilote…";
        try
        {
            var setup = new DriverSetupManager(new Progress<InstallProgress>(p => InstallPhaseText = p.Message));
            var result = await setup.UninstallAsync(SelectedProvider.Installer, ct);
            StatusText = result.Message
                + (result.Success ? " Choisissez ensuite vos enceintes/casque comme sortie par défaut si besoin." : "");
            await RefreshDevicesAsync();
        }
        finally
        {
            IsInstalling = false;
        }
    }

    [RelayCommand]
    private void OpenSoundPanel()
    {
        try
        {
            if (IsMacOS)
            {
                Process.Start(new ProcessStartInfo
                {
                    FileName = "open",
                    Arguments = "x-apple.systempreferences:com.apple.Sound-Settings.extension",
                    UseShellExecute = false,
                });
                return;
            }
            Process.Start(new ProcessStartInfo { FileName = "mmsys.cpl", UseShellExecute = true });
        }
        catch (Exception ex)
        {
            StatusText = "Ouverture des réglages son impossible : " + ex.Message;
        }
    }
}
