using System.Collections.ObjectModel;
using System.Linq;
using Avalonia.Media;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using RoomEq.Audio.Capture;
using RoomEq.Audio.Measurement;
using RoomEq.Audio.Playback;
using RoomEq.Audio.Routing;
using RoomEq.Core.Calibration;
using RoomEq.Core.Eq;
using RoomEq.Core.Headphones;
using RoomEq.Core.Io;

namespace RoomEq.App.ViewModels;

/// <summary>
/// Choix du menu déroulant des signaux de test : soit un signal généré à la
/// volée par le moteur (bruit, sinus, balayage — voir TestSignalCatalog), soit
/// un fichier WAV téléchargé depuis le catalogue officiel du projet.
/// </summary>
public sealed record TestSignalChoice(
    string Id,
    string DisplayName,
    string Category,
    GeneratorSignalItem? Generator,
    string? FilePath);

/// <summary>
/// ViewModel principal : sorties audio, transport, volume, bypass, et sous-ViewModels.
/// </summary>
public partial class MainViewModel : ObservableObject
{
    public PlaybackEngine Engine { get; }
    public CaptureEngine AnalyzerCapture { get; }
    public RoomMeasurementEngine Measurement { get; }

    public EqViewModel Eq { get; }
    public AnalyzerViewModel Analyzer { get; }
    public CalibrationViewModel Calibration { get; }
    public MicViewModel Mic { get; }
    public AsioViewModel Asio { get; }
    public MixerViewModel Mixer { get; }

    private readonly Avalonia.Threading.DispatcherTimer _telemetryTimer;
    private bool _syncingVolumeFromMixer; // recadrage du curseur depuis le Mixage : ne pas réécrire la carte

    /// <summary>Surveillance des branchements audio à chaud (inerte hors Windows).</summary>
    private readonly AudioDeviceWatcher _deviceWatcher = new();

    /// <summary>Liaisons preset ↔ périphérique de sortie (%APPDATA%\RoomEq\devicepresets.json).</summary>
    private readonly DevicePresetStore _devicePresets = new();

    [ObservableProperty] private ObservableCollection<OutputDeviceInfo> outputs = new();
    [ObservableProperty] private OutputDeviceInfo? selectedOutput;
    [ObservableProperty] private double volume = 1.0;
    [ObservableProperty] private bool bypass;
    [ObservableProperty] private string statusText = "Prêt.";
    [ObservableProperty] private string sourceName = "";
    [ObservableProperty] private bool isPlaying;

    /// <summary>Menu déroulant des signaux de test (intégrés + fichiers téléchargés).</summary>
    public ObservableCollection<TestSignalChoice> SignalChoices { get; } = new();
    [ObservableProperty] private TestSignalChoice? selectedSignal;

    /// <summary>Demande l'ouverture du catalogue de signaux (fenêtre gérée par la vue).</summary>
    public event Action? SignalCatalogRequested;

