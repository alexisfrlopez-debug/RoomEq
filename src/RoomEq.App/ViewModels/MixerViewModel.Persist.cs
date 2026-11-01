using CommunityToolkit.Mvvm.ComponentModel;
using RoomEq.Audio.Routing;

namespace RoomEq.App.ViewModels;

/// <summary>
/// MixerViewModel — persistance mixer.json : sortie master, faders, options
/// de la console et tranches complètes (avec leur EQ). La persistance ne
/// lève jamais (l'audio prime) — spec I-APP-03 v2.
/// D25 (v3.8.0) : les tranches « entrée/sortie » (capture/loopback) des
/// anciennes configurations sont IGNORÉES au chargement — elles n'existent
/// plus dans la console (l'EQ système APO traite le son du système) ; les
/// champs obsolètes (protocole, bascules de défauts) restent lus pour
/// tolérance puis cessent d'être écrits.
/// </summary>
public partial class MixerViewModel
{
    public void SaveConfig()
    {
        try
        {
            var stored = new StoredMixer
            {
                MasterOutputId = SelectedOutput?.Id,
                MasterFader = MasterFader,
                MasterMute = MasterMute,
                AutoStart = AutoStart,
                AutoOutputPreset = AutoOutputPreset,
                SavedDefaultRenderId = _savedRenderId,
                SavedDefaultCaptureId = _savedCaptureId
            };
            foreach (var vm in Channels)
            {
                stored.Channels.Add(new StoredChannel
                {
                    // D25 : la console ne contient plus que des sources internes
                    Kind = vm.Channel.Kind switch
                    {
                        TrackSourceKind.Player => "player",
                        TrackSourceKind.AsioBridge => "asio",
                        TrackSourceKind.MacVirtualDevice => "mac",
                        _ => "generator"
                    },
                    DeviceId = vm.Channel.Source.DeviceId,
                    Name = vm.Channel.Source.Name,
                    Fader = vm.Fader,
                    Pan = vm.Pan,
                    Mute = vm.Mute,
                    Solo = vm.Solo,
                    GeneratorEnabled = vm.GeneratorOn,
                    GeneratorSignal = vm.SelectedSignalText,
                    FilePath = vm.Channel.PlayerFileName,
                    Eq = vm.Channel.Eq.Preset
                });
            }
            _store.Save(stored);
        }
        catch
        {
            // la persistance ne doit jamais perturber l'audio
        }
    }

    private void LoadConfigOrDefaults()
    {
        var cfg = _store.Load();
        AutoStart = cfg.AutoStart;
        AutoOutputPreset = cfg.AutoOutputPreset;
        _savedRenderId = cfg.SavedDefaultRenderId;
        _savedCaptureId = cfg.SavedDefaultCaptureId;
        MasterFader = cfg.MasterFader <= 0 ? 1.0 : cfg.MasterFader;
        MasterMute = cfg.MasterMute;
        _storedOutputId = cfg.MasterOutputId;

        int restored = 0;
        foreach (var sc in cfg.Channels)
        {
            // D25 : les anciennes pistes liées à un périphérique Windows
            // (capture « entrée », boucle « sortie ») ne sont plus restaurées
            if (MixerTrackPolicy.IsLegacyDeviceKind(sc.Kind)) continue;

            TrackSource src = sc.Kind switch
            {
                "player" => TrackSource.InternalPlayer(),
                "asio" => TrackSource.AsioBridgeSource(),
                "mac" => TrackSource.MacVirtualSource(),
                _ => TrackSource.InternalGenerator()
            };

            var ch = Mixer.AddChannel(src);
            var vm = new ChannelViewModel(ch, () => SaveConfig())
            {
                Fader = Math.Clamp(sc.Fader, 0, 1.5),
                Pan = Math.Clamp(sc.Pan, -1, 1),
                Mute = sc.Mute,
                Solo = sc.Solo,
                GeneratorOn = sc.GeneratorEnabled,
                SelectedSignalText = sc.GeneratorSignal ?? "Bruit blanc"
            };
            if (sc.Eq != null) ch.Eq.SetPreset(sc.Eq);
            Channels.Add(vm);
            restored++;

            // v3 : le fichier de la tranche Lecteur survit au redémarrage
            if (src.Kind == TrackSourceKind.Player && !string.IsNullOrEmpty(sc.FilePath))
            {
                try
                {
                    ch.LoadFile(sc.FilePath);
                    vm.RefreshIdentity();
                }
                catch
                {
                    // fichier déplacé ou supprimé : la tranche reste vide
                }
            }
        }

        if (restored == 0) AddDefaultTracks();
    }
}
