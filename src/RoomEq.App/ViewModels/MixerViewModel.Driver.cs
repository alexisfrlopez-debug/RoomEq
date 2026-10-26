using System.Diagnostics;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using RoomEq.Audio.Routing;

namespace RoomEq.App.ViewModels;

/// <summary>
/// MixerViewModel — carte son virtuelle : sondage, installation /
/// désinstallation du pilote livré avec l'application, définition par défaut
/// et ouverture du panneau son — spec I-APP-03 (décomposition v3).
/// </summary>
public partial class MixerViewModel
{
    [ObservableProperty] private bool isInstalling;
    [ObservableProperty] private string installPhaseText = "";
    [ObservableProperty] private double installPercent;
    [ObservableProperty] private bool installIndeterminate = true;

    partial void OnAutoStartChanged(bool value) => SaveConfig();
    partial void OnAutoOutputPresetChanged(bool value) => SaveConfig();
    partial void OnAutoRouteOnInstallChanged(bool value) => SaveConfig();
    partial void OnSwitchDefaultCaptureChanged(bool value) => SaveConfig();

    partial void OnSelectedProviderChanged(IVirtualAudioProvider value)
    {
        OnPropertyChanged(nameof(CanAutoInstall));
        OnPropertyChanged(nameof(DriverPackagePresent));
        OnPropertyChanged(nameof(HasInstallOption));
        OnPropertyChanged(nameof(ProviderIsPreinstalled));
        if (!_initializing)
        {
            RefreshDevices();
            SaveConfig();
        }
    }

    // ================================================================ carte virtuelle (protocoles)

    /// <summary>Le paquet pilote RoomEq est-il présent à côté de l'application ?</summary>
    public bool DriverPackagePresent => DriverSetupManager.PackagePresent(
        AppContext.BaseDirectory, SelectedProvider.Installer);

    /// <summary>Une installation est-elle nécessaire ? (fournisseurs préinstallés exclus)</summary>
    public bool CanAutoInstall => !SelectedProvider.IsPreinstalled;

    /// <summary>Option d'installation disponible (fournisseur non préinstallé, hors macOS).</summary>
    public bool HasInstallOption => CanAutoInstall;

    /// <summary>Boutons d'installation/désinstallation du pilote visibles (Windows uniquement).</summary>
    public bool ShowDriverInstallUi => !IsMacOS && HasInstallOption;

    /// <summary>Ce protocole ne nécessite aucune installation ?</summary>
    public bool ProviderIsPreinstalled => SelectedProvider.IsPreinstalled;

    /// <summary>Définit le périphérique render du protocole choisi comme sortie par défaut de Windows (API PolicyConfig).</summary>
    [RelayCommand]
    private void SetVirtualDefault()
    {
        if (IsMacOS)
        {
            StatusText = "macOS interdit la bascule automatique de la sortie par défaut — choisissez « RoomEq Virtual » dans Réglages Système › Son › Sortie.";
            OpenSoundPanel();
            return;
        }

        if (VirtualCard.CableInputId == null)
        {
            StatusText = SelectedProvider.DisplayName + " n'est pas détecté : installez-le d'abord via « Installer le pilote RoomEq ».";
            return;
        }
        bool ok = DefaultDeviceSetter.SetDefaultRender(VirtualCard.CableInputId);
        StatusText = ok
            ? "« " + VirtualCard.CableInputName + " » est maintenant la sortie par défaut de Windows."
            : "Définition automatique refusée par Windows : passez par le panneau son (bouton ci-contre).";
        if (!ok) OpenSoundPanel();
        RefreshDevices();
    }

    /// <summary>
    /// Installe le pilote de la carte virtuelle RoomEq livré avec l'application
    /// (aucun téléchargement), puis attend l'apparition des périphériques
    /// « RoomEq Input / RoomEq Output ».
    /// </summary>
    [RelayCommand]
    private async Task InstallVirtualCardAsync(CancellationToken ct)
    {
        if (IsMacOS)
        {
            StatusText = "Le greffon macOS s'installe avec le paquet RoomEq (.pkg) — relancez l'installateur si le périphérique « RoomEq Virtual » manque.";
            return;
        }

        var provider = SelectedProvider;
        if (provider.IsPreinstalled)
        {
            StatusText = "Aucune installation nécessaire pour ce protocole (boucle WASAPI native).";
            return;
        }
        if (!DriverPackagePresent)
        {
            StatusText = "Paquet pilote introuvable : réinstallez RoomEq avec l'installateur (le dossier « driver » est copié à côté de l'application).";
            return;
        }

        IsInstalling = true;
        InstallIndeterminate = true;
        InstallPhaseText = "Préparation de l'installation…";
        try
        {
            var progress = new Progress<InstallProgress>(p =>
            {
                InstallPhaseText = p.Message;
                if (p.Percent is { } v)
                {
                    InstallIndeterminate = false;
                    InstallPercent = v;
                }
                else
                {
                    InstallIndeterminate = true;
                }
            });
            var setup = new DriverSetupManager(progress);
            var probe = new Func<bool>(() => _catalog.ProbeVirtualCard(provider).Installed);
            var result = await setup.InstallAsync(provider.Installer, AppContext.BaseDirectory, probe, ct);
            StatusText = result.Message;
            if (result.Success)
            {
                RefreshDevices();
                if (VirtualCard.Installed)
                {
                    EnsureCableTrack();

                    if (AutoRouteOnInstall)
                    {
                        if (!IsRunning) StartRoutingChain();
                        else if (!string.IsNullOrEmpty(VirtualCard.CableInputId) && !VirtualCard.IsDefaultRender)
                            DefaultDeviceSetter.SetDefaultRender(VirtualCard.CableInputId);

                        RefreshDevices();
                        StatusText = result.Message + " Chaîne active : Windows → « "
                                   + (VirtualCard.CableInputName ?? "carte virtuelle") + " » → EQ → « "
                                   + (SelectedOutput?.DisplayName ?? "?") + " ».";
                    }
                    else if (!IsRunning)
                    {
                        StatusText += " Cliquez « Router le son vers l'EQ » pour activer le traitement.";
                    }
                }
            }
        }
        finally
        {
            IsInstalling = false;
            InstallIndeterminate = true;
        }
    }

    /// <summary>Désinstalle le pilote de la carte virtuelle RoomEq (maintenance).</summary>
    [RelayCommand]
    private async Task UninstallDriverAsync(CancellationToken ct)
    {
        if (IsMacOS)
        {
            StatusText = "Supprimez /Library/Audio/Plug-Ins/HAL/RoomEqHAL.driver puis redémarrez coreaudiod (le désinstalleur du paquet .pkg le fait).";
            return;
        }

        if (SelectedProvider.IsPreinstalled) return;
        IsInstalling = true;
        InstallIndeterminate = true;
        InstallPhaseText = "Désinstallation du pilote…";
        try
        {
            var setup = new DriverSetupManager(new Progress<InstallProgress>(p => InstallPhaseText = p.Message));
            var result = await setup.UninstallAsync(SelectedProvider.Installer, ct);
            StatusText = result.Message;
            RefreshDevices();
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