    public MainViewModel()
    {
        Engine = new PlaybackEngine();
        AnalyzerCapture = new CaptureEngine();
        Measurement = new RoomMeasurementEngine(Engine, new CaptureEngine());

        Mic = new MicViewModel();
        Eq = new EqViewModel();
        Analyzer = new AnalyzerViewModel(Engine, AnalyzerCapture);
        Calibration = new CalibrationViewModel(Engine, Measurement, Mic);
        Asio = new AsioViewModel(() => Engine.Stop());
        Mixer = new MixerViewModel(Engine, SyncVolumeFromRouted);

        // initialisations croisées
        Eq.Init(Engine);
        Calibration.CorrectionToEq += preset => Dispatcher.UIThread.Post(() => Eq.LoadPreset(preset));
        Calibration.CorrectionSavePreset += preset => Dispatcher.UIThread.Post(() =>
        {
            new PresetStore().Save(preset);
            Eq.LoadPreset(preset); // rafraîchit la liste des presets
            BindCalibrationToMeasurementDevice(preset);
        });
        // la courbe mesurée devient importable dans l'onglet Égaliseur
        // (retirée automatiquement quand l'utilisateur réinitialise la calibration)
        Calibration.CorrectionReady += r => Dispatcher.UIThread.Post(() => Eq.SetCalibrationSource(r));
        Calibration.CurveReset += () => Dispatcher.UIThread.Post(() => Eq.SetCalibrationSource(null));

        // l'analyseur peut appliquer le profil micro sélectionné (lu dynamiquement à chaque analyse)
        Analyzer.MicGainAt = f => Mic.SelectedProfile is { IsValid: true, Enabled: true } p ? p.GainAt(f) : 0.0;

        // événements moteur (threads audio → UI)
        Engine.ErrorOccurred += msg => Dispatcher.UIThread.Post(() =>
        {
            StatusText = "Erreur : " + msg;
            DiagnosticsLog.Error("Moteur de lecture : " + msg);
        });
        Engine.StateChanged += _ => Dispatcher.UIThread.Post(RefreshTransportState);
        Engine.SourceChanged += name => Dispatcher.UIThread.Post(() =>
        {
            SourceName = name ?? "";
            StatusText = name != null ? "Lecture : " + name : "Lecture arrêtée.";
            RefreshTransportState();
        });

        RefreshOutputs();
        SelectedOutput = Outputs.FirstOrDefault();
        if (SelectedOutput != null) Engine.SetOutput(SelectedOutput.Id);
        Engine.EnsureDefaultOutput();
        LoadSignalChoices();

        // politique « preset par périphérique » : la sortie master peut changer
        // (restauration de session, routage) et chaque preset usine appliqué
        // manuellement est mémorisé pour la sortie en cours
        Mixer.MasterOutputChanged += dev => Dispatcher.UIThread.Post(() => EvaluateOutputPreset(dev));
        Mixer.OutputPresetOptionPicked += opt => Dispatcher.UIThread.Post(() => ApplyPresetOption(opt));
        Eq.HeadphonePresetApplied += model => Dispatcher.UIThread.Post(() => LinkFactoryPresetToOutput(model));
        Eq.LinkPresetRequested += name => Dispatcher.UIThread.Post(() => LinkNamedPresetToOutput(name));
        Dispatcher.UIThread.Post(() => EvaluateOutputPreset(Mixer.SelectedOutput));

        // branchements à chaud : casque Bluetooth, carte USB… les listes suivent
        // toutes seules (les boutons ⟳ / « Rafraîchir » restent disponibles)
        _deviceWatcher.DevicesChanged += OnHotDevicesChanged;

        // télémétrie (version de test) : envoi des journaux toutes les 5 minutes
        _telemetryTimer = new Avalonia.Threading.DispatcherTimer(
            TimeSpan.FromMinutes(5), Avalonia.Threading.DispatcherPriority.Background,
            (_, _) => _ = Task.Run(DriveLogUploader.UploadNowAsync));
        if (DriveLogUploader.Config.Enabled)
            _telemetryTimer.Start();
    }

    /// <summary>
    /// Fermeture de l'application : arrêt du routage, libération des périphériques
    /// et DERNIER envoi des journaux vers le Drive (synchrone, 10 s max — c'est le
    /// seul moment où l'on peut garantir l'envoi de la session entière).
    /// </summary>
    public void OnAppClosing()
    {
        try
        {
            _telemetryTimer.Stop();
            _deviceWatcher.Dispose();
            Mixer.SaveConfig();
            Mixer.Mixer.Stop();
            Mixer.Shutdown();
            Engine.Stop();
            AnalyzerCapture.Dispose();
            Engine.Dispose();
        }
        catch
        {
            // la fermeture ne doit jamais échouer
        }

        if (DriveLogUploader.Config.Enabled)
        {
            try
            {
                DriveLogUploader.UploadNowAsync().Wait(TimeSpan.FromSeconds(10));
            }
            catch
            {
                // un envoi impossible ne doit pas retarder la fermeture
            }
        }
    }

    private void RefreshTransportState()
    {
        IsPlaying = Engine.State == NAudio.Wave.PlaybackState.Playing;
    }

    [RelayCommand]
    private void RefreshOutputs()
    {
        Outputs = new ObservableCollection<OutputDeviceInfo>(Engine.EnumerateOutputs());
        if (SelectedOutput != null && !Outputs.Contains(SelectedOutput))
        {
            SelectedOutput = Outputs.FirstOrDefault();
        }
    }

    partial void OnSelectedOutputChanged(OutputDeviceInfo? value)
    {
        if (value == null) return;
        Engine.SetOutput(value.Id);
        StatusText = $"Sortie : {value.DisplayName}";
    }

    // ================================================================ preset par périphérique

