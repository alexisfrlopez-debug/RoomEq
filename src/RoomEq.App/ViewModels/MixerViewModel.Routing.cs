using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using RoomEq.Audio.Mac;
using RoomEq.Audio.Routing;

namespace RoomEq.App.ViewModels;

/// <summary>
/// MixerViewModel — démarrage/arrêt de la console et migration D25 (v3.8.0).
/// Windows : le bouton démarre la CONSOLE interne (lecteur, générateur, pont
/// DAW ASIO → bus master → sortie choisie) — le son du système, lui, est
/// traité par l'EQ système APO directement dans le moteur audio Windows ;
/// la carte virtuelle, la bascule des périphériques par défaut et la piste
/// « câble » ont été retirées avec les tranches « entrée/sortie » (conflits
/// de double correction, confusions, protections anti-boucle).
/// macOS : pas d'EQ système — le routage par périphérique virtuel
/// (« RoomEq Virtual ») reste la voie principale, inchangée — spec I-APP-03 v2.
/// </summary>
public partial class MixerViewModel
{
    // défauts Windows mémorisés par l'ANCIEN routage (v3.6/v3.7) — servis à la
    // migration D25 (HandleLegacyVirtualDefault), puis effacés
    private string? _savedRenderId;
    private string? _savedCaptureId;

    [ObservableProperty] private bool autoStart;
    [ObservableProperty] private string outputInfoText = "";

    /// <summary>Libellé du bouton de démarrage (console Windows / routage macOS).</summary>
    public string RoutingStartLabel => IsMacOS ? "Router le son vers l'EQ" : "Démarrer la console";

    /// <summary>Libellé du bouton d'arrêt.</summary>
    public string RoutingStopLabel => IsMacOS ? "■ Arrêter le routage" : "■ Arrêter la console";

    /// <summary>Titre de l'aide de la barre de console/routage.</summary>
    public string RoutingHelpTitle => IsMacOS ? "Routage du son macOS" : "Console & EQ système";

    /// <summary>Texte d'aide de la barre de console/routage.</summary>
    public string RoutingHelpText => IsMacOS
        ? "Chaîne complète : macOS joue dans « RoomEq Virtual » → RoomEq capte le périphérique → égaliseur → rendu sur la sortie physique choisie (en haut à droite du MASTER).\n\nChoisissez « RoomEq Virtual » comme sortie macOS (Réglages Système › Son) — macOS interdit la bascule automatique — puis cliquez « Router le son vers l'EQ ».\n\nAnti-boucle : la sortie master ne peut pas être le périphérique virtuel lui-même.\n\nLe fader MASTER règle le niveau interne de RoomEq — le VOLUME FINAL des enceintes se règle avec Ctrl+Alt+↑ / ↓ (voir « Volume carte physique »)."
        : "Deux chemins, un seul égaliseur :\n\n• EQ SYSTÈME (recommandé, en haut) : la courbe s'applique DANS le moteur audio Windows — tout le son, latence nulle, aucun pilote.\n\n• CONSOLE (ce bouton) : pour les sources INTERNES de RoomEq — lecteur de fichiers, générateur de test, et DAW via le pont ASIO (« RoomEq ASIO »). Elles traversent l'égaliseur de piste, puis le bus master, puis la sortie choisie.\n\nSi l'EQ système est actif sur la sortie master, le bus master contourne son propre EQ (la courbe est déjà appliquée par Windows — sinon elle serait doublée).\n\nLe fader MASTER règle le niveau interne — le VOLUME FINAL des enceintes se règle avec Ctrl+Alt+↑ / ↓ (voir « Volume carte physique »).";

    [RelayCommand]
    private void ToggleRouting()
    {
        if (IsRunning)
        {
            Mixer.Stop();
            RefreshTransportState();
            RefreshDevices();
            StatusText = IsMacOS ? "Routage arrêté." : "Console arrêtée.";
            return;
        }

        if (StartRoutingChain())
            StatusText = IsMacOS
                ? $"Routage actif : macOS → « RoomEq Virtual » → EQ → « {SelectedOutput?.DisplayName} » "
                  + $"({Mixer.OutputLatencyMs + 20} ms approx.). Sélectionnez « RoomEq Virtual » comme sortie macOS si ce n'est pas déjà fait."
                : $"Console démarrée : lecteur, générateur et DAW traversent l'égaliseur master "
                  + $"→ « {SelectedOutput?.DisplayName} ». Le son du système, lui, passe par l'EQ système (panneau ci-dessus).";
    }

    /// <summary>
    /// Démarre la console : synchronisation de l'EQ master puis bus master vers
    /// la sortie choisie. Retourne false si le démarrage a échoué. (D25 : plus
    /// AUCUNE bascule de périphérique par défaut — l'EQ système APO traite le
    /// son du système sans carte virtuelle.)
    /// </summary>
    private bool StartRoutingChain()
    {
        try
        {
            SyncMasterEqFromMain();
            Mixer.Start();
            RefreshTransportState();
            RefreshDevices();
            return true;
        }
        catch (Exception ex)
        {
            StatusText = (IsMacOS ? "Routage impossible : " : "Démarrage de la console impossible : ") + ex.Message;
            RefreshTransportState();
            return false;
        }
    }

    /// <summary>Démarre automatiquement la console si configuré (appelé au chargement de la fenêtre).</summary>
    public void AutoStartIfConfigured()
    {
        if (!AutoStart || IsRunning) return;
        if (IsMacOS)
        {
            if (MacAudioBridge.PluginInstalled)
                Dispatcher.UIThread.Post(ToggleRouting);
            return;
        }
        // présence d'au moins une sortie de rendu (la console doit pouvoir
        // s'ouvrir ; en CI/headless les listes sont vides — rien à démarrer)
        if (_catalog.EnumerateLoopbackSources().Count > 0)
        {
            Dispatcher.UIThread.Post(ToggleRouting);
        }
    }

    /// <summary>Ajoute les tranches par défaut d'une session (D25 : sources internes).</summary>
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

        // Windows : le son du système passe par l'EQ système (APO) — la console
        // démarre vide de périphériques : lecteur + générateur seulement.
        AddTrack(TrackSource.InternalPlayer());
        AddTrack(TrackSource.InternalGenerator());
    }
}
