using System.Diagnostics;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using RoomEq.Audio.Routing;
using RoomEq.Core.Io;

namespace RoomEq.App.ViewModels;

/// <summary>
/// MixerViewModel — volet hérité (D25/D26) : depuis l'EQ système par APO,
/// RoomEq n'installe plus AUCUNE carte virtuelle. Ce fichier ne contient plus
/// que la MAINTENANCE du passé : suppression du pilote « RoomEq Input /
/// RoomEq Output » d'une installation v3.6/v3.7, restauration des périphériques
/// par défaut capturés par l'ancien routage, et ouverture du panneau son.
/// </summary>
public partial class MixerViewModel
{
    [ObservableProperty] private bool isInstalling;
    [ObservableProperty] private string installPhaseText = "";

    // défauts Windows mémorisés par l'ANCIEN routage (v3.6/v3.7) — servis à la
    // migration D25 (HandleLegacyVirtualDefault), puis effacés
    private string? _savedRenderId;
    private string? _savedCaptureId;

    /// <summary>Migration D25 déjà traitée (une seule tentative par session).</summary>
    private bool _legacyDefaultsHandled;

    partial void OnAutoOutputPresetChanged(bool value) => SaveConfig();

    /// <summary>Le bouton de suppression du pilote hérité est-il proposé ?</summary>
    public bool ShowLegacyDriverCleanup
        => !IsMacOS && OperatingSystem.IsWindows() && VirtualCard.PartiallyInstalled;

    /// <summary>
    /// Migration D25 (Windows, une fois) : une installation issue de v3.6/v3.7
    /// peut avoir laissé l'entrée de la carte virtuelle (« RoomEq Input »)
    /// comme sortie PAR DÉFAUT de Windows — ce serait un silence total. Si la
    /// sortie d'origine a été mémorisée par l'ancien routage (mixer.json), elle
    /// est restaurée ; sinon l'utilisateur est guidé (l'EQ remplace le routage,
    /// le pilote peut être supprimé via le bouton de maintenance).
    /// </summary>
    private void HandleLegacyVirtualDefault(AudioDeviceCatalog.DeviceSnapshot snap)
    {
        if (_legacyDefaultsHandled || IsMacOS || !OperatingSystem.IsWindows())
            return;
        if (snap.VirtualCard.CableInputId == null)
            return; // pas de carte détectée à ce passage (branchement à chaud possible)
        if (!snap.VirtualCard.IsDefaultRender)
        {
            _legacyDefaultsHandled = true; // la carte n'est pas la sortie par défaut : rien à faire
            return;
        }

        if (!string.IsNullOrEmpty(_savedRenderId))
        {
            try
            {
                if (DefaultDeviceSetter.SetDefaultRender(_savedRenderId))
                {
                    DiagnosticsLog.Info("D25 : sortie par défaut de Windows restaurée "
                        + "depuis la carte virtuelle héritée.");
                    StatusText = "Sortie par défaut de Windows restaurée sur votre périphérique "
                        + "d'origine — la carte virtuelle n'est plus utilisée (l'EQ système la remplace).";
                }
                if (!string.IsNullOrEmpty(_savedCaptureId))
                    DefaultDeviceSetter.SetDefaultCapture(_savedCaptureId);
            }
            catch
            {
                // la migration ne doit jamais interrompre le démarrage
            }
            _savedRenderId = null;
            _savedCaptureId = null;
            SaveConfig();
        }
        else
        {
            StatusText = "« " + snap.VirtualCard.CableInputName
                + " » est encore la sortie par défaut de Windows mais RoomEq ne l'utilise plus : "
                + "choisissez vos enceintes ou votre casque dans la barre du haut (ou le panneau son), "
                + "puis supprimez l'ancien pilote ci-dessous.";
        }
        _legacyDefaultsHandled = true;
    }

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