    /// <summary>
    /// Un périphérique audio vient d'être branché / retiré / ou devient le périphérique par défaut :
    /// rafraîchit toutes les listes (barre du haut, Mixage, Analyseur, Calibration)
    /// puis réévalue le preset de la sortie master — le nouveau casque branché est
    /// reconnu et sa correction appliquée sans redémarrer RoomEq.
    /// </summary>
    private void OnHotDevicesChanged()
    {
        Dispatcher.UIThread.Post(() =>
        {
            RefreshOutputs();
            Mixer.RefreshDevices();
            try { Analyzer.RefreshAudioDevices(); } catch { /* énumération indisponible */ }
            try { Calibration.RefreshAudioDevices(); } catch { /* énumération indisponible */ }
            EvaluateOutputPreset(Mixer.SelectedOutput);
            StatusText = "Périphériques audio mis à jour (branchement à chaud détecté).";
        });
    }

    /// <summary>
    /// Politique du preset adapté à la sortie master (onglet Mixage) :
    ///   1. liaison manuelle de l'utilisateur (prioritaire) ;
    ///   2. preset de calibration mesuré sur ce périphérique ;
    ///   3. preset usine reconnu depuis le nom du périphérique (casque/écouteurs
    ///      de la bibliothèque intégrée) ;
    ///   4. rien — l'égaliseur reste tel quel.
    /// </summary>
    private void EvaluateOutputPreset(OutputDeviceInfo? device)
    {
        if (device == null) return;
        Eq.LinkTargetText = $"Sortie master actuelle : {device.DisplayName}";
        var binding = _devicePresets.Get(device.Id);
        RebuildOutputPresetOptions(device, binding);

        if (!Mixer.AutoOutputPreset)
        {
            Mixer.OutputPresetText = "Preset auto désactivé (option du Mixage).";
            return;
        }

        if (binding is { IsUsable: true })
        {
            ApplyBinding(device, binding, binding.Kind == DevicePresetKind.Manual
                ? "preset manuel mémorisé pour cette sortie"
                : "preset de calibration mesuré sur cette sortie");
            return;
        }
        if (binding is { Kind: DevicePresetKind.None })
        {
            Mixer.OutputPresetText = "Aucun preset pour cette sortie (choix « Aucun »).";
            return;
        }

        var match = HeadphonePresetLibrary.MatchDeviceName(device.DisplayName);
        if (match != null)
        {
            Eq.LoadPreset(HeadphonePresetLibrary.ToEqPreset(match));
            Mixer.OutputPresetText = $"Auto : « {match.DisplayName} » reconnu (usine).";
            StatusText = $"Sortie « {device.DisplayName} » reconnue : correction {match.DisplayName} appliquée automatiquement.";
            DiagnosticsLog.Info($"Preset auto : {device.DisplayName} -> {match.DisplayName} (usine)");
        }
        else
        {
            Mixer.OutputPresetText = "Auto : aucun preset connu pour cette sortie.";
        }
    }

    /// <summary>Applique une liaison (usine ou utilisateur) à l'égaliseur principal.</summary>
    private void ApplyBinding(OutputDeviceInfo device, DevicePresetBinding binding, string originLabel)
    {
        if (binding.PresetClass == "factory")
        {
            var model = HeadphonePresetLibrary.Find(binding.PresetRef);
            if (model == null)
            {
                Mixer.OutputPresetText = "Preset lié introuvable dans la bibliothèque — retour en auto.";
                _devicePresets.Remove(device.Id);
                RebuildOutputPresetOptions(device, null);
                return;
            }
            Eq.LoadPreset(HeadphonePresetLibrary.ToEqPreset(model));
        }
        else
        {
            var preset = new PresetStore().Load(binding.PresetRef);
            if (preset == null)
            {
                Mixer.OutputPresetText = $"Preset « {binding.PresetRef} » supprimé — retour en auto.";
                _devicePresets.Remove(device.Id);
                RebuildOutputPresetOptions(device, null);
                return;
            }
            Eq.LoadPreset(preset);
        }
        Mixer.OutputPresetText = $"Appliqué : « {binding.PresetRef} » ({originLabel}).";
        StatusText = $"Preset de « {device.DisplayName} » : {binding.PresetRef}.";
    }

