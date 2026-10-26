# WORKLOG — RoomEq v3 (recodage Triade)

Journal de travail du recodage. Une section par session, format TRIADE.
Copie de référence également maintenue dans le worklog principal
(`/home/z/my-project/worklog.md`).

---
Session : 1 — 2026-10-25/26
Agent : main (Z.ai)
Task : Reprise du projet RoomEq (dossier Drive « Equalizer ») : recodage
entier de l'application, optimisation et nettoyage selon le protocole TRIADE.

Work Log:
- Restauré le contexte complet : listing récursif du Drive « Equalizer »
  (518 entrées), téléchargement miroir idempotent (284 fichiers, 7,9 Mo —
  doublons v1.x écartés en gardant le plus récent), lecture
  d'INSTRUCTIONS-REPRISE.md (v1.9.0) et des worklogs Drive (v2.0→v2.2.1).
- Découvert que GitHub est à **v2.2.2** (deff25c : correctif interop
  SP_DEVINFO_DATA 0x6F8) — plus récent que le dernier worklog Drive → cloné
  `roomeq-git-ref` comme référence code (CI verte) ; le miroir Drive
  `roomeq-legacy/` ne sert que pour les documents hors dépôt.
- Fausses alertes de corruption écartées : les affichages « freqsid] » en bash
  sont des séquences ANSI avalées (`[m` de `freqs[mid]`) — vérifié au lecteur
  de fichiers.
- Installé le SDK .NET 8.0.427 ; créé le dépôt `/home/z/my-project/roomeq`
  avec la structure TRIADE : `I/` (specs par module), `src/`, `tests/`,
  `CARTE.md` (37 modules + 4 natifs scellés), `docs/DECISIONS.md` (D1-D7),
  `docs/PLAN_RECODAGE.md` (7 vagues), `natif/REFERENCE-NATIF.md`.
- **Vague 2 — Dsp + Eq** : specs I écrites puis code recodé : FFT radix-2
  (bit-reversal + papillons par étapes), fenêtres (Hann/BH/rect + protection
  n=1), biquads RBJ en **Transposed Direct Form II** + Process(Span) + MagnitudeAt,
  bruits (blanc/rose Kellet/brun/violet/sinus), balayage ESS (phase forme
  fermée), SpectrumTools (Welch/octaves/lissage O(n)/InterpLog/Peak-RMS),
  ChannelMixer (repli robuste), EqBand/EqPreset/EqChain (reconfiguration par
  référence atomique, **préampli appliqué dans Process** — amélioration),
  CurvePoint partagé. 86 tests verts après correction de 4 tests fautifs
  (Hann(8) : sommet non échantillonné = 0,9505 ; spec corrigée en conséquence).
- **Vague 3 — Calibration + Headphones** : specs puis recodage de TargetCurve
  (3 cibles), SweepAnalyzer (Farina complet), AutoBandPlanner (glouton
  anti-doublons à zones d'exclusion), MultiSignalAnalysis (médiane +
  dispersion + poids de confiance), CorrectionPrecision (4 niveaux, valeurs
  exactes), CorrectionDesigner (Design/FromAligned/FromCalibration),
  MicProfile (+ parseur tolérant), MicProfileLibrary (ressource intégrée),
  MeasurementSignalCatalog, HeadphonePresetLibrary (AutoEq 255 modèles,
  MatchDeviceName normalisé accents). 64 tests verts.
