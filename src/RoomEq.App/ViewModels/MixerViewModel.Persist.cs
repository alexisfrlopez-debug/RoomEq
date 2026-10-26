using CommunityToolkit.Mvvm.ComponentModel;
using RoomEq.Audio.Routing;

namespace RoomEq.App.ViewModels;

/// <summary>
/// MixerViewModel — persistance mixer.json : sortie master, faders, options
/// de routage, défauts Windows sauvegardés et tranches complètes (avec leur
/// EQ). La persistance ne lève jamais (l'audio prime) — spec I-APP-03.
/// Amélioration v3 : le fichier chargé dans la tranche Lecteur
/// (StoredChannel.FilePath) est restauré au démarrage.
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
                VirtualProviderId = SelectedProvider.Id,
                AutoRouteOnInstall = AutoRouteOnInstall,
                SwitchDefaultCapture = SwitchDefaultCapture,
                SavedDefaultRenderId = _savedRenderId,
                SavedDefaultCaptureId = _savedCaptureId
            };
            foreach (var vm in Channels)
            {
                stored.Channels.Add(new StoredChannel
                {
                    Kind = vm.Channel.Kind switch
                    {
                        TrackSourceKind.Capture => "capture",
                        TrackSourceKind.Loopback => "loopback",
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
        _initializing = true;
        try
        {
            var cfg = _store.Load();
            AutoStart = cfg.AutoStart;
            AutoOutputPreset = cfg.AutoOutputPreset;
            AutoRouteOnInstall = cfg.AutoRouteOnInstall;
            SwitchDefaultCapture = cfg.SwitchDefaultCapture;
            _savedRenderId = cfg.SavedDefaultRenderId;
            _savedCaptureId = cfg.SavedDefaultCaptureId;
            MasterFader = cfg.MasterFader <= 0 ? 1.0 : cfg.MasterFader;
            MasterMute = cfg.MasterMute;
            _storedOutputId = cfg.MasterOutputId;
            SelectedProvider = VirtualAudioCatalog.GetById(cfg.VirtualProviderId);

            VirtualCard = _catalog.ProbeVirtualCard(SelectedProvider);

            int restored = 0;
            foreach (var sc in cfg.Channels)
            {
                TrackSource src = sc.Kind switch
                {
                    "capture" => TrackSource.CaptureDevice(sc.DeviceId, sc.Name),
                    "loopback" => TrackSource.LoopbackDevice(sc.DeviceId, sc.Name),
                    "player" => TrackSource.InternalPlayer(),
                    "asio" => TrackSource.AsioBridgeSource(),
                    "mac" => TrackSource.MacVirtualSource(),
                    _ => TrackSource.InternalGenerator()
                };

                // un périphérique disparu depuis la dernière session est ignoré
                if (src.Kind is TrackSourceKind.Capture or TrackSourceKind.Loopback
                    && _catalog.ResolveDevice(src) == null) continue;
                // la piste DAW (ASIO) est restaurée telle quelle (source interne)

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
        finally
        {
            _initializing = false;
        }
    }
}