    /// <summary>Reconstruit le sélecteur « Preset de la sortie » (Auto / Aucun / vôtres / suggestion usine).</summary>
    private void RebuildOutputPresetOptions(OutputDeviceInfo device, DevicePresetBinding? binding)
    {
        var options = new List<OutputPresetOption>
        {
            new("auto", "Auto (recommandé)", "Mode"),
            new("none", "Aucun", "Mode"),
        };

        // presets utilisateur (typiquement : calibrations sauvegardées)
        foreach (var name in new PresetStore().List())
            options.Add(new OutputPresetOption("user:" + name, name, "Vos presets"));

        // suggestion usine : le modèle reconnu dans le nom du périphérique
        var match = HeadphonePresetLibrary.MatchDeviceName(device.DisplayName);
        if (match != null)
            options.Add(new OutputPresetOption("factory:" + match.Id,
                match.DisplayName + " (reconnu)", "Bibliothèque"));

        OutputPresetOption? selected = binding switch
        {
            { Kind: DevicePresetKind.None } => options.FirstOrDefault(o => o.Key == "none"),
            { IsUsable: true, PresetClass: "user" } =>
                options.FirstOrDefault(o => o.Key == "user:" + binding.PresetRef)
                ?? options.FirstOrDefault(o => o.Key == "auto"),
            { IsUsable: true, PresetClass: "factory" } =>
                options.FirstOrDefault(o => o.Key == "factory:" + binding.PresetRef)
                ?? options.FirstOrDefault(o => o.Key == "auto"),
            _ => options.FirstOrDefault(o => o.Key == "auto"),
        };
        Mixer.SetOutputPresetOptions(options, selected);
    }

    /// <summary>L'utilisateur a choisi une option du sélecteur « Preset de la sortie ».</summary>
    private void ApplyPresetOption(OutputPresetOption option)
    {
        var device = Mixer.SelectedOutput;
        if (device == null) return;

        switch (option.Key)
        {
            case "auto":
                _devicePresets.Remove(device.Id);
                EvaluateOutputPreset(device);
                Eq.LinkPresetStatus = $"Sortie « {device.DisplayName} » : mode Auto (calibration > usine reconnue).";
                break;

            case "none":
                _devicePresets.Set(new DevicePresetBinding
                {
                    DeviceId = device.Id,
                    DeviceName = device.DisplayName,
                    Kind = DevicePresetKind.None,
                });
                Mixer.OutputPresetText = "Aucun preset pour cette sortie (choix mémorisé).";
                Eq.LinkPresetStatus = $"Sortie « {device.DisplayName} » : aucun preset, même automatique.";
                RebuildOutputPresetOptions(device, _devicePresets.Get(device.Id));
                break;

            default:
                if (option.Key.StartsWith("user:", StringComparison.Ordinal))
                {
                    string name = option.Key["user:".Length..];
                    _devicePresets.Set(new DevicePresetBinding
                    {
                        DeviceId = device.Id,
                        DeviceName = device.DisplayName,
                        PresetClass = "user",
                        PresetRef = name,
                        Kind = DevicePresetKind.Manual,
                    });
                }
                else if (option.Key.StartsWith("factory:", StringComparison.Ordinal))
                {
                    string id = option.Key["factory:".Length..];
                    _devicePresets.Set(new DevicePresetBinding
                    {
                        DeviceId = device.Id,
                        DeviceName = device.DisplayName,
                        PresetClass = "factory",
                        PresetRef = id,
                        Kind = DevicePresetKind.Manual,
                    });
                }
                else break;

                ApplyBinding(device, _devicePresets.Get(device.Id)!, "choix manuel");
                RebuildOutputPresetOptions(device, _devicePresets.Get(device.Id));
                Eq.LinkPresetStatus = $"Sortie « {device.DisplayName} » : {option.Label} — mémorisé pour les prochaines sessions.";
                break;
        }
    }

    /// <summary>Un preset usine vient d'être appliqué dans l'onglet Égaliseur : lié à la sortie master.</summary>
    private void LinkFactoryPresetToOutput(HeadphoneModel model)
    {
        var device = Mixer.SelectedOutput;
        if (device == null) return;
        _devicePresets.Set(new DevicePresetBinding
        {
            DeviceId = device.Id,
            DeviceName = device.DisplayName,
            PresetClass = "factory",
            PresetRef = model.Id,
            Kind = DevicePresetKind.Manual,
        });
        RebuildOutputPresetOptions(device, _devicePresets.Get(device.Id));
        Mixer.OutputPresetText = $"Appliqué : « {model.DisplayName} » (choix manuel).";
        Eq.LinkPresetStatus = $"Correction {model.DisplayName} mémorisée pour la sortie « {device.DisplayName} ».";
    }

