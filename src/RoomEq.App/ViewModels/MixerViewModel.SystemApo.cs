using System.Collections.ObjectModel;
using Avalonia.Threading;
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
/// les mesures. C'est LE mécanisme de RoomEq sous Windows : « j'installe
/// l'appli, j'active ma sortie, tout le son passe dans l'EQ ».
/// D26 : le VERDICT de chaque action (succès comme échec — élévation refusée,
/// sortie sans slot d'effets, périphérique débranché…) reste affiché DANS le
/// panneau jusqu'à l'action suivante — il n'est plus relégué dans la barre
/// d'état du bas puis écrasé par le rafraîchissement, ce qui rendait les
/// échecs d'activation invisibles (« le bouton ne s'active jamais »).
/// </summary>
public partial class MixerViewModel
{
    /// <summary>EQ système partagé (créé au constructeur, câblé par le shell).</summary>
    public WindowsApoSystem SystemApo { get; private set; } = null!;

    /// <summary>Lignes du panneau (une par sortie Windows active).</summary>
    public ObservableCollection<ApoEndpointViewModel> ApoEndpoints { get; } = new();

    [ObservableProperty] private string systemApoStatusText = "";
    [ObservableProperty] private bool isSystemApoBusy;

    /// <summary>Verdict de la dernière action : reste affiché dans le panneau
    /// (D26) — c'est lui qui explique un bouton qui « ne s'active pas ».</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasSystemApoVerdict))]
    private string systemApoVerdictText = "";

    /// <summary>Un verdict est-il affiché ?</summary>
    public bool HasSystemApoVerdict => SystemApoVerdictText.Length > 0;

    /// <summary>Le verdict est-il un échec (couleur d'alerte) ?</summary>
    [ObservableProperty] private bool systemApoVerdictIsError;

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
        OnPropertyChanged(nameof(SystemApoSupported));

        var load = SystemApo.LastApoLoadUtc;
        SystemApoStatusText = !WindowsApoSystem.IsPlatformSupported
            ? "EQ système : fonctionnalité Windows."
            : !SystemApo.IsReady
                ? "EQ système indisponible : DLL APO absentes du dossier d'installation (réinstallez RoomEq)."
                : endpoints.Count == 0
                    ? "Aucune sortie audio détectée."
                    : AnySystemApoEnabled
                        ? ApoChainText.Compose(new ApoChainState(
                            SystemApo.Config.Refresh(),
                            SystemApo.LastCurveWriteError,
                            SystemApo.ReadApoLogTail(),
                            load))
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
            "Activation de « " + endpoint.Name + " »");
    }

    [RelayCommand]
    private async Task SystemApoDisableAsync(ApoEndpointViewModel? endpoint)
    {
        if (endpoint == null || IsSystemApoBusy) return;
        await RunSystemApoAsync(
            () => SystemApo.DisableAsync(endpoint.Key),
            "Désactivation de « " + endpoint.Name + " »");
    }

    [RelayCommand]
    private async Task SystemApoRepairAsync()
    {
        if (IsSystemApoBusy) return;
        await RunSystemApoAsync(
            () => SystemApo.RepairAsync(),
            "Réparation des sorties activées");
    }

    [RelayCommand]
    private async Task SystemApoUninstallAsync()
    {
        if (IsSystemApoBusy) return;
        await RunSystemApoAsync(
            () => SystemApo.UninstallAsync(),
            "Suppression de l'EQ système");
    }

    private async Task RunSystemApoAsync(Func<Task<ApoInstallResult>> action, string actionLabel)
    {
        IsSystemApoBusy = true;
        SystemApoStatusText = actionLabel + "… (acceptez l'invite administrateur)";
        try
        {
            var result = await action().ConfigureAwait(true);
            // D26 : le verdict — y compris les échecs — reste DANS le panneau :
            // « élévation refusée », « n'expose pas de slot d'effets », etc.
            // doivent être lisibles là où l'utilisateur a cliqué.
            SystemApoVerdictText = (result.Success ? "✓ " : "✗ ")
                + actionLabel + " — " + result.Message;
            SystemApoVerdictIsError = !result.Success;
            StatusText = result.Message;
            RefreshSystemApoCore();
            ScheduleLateSystemApoRefresh();
            if (result.Success)
                RefreshVolumeTarget(); // v3.9.5 : le service audio vient d'être redémarré —
                                       // les objets COM du volume sont morts, re-acquisition immédiate
        }
        finally
        {
            IsSystemApoBusy = false;
        }
    }

    /// <summary>
    /// Second rafraîchissement différé (D26) : l'activation redémarre le
    /// service audio, et l'énumération immédiate des sorties peut tomber sur
    /// un service encore en train de reconstruire ses endpoints (liste vide ou
    /// incomplète — le bouton paraît alors « non activé » alors que tout a
    /// réussi). 1,5 s plus tard, la liste est complète.
    /// </summary>
    private void ScheduleLateSystemApoRefresh()
    {
        Dispatcher.UIThread.Post(async () =>
        {
            try
            {
                await Task.Delay(1500).ConfigureAwait(true);
                RefreshSystemApoCore();
                RefreshVolumeTarget(); // v3.9.5 : seconde chance après le retour du service audio
            }
            catch
            {
                // un rafraîchissement différé ne doit jamais lever
            }
        }, DispatcherPriority.Background);
    }
}
