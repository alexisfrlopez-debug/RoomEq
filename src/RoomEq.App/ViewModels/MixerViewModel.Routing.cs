using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using RoomEq.Audio.Mac;
using RoomEq.Audio.Routing;

namespace RoomEq.App.ViewModels;

/// <summary>
/// MixerViewModel — chaîne de routage Windows → carte virtuelle → EQ → sortie
/// physique, bascule/restauration des périphériques par défaut, démarrage
/// automatique et tranches par défaut — spec I-APP-03 (décomposition v3).
/// </summary>
public partial class MixerViewModel
{
    // défauts Windows mémorisés avant bascule (restauration à l'arrêt du routage)
    private string? _savedRenderId;
    private string? _savedCaptureId;

    [ObservableProperty] private bool autoStart;
    [ObservableProperty] private bool autoRouteOnInstall = true;
    [ObservableProperty] private bool switchDefaultCapture = true;
    [ObservableProperty] private string outputInfoText = "";

    [RelayCommand]
    private void ToggleRouting()
    {
        if (IsRunning)
        {
            Mixer.Stop();
            TryRestoreDefaults();
            RefreshTransportState();
            RefreshDevices();
            StatusText = IsMacOS
                ? "Routage arrêté."
                : "Routage arrêté — périphériques par défaut de Windows restaurés.";
            return;
        }

        if (StartRoutingChain())
            StatusText = IsMacOS
                ? $"Routage actif : macOS → « RoomEq Virtual » → EQ → « {SelectedOutput?.DisplayName} » "
                  + $"({Mixer.OutputLatencyMs + 20} ms approx.). Sélectionnez « RoomEq Virtual » comme sortie macOS si ce n'est pas déjà fait."
                : $"Routage actif : Windows → carte virtuelle → EQ → « {SelectedOutput?.DisplayName} » "
                  + $"({Mixer.OutputLatencyMs + 20} ms approx.). Les périphériques par défaut d'origine "
                  + "sont restaurés à l'arrêt.";
    }

    /// <summary>
    /// Démarre la chaîne complète : bascule des périphériques par défaut de Windows
    /// vers la carte virtuelle (sortie, et entrée si l'option est active), puis
    /// captures + bus master. Retourne false si le démarrage a échoué.
    /// </summary>
    private bool StartRoutingChain()
    {
        try
        {
            SyncMasterEqFromMain();
            TrySwitchDefaultsForRouting();
            Mixer.Start();
            RefreshTransportState();
            RefreshDevices();
            return true;
        }
        catch (Exception ex)
        {
            StatusText = "Routage impossible : " + ex.Message;
            RefreshTransportState();
            return false;
        }
    }

    /// <summary>
    /// Bascule Windows vers la carte virtuelle : la sortie par défaut passe sur
    /// l'entrée de la carte (« RoomEq Input ») et, si l'option est active,
    /// l'entrée par défaut passe sur la sortie de la carte (« micro virtuel »).
    /// Les défauts d'origine sont mémorisés une seule fois pour être restaurés
    /// à l'arrêt du routage.
    /// </summary>
    private void TrySwitchDefaultsForRouting()
    {
        if (IsMacOS)
        {
            // macOS n'expose AUCUNE API publique pour changer la sortie par défaut :
            // l'utilisateur choisit « RoomEq Virtual » dans Réglages Système (le
            // sondage affiche en permanence si c'est déjà fait).
            return;
        }

        try
        {
            var st = _catalog.ProbeVirtualCard(SelectedProvider);
            if (!st.Installed || st.CableInputId == null)
                return; // boucle WASAPI ou carte absente : rien à basculer

            if (string.IsNullOrEmpty(_savedRenderId))
            {
                _savedRenderId = st.DefaultRenderId;
                _savedCaptureId = _catalog.GetDefaultCaptureId();
                SaveConfig(); // mémorisé aussi sur disque (survit à un plantage)
            }

            if (!string.Equals(st.DefaultRenderId, st.CableInputId, StringComparison.OrdinalIgnoreCase))
            {
                if (DefaultDeviceSetter.SetDefaultRender(st.CableInputId))
                    StatusText = "Sortie par défaut de Windows → « " + st.CableInputName + " ».";
                else
                    StatusText = "Bascule automatique refusée par Windows : utilisez « Définir par défaut » ou le panneau son.";
            }

            if (SwitchDefaultCapture && st.CableOutputId != null)
            {
                string cur = _catalog.GetDefaultCaptureId() ?? "";
                if (!string.Equals(cur, st.CableOutputId, StringComparison.OrdinalIgnoreCase))
                    DefaultDeviceSetter.SetDefaultCapture(st.CableOutputId);
            }
        }
        catch
        {
            // la bascule des défauts ne doit jamais empêcher le démarrage du routage
        }
    }