- **Diagnostic empirique de la porte SNR** (mini-projet console temporaire) :
  bruit pur = ratio 186, bruit passe-bas réaliste = 9,2, balayage réel =
  1,2e7 → le seuil 8× de la v2.2.2 laissait passer le bruit seul (bandes
  absurdes au lieu du message d'aide). **Décision D8** : seuil relevé à
  1000× (4 décades de marge de chaque côté), spec + tests à l'appui.
- **Vague 4 — Io** : AppFlavor (silence par défaut) et DiagnosticsLog recodés
  (avec **OverrideDir** qui ferme le writer au changement + ResetForTests) ;
  TelemetryConfig, SignalPack, SignalStore, SignalCatalogClient,
  DriveLogUploader **adoptés tels quels** (déjà conformes aux specs que j'ai
  écrites à partir d'eux, sécurisés en v1.9-v2.0, testés — la réécriture
  n'apporterait rien) ; Stores recodé avec **SanitizeFileName
  multi-plateformes** (union fixe Windows+Unix : la v2.2.2 dépendait de
  Path.GetInvalidFileNameChars() de l'OS hôte — défaut révélé par les tests
  sous Linux). 62 tests verts.
- Outillage de sauvegarde (D7) : `scripts/drive_sync_roomeq.py` (miroir MD5
  idempotent + bundle git) et `scripts/sync_backup.sh` (tests → commit → push
  GitHub branche v3-recodage → miroir Drive « Equalizer/RoomEq-v3 »).
- Documents de reprise : REPRISE.md (état + méthode + pièges), README.md,
  REFERENCE-NATIF.md, Carte et plan mis à jour (vagues 1-4 ✅).
- Commits : 6e3efc5 (Dsp+Eq+Calibration+Headphones, 150 tests) puis 0d04fce
  (Io, 212 tests) + commit de clôture.

Stage Summary:
- **RoomEq.Core v3 entièrement recodé sous TRIADE** : 24/24 modules ✅,
  212/212 tests verts, build 0 erreur 0 avertissement, 25 specs I écrites
  (une par module) AVANT le code, Carte à adresses stables tenue à jour.
- Améliorations documentées vs v2.2.2 : D8 (détection balayage), préampli
  EqChain, SanitizeFileName multi-plateformes, DiagnosticsLog injectable,
  PlanInfo dans CorrectionResult (le designer ne dépend plus du journal).
- Prochaine session : **vague 5 — RoomEq.Audio** (M-AUD-01..06, 5 259 lignes
  legacy : capture, lecture, routage/mixeur, pont ASIO, pont macOS, moteur de
  mesure) puis vague 6 — App. Checklist : REPRISE.md §2.
- Sauvegardes : GitHub branche v3-recodage + Drive « Equalizer/RoomEq-v3 »
  (fichiers + bundle git restaurable) + dépôt local.

---
Session : 2 — 2026-10-26
Agent : main (Z.ai)
Task : Vague 5 du plan de recodage — RoomEq.Audio complet (M-AUD-01..06),
specs I d'abord, code ensuite, tests verts, Carte tenue à jour (protocole TRIADE).

Work Log:
- Reconstitué le contexte : lecture du worklog, du plan, de la Carte, des
  décisions D1-D8 ; inventaire du legacy v2.2.2 (clone git-ref) : 26 fichiers
  C#, 5 500 lignes + 1 045 lignes de tests (6 fichiers).
- Écrit les 6 specs I/audio/ AVANT le code (R2) : I-capture (M-AUD-01),
  I-lecture (M-AUD-02), I-routage (M-AUD-03, le plus gros : 12 invariants),
  I-pont-asio (M-AUD-04, miroir binaire du protocole C), I-pont-macos
  (M-AUD-05), I-moteur-mesure (M-AUD-06, ordre temporel et garde-fous).
- Créé le projet src/RoomEq.Audio (NAudio 2.2.1, InternalsVisibleTo vers les
  tests) et ajouté à la solution ; recodé les 26 fichiers en clean-room informé
  du legacy : CaptureEngine, PlaybackEngine + SampleProviders + AudioOutputs +
  TestSignalCatalog, RingBuffer, TrackSource (scindé d'AudioDeviceCatalog),
  AudioDeviceCatalog, VirtualAudioProviders, MixerStore, VolumeSyncPolicy,
  EndpointVolumeController, DefaultDeviceSetter, AudioDeviceWatcher,
  DriverSetupManager, ConsoleChannel, ConsoleMixer (+MasterBus/StereoToOutput),
  AsioBridgeInterop/Host/Manager, CoreAudioApi/Catalog/Output/Capture,
  MacAudioBridge, RoomMeasurementEngine.
- Améliorations v3 documentées : ConsoleWasapiOutput fusionné dans
  WasapiOutput(latenceMs) (suppression d'une classe dupliquée) ;
  MasterBus et StereoToOutput extraits en classes internes testables ;
  AsioBridgeTransport discrimine la plateforme par test explicite
  OperatingSystem.IsWindows() au lieu de catch(PlatformNotSupportedException) ;
  Volume/Loop de PlaybackEngine en lecture/écriture Volatile (atomiques pour
  le thread audio) ; CoreAudioOutput lève PlaybackStopped à l'arrêt (contrat
  IAudioOutput homogène) ; constructeur de test AsioBridgeHost avec libellés
  paramétrés (le pont périphérique n'écrase plus les textes par réflexion).
- Écrit la suite tests/RoomEq.Audio.Tests : 9 fichiers, 142 tests — portage
  intégral des tests legacy (RingBuffer 6, console/persist/EQ-sync, AsioBridge
  8, DeviceBridge 3, DriverSetup 12, SignalCatalog/VolumeSync théories) +
  extensions v3 (providers : gain/canaux/tap/générateur-FFT/boucle ; MasterBus :
  sommation, mute, multi-canaux ; anti-larsen vérifiable sous Linux car la
  validation précède l'ouverture WASAPI ; AssembleSegment : rognage exact ;
  inertie plateforme de Capture/Playback/Catalog/AsioManager).
- Corrections en cours de route : using System.Collections.Concurrent oublié ;
  contrainte where T : struct omise sur SetPropertyStruct (reprise du legacy) ;
  EqSampleProvider ramené au contrat 2 arguments ; CS0067 réglé en levant
  PlaybackStopped ; 2 tests fautifs corrigés (arithmétique de rognage
  3000−750=2250 ; loi de pan −3 dB/voie au centre : crête ≈ 0,707).
- Vérification R5 : solution complète build 0 erreur 0 avertissement ;
  212/212 tests Core + 142/142 tests Audio = 354 verts.
- Carte mise à jour (M-AUD-01..06 ✅, statut vagues 2-5), plan de recodage
  (vague 5 ✅ + améliorations, prochaine = vague 6 App).

Stage Summary:
- RoomEq.Audio v3 recodé et vérifié : 6/6 modules ✅, 142 tests verts, build
  0/0, specs I/audio/ complètes — la couche E/S est consommable par la vague 6
  (RoomEq.App) via les mêmes points d'entrée que la v2.2.2.
- Le protocole du pont ASIO reste verrouillé côté tests (constantes, décalages,
  tailles) : toute dérive du miroir C/C# est détectée avant livraison.
- Prochaine session : vague 6 — RoomEq.App (M-APP-01..05, shell Avalonia,
  ViewModels < 300 lignes) puis vague 7 (finalisation, parité v2.2.2, tag).

---
## Vague 6 (session 3) — RoomEq.App complet + comblement M-EQ-04

**Périmètre** : M-APP-01..05 (shell Avalonia, 6 onglets, ViewModels, contrôles)
+ M-EQ-04 DevicePresetStore — module v2.2.2 oublié lors des vagues 2-4,
découvert à l'analyse des dépendances de MainViewModel (politique « preset
adapté à la sortie »).

**Travail** :
- 6 specs I écrites AVANT le code (R2) : `I/eq/I-presets-peripherique.md` +
  `I/app/I-shell.md`, `I-onglet-calibration.md`, `I-onglet-mixage.md`,
  `I-onglet-eq.md`, `I-onglets-secondaires.md`.
- Core : `Eq/DevicePresetStore.cs` recodé (magasin JSON à fichier unique,
  répertoire injectable) + 7 tests portés/étendus (écriture inutile évitée
  sur Remove sans cible).
- RoomEq.App créé (Avalonia 11.3.2 + CommunityToolkit.Mvvm 8.4.0, mêmes
  versions que la v2.2.2) : csproj (manifest/icône Windows, exclusion macOS),
  thème sombre App.axaml, Program, assets.
- ViewModels — décomposition v3, chaque fichier < 300 lignes :
  MainViewModel (~300) + OutputPresetCoordinator (politique preset↔sortie,
  interfaces étroites IOutputPresetEqView/IOutputPresetMixerView → testable
  sans périphérique) + TestSignalMenu (menu signaux) ; EqViewModel +
  HeadphonePickerViewModel (casques) ; MixerViewModel en 5 partiels
  (cœur / Routing / Volume / Driver / Persist) ; ChannelViewModel +
  TrackEqViewModel séparés ; CalibrationViewModel + CalibrationOptions +
  LiveMicMonitor ; MicViewModel + MicViewModel.Curve ; AnalyzerViewModel ;
  AsioViewModel ; BandViewModel ; RollingBuffer.
- Contrôles : PlotControl (log/dB + PlotSeries), VuMeter, EqControl
  (poignées + molette Q), MicCurveEditor, HelpButton/HelpDialog,
  FlavorDialog, SignalCatalogDialog.
- Vues : MainWindow (barre supérieure, 6 onglets, barre d'état, OSD volume,
  raccourcis Ctrl+Alt+↑/↓/M) + EqTab, MixerTab, CalibrationTab, MicTab,
  AnalyzerTab, AsioTab — bindings adaptés aux sous-ViewModels extraits.
- Tests App (75) : RollingBuffer, Band/Channel/TrackEq,
  OutputPresetCoordinator (priorités, mémorisation, replis), HeadphonePicker,
  EqViewModel (presets, import calibration, poignées), MicViewModel (édition
  courbe, noms uniques, imports), onglets secondaires (options, LiveMicMonitor
  centré, menu signaux, ASIO sans pilote).

**Améliorations v3 (documentées dans la spec et le plan)** :
- `StoredChannel.FilePath` (existant v3 Core, non rempli en v2) : le fichier
  de la tranche Lecteur survit au redémarrage.
- Bogue v2 corrigé — AsioViewModel sans pilote : SelectedDriver reste null →
  aucun PropertyChanged → message d'aide jamais affiché. Appel explicite de
  RefreshInfo() quand la liste est vide.
- Bogue v2 corrigé — « Lier ce preset à la sortie » : le preset plat par
  défaut (« Plat ») échappait au garde-fou (seul « Personnalisé » était
  refusé) → liaison d'un preset sans nom demandée puis refusée en cascade.
- Kind « mac » (MacVirtualDevice) géré dans la persistance mixer.json
  (la v2 ne persistait pas ce libellé).

**Vérification R5** : solution complète build 0 erreur 0 avertissement ;
436/436 tests verts (219 Core + 142 Audio + 75 App).

**Carte** : M-EQ-04 + M-APP-01..05 ✅ ; statut vagues 2-6.

**Prochaine session** : vague 7 — finalisation (parité v2.2.2 point par
point, tag v3.0.0-dev.1, marqueurs 🔒, packaging adapté des scripts CI legacy).