    /// <summary>Le bouton « Lier à la sortie » de l'onglet Égaliseur : preset nommé → sortie master.</summary>
    private void LinkNamedPresetToOutput(string presetName)
    {
        var device = Mixer.SelectedOutput;
        if (device == null)
        {
            Eq.LinkPresetStatus = "Aucune sortie master sélectionnée (onglet Mixage).";
            return;
        }

        // preset usine ? (le nom affiché est « Marque Modèle »)
        var model = HeadphonePresetLibrary.LoadAll()
            .FirstOrDefault(m => string.Equals(m.DisplayName, presetName, StringComparison.OrdinalIgnoreCase));
        if (model != null)
        {
            LinkFactoryPresetToOutput(model);
            return;
        }

        // preset utilisateur : il doit être sauvegardé pour survivre aux sessions
        var store = new PresetStore();
        if (store.Load(presetName) == null)
        {
            Eq.LinkPresetStatus = $"Le preset « {presetName} » n'est pas encore sauvegardé : " +
                "cliquez « Enregistrer » ci-dessus, puis reliez-le.";
            return;
        }
        _devicePresets.Set(new DevicePresetBinding
        {
            DeviceId = device.Id,
            DeviceName = device.DisplayName,
            PresetClass = "user",
            PresetRef = presetName,
            Kind = DevicePresetKind.Manual,
        });
        RebuildOutputPresetOptions(device, _devicePresets.Get(device.Id));
        Mixer.OutputPresetText = $"Appliqué : « {presetName} » (choix manuel).";
        Eq.LinkPresetStatus = $"Preset « {presetName} » mémorisé pour la sortie « {device.DisplayName} ».";
    }

    /// <summary>
    /// Une calibration vient d'être sauvegardée : le preset est lié au périphérique
    /// de mesure (la sortie de lecture de la barre du haut, celle où le balayage a
    /// été joué) — la reconnaissance automatique le réappliquera à chaque fois que
    /// ce périphérique redevient la sortie master.
    /// </summary>
    private void BindCalibrationToMeasurementDevice(EqPreset preset)
    {
        var device = SelectedOutput;   // sortie du moteur de mesure au moment de la calibration
        if (device == null) return;
        _devicePresets.Set(new DevicePresetBinding
        {
            DeviceId = device.Id,
            DeviceName = device.DisplayName,
            PresetClass = "user",
            PresetRef = preset.Name,
            Kind = DevicePresetKind.Calibration,
        });
        DiagnosticsLog.Info($"Calibration liee a {device.DisplayName} -> preset {preset.Name}");
        if (string.Equals(Mixer.SelectedOutput?.Id, device.Id, StringComparison.OrdinalIgnoreCase))
            RebuildOutputPresetOptions(device, _devicePresets.Get(device.Id));
    }

    partial void OnVolumeChanged(double value)
    {
        // 1) lecteur interne (fichiers, signaux de test) : gain après l'EQ ;
        // 2) son ROUTÉ (Windows → carte virtuelle → mixeur → carte physique) :
        //    ce chemin ne passe PAS par le lecteur interne — le curseur pilote
        //    donc aussi le volume Windows de la carte physique routée (même
        //    cible que Ctrl+Alt+↑ / ↓). Sans lui, le curseur paraîtrait « sans
        //    effet » sur la sortie sélectionnée dans l'usage principal.
        Engine.Volume = value;

        // recadrage venant du Mixage / des raccourcis clavier / de Windows : la
        // carte est DÉJÀ à la bonne valeur (c'est elle qui a provoqué le recadrage)
        // — ne pas la réécrire, seule la position du curseur doit suivre.
        if (!_syncingVolumeFromMixer)
            Mixer.ApplyTopBarVolume(value);

        StatusText = value <= 0.001
            ? "Volume coupé (lecteur interne + carte physique routée)."
            : $"Volume : {Math.Min(value, 1.0):0%} — lecteur interne + carte physique routée" + (value > 1.0 ? " (carte au maximum, gain lecteur augmenté)" : "");
    }