    /// <summary>Restaure les périphériques par défaut mémorisés avant la bascule.</summary>
    private void TryRestoreDefaults()
    {
        try
        {
            if (!string.IsNullOrEmpty(_savedRenderId))
                DefaultDeviceSetter.SetDefaultRender(_savedRenderId);
            if (!string.IsNullOrEmpty(_savedCaptureId))
                DefaultDeviceSetter.SetDefaultCapture(_savedCaptureId);
        }
        catch
        {
            // la restauration ne doit jamais lever
        }
        finally
        {
            _savedRenderId = null;
            _savedCaptureId = null;
            SaveConfig();
        }
    }

    /// <summary>Démarre automatiquement le routage si configuré (appelé au chargement de la fenêtre).</summary>
    public void AutoStartIfConfigured()
    {
        if (!AutoStart || IsRunning) return;
        if (IsMacOS)
        {
            if (MacAudioBridge.PluginInstalled)
                Dispatcher.UIThread.Post(ToggleRouting);
            return;
        }
        var st = _catalog.ProbeVirtualCard(SelectedProvider);
        if (st.CableOutputFound || _catalog.EnumerateLoopbackSources().Count > 0)
        {
            Dispatcher.UIThread.Post(ToggleRouting);
        }
    }

    /// <summary>
    /// Garantit la présence de la piste captant la sortie de la carte virtuelle
    /// (« RoomEq Output ») — c'est elle qui alimente l'égaliseur.
    /// </summary>
    private void EnsureCableTrack()
    {
        if (IsMacOS)
        {
            bool macTrackExists = Channels.Any(c => c.Channel.Source.Kind == TrackSourceKind.MacVirtualDevice);
            if (!macTrackExists)
                AddTrack(TrackSource.MacVirtualSource());
            return;
        }

        if (VirtualCard.CableOutputId == null) return;
        bool exists = Channels.Any(c => c.Channel.Source.Kind == TrackSourceKind.Capture
            && string.Equals(c.Channel.Source.DeviceId, VirtualCard.CableOutputId, StringComparison.OrdinalIgnoreCase));
        if (!exists)
        {
            AddTrack(TrackSource.CaptureDevice(VirtualCard.CableOutputId,
                (VirtualCard.CableOutputName ?? "Sortie virtuelle") + " — Windows"));
        }
    }

    /// <summary>Ajoute les tranches par défaut d'une session : Windows/macOS, lecteur, générateur.</summary>
    private void AddDefaultTracks()
    {
        if (IsMacOS)
        {
            // Son macOS : via le périphérique virtuel (greffon), si installé
            if (MacAudioBridge.PluginInstalled)
                AddTrack(TrackSource.MacVirtualSource());

            AddTrack(TrackSource.InternalPlayer());
            AddTrack(TrackSource.InternalGenerator());
            return;
        }

        // Son Windows : via le câble virtuel du protocole choisi, sinon loopback de la sortie par défaut
        if (VirtualCard.CableOutputFound && VirtualCard.CableOutputId != null)
        {
            AddTrack(TrackSource.CaptureDevice(VirtualCard.CableOutputId,
                (VirtualCard.CableOutputName ?? "Sortie virtuelle") + " — Windows"));
        }
        else if (VirtualCard.DefaultRenderId != null)
        {
            AddTrack(TrackSource.LoopbackDevice(VirtualCard.DefaultRenderId,
                (VirtualCard.DefaultRenderName ?? "Sortie Windows") + " — boucle"));
        }

        // Lecteur interne
        AddTrack(TrackSource.InternalPlayer());

        // Première entrée non virtuelle comme Micro
        var micro = _catalog.EnumerateCaptureSources()
            .FirstOrDefault(s => !AudioDeviceCatalog.IsVirtualCapture(s.Name));
        if (micro != null) AddTrack(micro);

        // Générateur
        AddTrack(TrackSource.InternalGenerator());
    }
}
