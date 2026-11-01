using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using RoomEq.Audio.Routing;

namespace RoomEq.App.ViewModels;

/// <summary>
/// MixerViewModel — volume WINDOWS de la carte physique d'écoute (celle de la
/// carte virtuelle n'a aucun effet), synchronisation des deux curseurs et
/// des raccourcis clavier — spec I-APP-03 v3 (décomposition v3).
/// D26 : le fader master interne disparaît avec la console — reste le pilotage
/// du volume SYSTÈME de la sortie d'écoute (barre du haut), seul volume audible.
/// </summary>
public partial class MixerViewModel
{
    // volume WINDOWS du périphérique physique qui rend le son — piloté aussi au
    // clavier (Ctrl+Alt+↑ / ↓ / M)
    [ObservableProperty] private double routedVolume = -1;
    [ObservableProperty] private bool routedMute;
    [ObservableProperty] private string routedVolumeText = "—";

    /// <summary>Le volume de la sortie physique est-il accessible ?</summary>
    public bool HasRoutedVolume => _endpointVolume.Available;

    /// <summary>Nom du périphérique dont on règle le volume.</summary>
    public string RoutedVolumeTarget => _endpointVolume.TargetName ?? SelectedOutput?.DisplayName ?? "";

    /// <summary>Signalé à chaque changement de volume (affichage OSD plein écran).</summary>
    public event Action<string>? RoutedVolumeFeedback;

    private void RefreshRoutedVolumeDisplay()
    {
        double v = _endpointVolume.VolumeScalar;
        _syncingRoutedVolume = true; // le binding TwoWay renverrait sinon la valeur dans OnRoutedVolumeChanged
        try
        {
            RoutedVolume = v;
            RoutedMute = _endpointVolume.Muted;
            RoutedVolumeText = v < 0
                ? "—"
                : (RoutedMute ? "Muet" : $"{Math.Round(v * 100)} %");
        }
        finally { _syncingRoutedVolume = false; }
        if (VolumeSyncPolicy.IsUsable(v)) _routedVolumeReady = true;
        _lastEndpointSeen = v;
    }

    /// <summary>
    /// Le curseur « Volume » de la barre supérieure a bougé : la valeur devient
    /// la consigne de la carte physique, PUIS les affichages sont recalés —
    /// les deux curseurs restent identiques quel que soit celui que l'on déplace.
    /// </summary>
    partial void OnRoutedVolumeChanged(double value)
    {
        // écriture programmatique (RefreshRoutedVolumeDisplay) : rien à propager ;
        // premier peuplement du binding : RoutedVolume démarre à −1 et le Slider
        // le contraint à 0 dès l'affichage — ne PAS écrire 0 dans la carte !
        if (_syncingRoutedVolume || !_routedVolumeReady) return;
        if (!VolumeSyncPolicy.IsUsable(value)) return;
        if (!_endpointVolume.Available) return;

        double target = VolumeSyncPolicy.ClampToEndpointRange(value);
        if (VolumeSyncPolicy.ShouldWriteToEndpoint(_endpointVolume.VolumeScalar, target))
            _endpointVolume.SetVolumeScalar(target);
        RefreshRoutedVolumeDisplay();
        SyncTopBarFromEndpoint();
    }

    /// <summary>Recale le curseur de la barre supérieure sur le volume réel de la carte
    /// (la décision finale — déjà synchronisé ou non — appartient au ViewModel principal).</summary>
    private void SyncTopBarFromEndpoint()
    {
        double v = _endpointVolume.VolumeScalar;
        if (VolumeSyncPolicy.IsUsable(v)) _syncTopBarVolume?.Invoke(v);
    }

    private void NudgeRoutedVolume(int direction)
    {
        if (!_endpointVolume.Available)
        {
            StatusText = "Volume indisponible sur cette sortie (pilote ASIO ?).";
            return;
        }
        double v = _endpointVolume.VolumeScalar;
        if (v < 0) return;
        _endpointVolume.SetVolumeScalar(v + direction * 0.05);
        RefreshRoutedVolumeDisplay();
        SyncTopBarFromEndpoint(); // le curseur de la barre du haut suit les raccourcis clavier
        FeedbackRoutedVolume();
    }

    [RelayCommand] private void RoutedVolumeUp() => NudgeRoutedVolume(+1);
    [RelayCommand] private void RoutedVolumeDown() => NudgeRoutedVolume(-1);

    /// <summary>
    /// Applique le curseur « Volume » de la barre supérieure au périphérique
    /// physique d'écoute : de 0 à 100 % règle le volume Windows de la carte (le
    /// son du système — traité par l'EQ système — ne passe PAS par le lecteur
    /// interne, d'où ce pont) ; au-delà de 100 % le volume Windows reste au
    /// maximum et seul le gain du lecteur interne monte.
    /// Sans effet si la sortie n'expose pas de volume Windows (ASIO).
    /// </summary>
    public void ApplyTopBarVolume(double value)
    {
        if (!_endpointVolume.Available) return;
        double target = EndpointVolumeController.SliderToEndpoint(value);
        if (VolumeSyncPolicy.ShouldWriteToEndpoint(_endpointVolume.VolumeScalar, target))
            _endpointVolume.SetVolumeScalar(VolumeSyncPolicy.ClampToEndpointRange(target));
        RefreshRoutedVolumeDisplay();
    }

    [RelayCommand]
    private void ToggleRoutedMute()
    {
        bool? state = _endpointVolume.ToggleMute();
        if (state == null)
        {
            StatusText = "Sourdine indisponible sur cette sortie.";
            return;
        }
        RefreshRoutedVolumeDisplay();
        FeedbackRoutedVolume();
    }

    private void FeedbackRoutedVolume()
    {
        string text = _endpointVolume.Available
            ? $"Volume — {RoutedVolumeTarget} : {RoutedVolumeText}"
            : "Volume non disponible pour cette sortie.";
        RoutedVolumeFeedback?.Invoke(text);
    }

    /// <summary>
    /// Le volume de la carte peut changer HORS de l'application (mixeur Windows,
    /// touches du casque, autre logiciel) : en le sondant à chaque battement, les
    /// deux curseurs restent fidèles au réglage réel, quel que soit l'ordre et
    /// l'origine des modifications.
    /// </summary>
    private void PollExternalEndpointVolume()
    {
        if (!_endpointVolume.Available)
        {
            _lastEndpointSeen = -1;
            return;
        }
        double v = _endpointVolume.VolumeScalar;
        if (!VolumeSyncPolicy.HasChangedSinceLastSeen(_lastEndpointSeen, v)) return;
        RefreshRoutedVolumeDisplay();
        SyncTopBarFromEndpoint();
    }
}