    /// <summary>
    /// Recadre le curseur de la barre supérieure sur le volume RÉEL de la carte
    /// physique, après un changement venu d'ailleurs : curseur de l'onglet Mixage,
    /// raccourcis Ctrl+Alt+↑ / ↓, changement de sortie master, ou réglage fait
    /// hors de l'application (mixeur Windows). C'est ce qui garde les deux
    /// curseurs identiques quel que soit celui que l'on déplace, et dans quel
    /// ordre. Le drapeau évite la boucle : sans lui, OnVolumeChanged réécrirait
    /// la carte (déjà à la bonne valeur) et déclencherait un aller-retour.
    /// </summary>
    internal void SyncVolumeFromRouted(double endpointScalar)
    {
        if (!VolumeSyncPolicy.ShouldSyncTopBar(Volume, endpointScalar)) return;
        _syncingVolumeFromMixer = true;
        try { Volume = endpointScalar; }
        finally { _syncingVolumeFromMixer = false; }
    }

    partial void OnBypassChanged(bool value)
    {
        Engine.Eq.Bypass = value;
        StatusText = value ? "Égaliseur contourné (bypass)." : "Égaliseur actif.";
    }

    [RelayCommand]
    private void PlayPause()
    {
        switch (Engine.State)
        {
            case NAudio.Wave.PlaybackState.Playing:
                Engine.Pause();
                break;
            case NAudio.Wave.PlaybackState.Paused:
                Engine.Resume();
                break;
            default:
                StatusText = "Ouvrez un fichier ou lancez un signal de test pour écouter.";
                break;
        }
        RefreshTransportState();
    }

    [RelayCommand]
    private void Stop()
    {
        Engine.Stop();
        RefreshTransportState();
        StatusText = "Lecture arrêtée.";
    }

    /// <summary>Joue un fichier (appelé par la vue après le sélecteur de fichiers).</summary>
    public void PlayFile(string path)
    {
        Engine.PlayFile(path);
        RefreshTransportState();
    }

    // ================================================================ signaux de test

    /// <summary>Reconstruit le menu déroulant : signaux intégrés + bibliothèque téléchargée.</summary>
    public void RefreshSignalChoices() => LoadSignalChoices();

    private void LoadSignalChoices()
    {
        TestSignalChoice? previous = SelectedSignal;
        SignalChoices.Clear();
        foreach (var g in TestSignalCatalog.BuiltIn)
            SignalChoices.Add(new TestSignalChoice(g.Id, g.DisplayName, g.Category, g, null));

        var store = new SignalStore();
        foreach (var f in store.List())
            SignalChoices.Add(new TestSignalChoice(
                "fichier|" + f.FileName, f.DisplayName, "Téléchargés", null, store.PathFor(f.FileName)));

        // conserve la sélection précédente si elle existe encore, sinon le signal
        // par défaut (bruit rose : réglage de niveau d'écoute)
        SelectedSignal = (previous != null ? SignalChoices.FirstOrDefault(c => c.Id == previous.Id) : null)
                         ?? SignalChoices.FirstOrDefault(c => c.Id == TestSignalCatalog.DefaultItemId)
                         ?? SignalChoices.FirstOrDefault();
    }

    [RelayCommand]
    private void PlaySelectedSignal()
    {
        var choice = SelectedSignal;
        if (choice == null) return;

        if (choice.Generator is { } g)
        {
            Engine.PlayTestSignal(g.Signal, g.Amplitude, g.SineFrequency, g.SweepSpec);
            RefreshTransportState();
            StatusText = g.DisplayName + " — ajustez le volume si besoin.";
            return;
        }

        if (choice.FilePath != null)
        {
            try
            {
                // les fichiers du catalogue sont courts : lecture en boucle pour
                // un usage à l'oreille continu, comme les signaux générés
                var reader = new NAudio.Wave.AudioFileReader(choice.FilePath);
                void Restart() { try { reader.Position = 0; } catch { /* fichier retiré ? */ } }
                Engine.PlaySource(new LoopingSampleProvider(reader, Restart) { Loop = true },
                    choice.DisplayName, Restart);
                RefreshTransportState();
                StatusText = choice.DisplayName + " (fichier téléchargé, lecture en boucle).";
            }
            catch (Exception ex)
            {
                StatusText = "Lecture impossible : " + ex.Message;
                DiagnosticsLog.Error("Lecture d'un signal téléchargé : " + ex.Message);
            }
        }
    }

    [RelayCommand]
    private void OpenSignalCatalog() => SignalCatalogRequested?.Invoke();
}
