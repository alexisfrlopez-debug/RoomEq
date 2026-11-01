using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using RoomEq.Audio.SystemApo;

namespace RoomEq.App.ViewModels;

/// <summary>
/// Une ligne du panneau « EQ système » : une sortie Windows, son état APO et
/// ce que l'utilisateur peut y faire (fichier partagé avec la vue).
/// </summary>
public sealed class ApoEndpointViewModel
{
    public required string Key { get; init; }
    public required string Name { get; init; }
    public required bool Enabled { get; init; }
    public required bool Forbidden { get; init; }
    public required bool IsDefault { get; init; }

    public string Display => Name + (IsDefault ? "  ·  sortie par défaut" : "");

    public string State => Forbidden
        ? "câble virtuel — interdit"
        : Enabled ? "● EQ système actif" : "○ non traité";
}

/// <summary>
/// MixerViewModel — panneau « EQ système (APO) » de l'onglet Mixage (spec
/// I-AUD-09, lot 3) : liste des sorties Windows avec état d'activation,
/// activation/désactivation par sortie (une invite administrateur, effet
/// immédiat et réversible), réparation après mise à jour de pilote, courbe
/// poussée en continu depuis l'onglet Égaliseur, bypass automatique pendant
/// les mesures. C'est le remplacement user-friendly de la carte virtuelle :
/// « j'installe l'appli, j'active ma sortie, tout le son passe dans l'EQ ».
/// </summary>
public partial class MixerViewModel
{
    /// <summary>EQ système partagé (créé au constructeur, câblé par le shell).</summary>
    public WindowsApoSystem SystemApo { get; private set; } = null!;

    /// <summary>Lignes du panneau (une par sortie Windows active).</summary>
    public ObservableCollection<ApoEndpointViewModel> ApoEndpoints { get; } = new();

    [ObservableProperty] private string systemApoStatusText = "";
    [ObservableProperty] private bool isSystemApoBusy;

    /// <summary>Le panneau est-il utilisable sur cette machine ?</summary>
    public bool SystemApoSupported => SystemApo.IsReady;

    /// <summary>Au moins une sortie a l'EQ système actif.</summary>
    public bool AnySystemApoEnabled => SystemApo.AnyEnabled;

    // ================================================================ liste

    /// <summary>Reconstruit les lignes du panneau (thread UI).</summary>
    public void RefreshSystemApoCore()
    {
        var endpoints = SystemApo.EnumerateEndpoints();

        ApoEndpoints.Clear();
        foreach (var e in endpoints)
            ApoEndpoints.Add(new ApoEndpointViewModel
            {
                Key = e.Key,
                Name = e.FriendlyName,
                Enabled = e.Enabled,
                Forbidden = e.Forbidden,
                IsDefault = e.IsDefault,
            });
        OnPropertyChanged(nameof(AnySystemApoEnabled));

        var load = SystemApo.LastApoLoadUtc;
        SystemApoStatusText = !WindowsApoSystem.IsPlatformSupported
            ? "EQ système : fonctionnalité Windows."
            : !SystemApo.IsReady
                ? "EQ système indisponible : DLL APO absentes du dossier d'installation (réinstallez RoomEq)."
                : endpoints.Count == 0
                    ? "Aucune sortie audio détectée."
                    : AnySystemApoEnabled
                        ? "EQ système actif — la courbe de l'onglet Égaliseur s'applique à tout ce qui sort par les sorties marquées ●"
                          + (load is { } t ? $" (APO chargé : {t:HH:mm:ss} UTC)" : "")
                        : "Activez une ou plusieurs sorties : tout leur son traversera l'égaliseur (invite administrateur, réversible).";
    }

    [RelayCommand]
    private void RefreshSystemApo() => RefreshSystemApoCore();

    // ================================================================ actions

    [RelayCommand]
    private async Task SystemApoEnableAsync(ApoEndpointViewModel? endpoint)
    {
        if (endpoint == null || IsSystemApoBusy) return;
        await RunSystemApoAsync(
            () => SystemApo.EnableAsync(endpoint.Key),
            "Activation de « " + endpoint.Name + " »…");
    }

    [RelayCommand]
    private async Task SystemApoDisableAsync(ApoEndpointViewModel? endpoint)
    {
        if (endpoint == null || IsSystemApoBusy) return;
        await RunSystemApoAsync(
            () => SystemApo.DisableAsync(endpoint.Key),
            "Désactivation de « " + endpoint.Name + " »…");
    }

    [RelayCommand]
    private async Task SystemApoRepairAsync()
    {
        if (IsSystemApoBusy) return;
        await RunSystemApoAsync(
            () => SystemApo.RepairAsync(),
            "Réparation des sorties activées…");
    }

    [RelayCommand]
    private async Task SystemApoUninstallAsync()
    {
        if (IsSystemApoBusy) return;
        await RunSystemApoAsync(
            () => SystemApo.UninstallAsync(),
            "Suppression de l'EQ système…");
    }

    private async Task RunSystemApoAsync(Func<Task<ApoInstallResult>> action, string busyText)
    {
        IsSystemApoBusy = true;
        SystemApoStatusText = busyText + " (acceptez l'invite administrateur)";
        try
        {
            var result = await action().ConfigureAwait(true);
            StatusText = result.Message;
            RefreshSystemApoCore();
        }
        finally
        {
            IsSystemApoBusy = false;
        }
    }
}
