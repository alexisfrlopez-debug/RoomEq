# CARTE — RoomEq v3 (recodage Triade)

> Couche C du protocole TRIADE pour ce dépôt. Index de navigation à adresses
> stables. **Règle : toute modification de `src/` doit mettre à jour la ligne
> correspondante de la Carte.** Dernière mise à jour : vague 6 (session de
> recodage 3).

| En-tête | Valeur |
|---|---|
| projet | RoomEq v3.0.0 — égaliseur acoustique temps réel |
| profil de cible | x86-64-pc-windows-msvc (.NET 8 + Avalonia 11) + macOS (greffon scellé) |
| base de référence | v2.2.1 (GitHub alexisfrlopez-debug/RoomEq @ 79c2c95) |
| empreinte legacy | miroir local `roomeq-legacy/src-drive/RoomEq` (284 fichiers) |
| statut | 🚧 vagues 2-6 terminées (Core + Audio + App, 436 tests verts) — reste finalisation |

## Modules — couche I (intention) / B (code)

| ID | Module | Spec I | Code | Tests | Statut |
|----|--------|--------|------|-------|--------|
| M-DSP-01 | FFT radix-2 | `I/dsp/I-fft.md` | `src/RoomEq.Core/Dsp/Fft.cs` | `tests/…/FftTests.cs` | ✅ |
| M-DSP-02 | Fenêtrage | `I/dsp/I-fenetres.md` | `…/Dsp/Windowing.cs` | `…/WindowingTests.cs` | ✅ |
| M-DSP-03 | Filtres biquad | `I/dsp/I-biquad.md` | `…/Dsp/BiquadFilter.cs` | `…/BiquadTests.cs` | ✅ |
| M-DSP-04 | Générateurs de bruit | `I/dsp/I-bruits.md` | `…/Dsp/NoiseGenerator.cs` | `…/NoiseTests.cs` | ✅ |
| M-DSP-05 | Balayage log (ESS) | `I/dsp/I-balayage.md` | `…/Dsp/LogSweepGenerator.cs` | `…/SweepTests.cs` | ✅ |
| M-DSP-06 | Outils spectre | `I/dsp/I-spectre.md` | `…/Dsp/SpectrumTools.cs` | `…/SpectrumTests.cs` | ✅ |
| M-DSP-07 | Mixage canaux | `I/dsp/I-mixage-canaux.md` | `…/Dsp/ChannelMixer.cs` | `…/ChannelMixerTests.cs` | ✅ |
| M-EQ-01 | Bandes EQ | `I/eq/I-bandes.md` | `src/RoomEq.Core/Eq/EqBand.cs` | `…/EqTests.cs` | ✅ |
| M-EQ-02 | Chaîne EQ temps réel | `I/eq/I-chaine-eq.md` | `…/Eq/EqChain.cs` | `…/EqChainTests.cs` | ✅ |
| M-EQ-03 | Presets EQ | `I/eq/I-presets.md` | `…/Eq/EqPreset.cs` | `…/EqTests.cs` | ✅ |
| M-EQ-04 | Liaisons preset ↔ périphérique | `I/eq/I-presets-peripherique.md` | `…/Eq/DevicePresetStore.cs` | `…/DevicePresetStoreTests.cs` | ✅ |
| M-CAL-01 | Courbe cible | `I/calibration/I-courbe-cible.md` | `…/Calibration/TargetCurve.cs` | `…/TargetCurveTests.cs` | ✅ |
| M-CAL-02 | Analyse de balayage | `I/calibration/I-analyse-balayage.md` | `…/Calibration/SweepAnalyzer.cs` | `…/SweepAnalyzerTests.cs` | ✅ |
| M-CAL-03 | Planificateur de bandes | `I/calibration/I-plan-bandes.md` | `…/Calibration/AutoBandPlanner.cs` | `…/AutoBandPlannerTests.cs` | ✅ |
| M-CAL-04 | Multi-signaux | `I/calibration/I-multi-signaux.md` | `…/Calibration/MultiSignalAnalysis.cs` | `…/MultiSignalTests.cs` | ✅ |
| M-CAL-05 | Précision correction | `I/calibration/I-precision.md` | `…/Calibration/CorrectionPrecision.cs` | `…/CorrectionPrecisionTests.cs` | ✅ |
| M-CAL-06 | Design de correction | `I/calibration/I-design-correction.md` | `…/Calibration/CorrectionDesigner.cs` | `…/CorrectionDesignerTests.cs` | ✅ |
| M-CAL-07 | Profils micro | `I/calibration/I-profils-micro.md` | `…/Calibration/MicProfile*.cs` | `…/MicProfileLibraryTests.cs` | ✅ |
| M-CAL-08 | Catalogue signaux test | `I/calibration/I-catalogue-signaux.md` | `…/Calibration/TestSignalCatalog.cs` | `…/MeasurementSignalsTests.cs` | ✅ |
| M-HPH-01 | Presets casque (AutoEq) | `I/headphones/I-presets-casque.md` | `…/Headphones/HeadphonePresetLibrary.cs` | `…/HeadphonePresetLibraryTests.cs` | ✅ |
| M-IO-01 | Version test/public | `I/io/I-saveur-app.md` | `…/Io/AppFlavor.cs` | `…/AppFlavorTests.cs` | ✅ |
| M-IO-02 | Journal diagnostic | `I/io/I-journal.md` | `…/Io/DiagnosticsLog.cs` | `…/DiagnosticsTests.cs` | ✅ |
| M-IO-03 | Télémétrie Drive | `I/io/I-telemetrie.md` | `…/Io/TelemetryConfig.cs` + `DriveLogUploader.cs` | `…/TelemetryTests.cs` | ✅ |
| M-IO-04 | Magasin de signaux | `I/io/I-magasin-signaux.md` | `…/Io/SignalPack.cs` + `SignalStore.cs` + `SignalCatalogClient.cs` | `…/SignalPackTests.cs` + `SignalStoreTests.cs` | ✅ |
| M-IO-05 | Magasins de réglages | `I/io/I-magasins.md` | `…/Io/Stores.cs` | `…/StoresRenameTests.cs` | ✅ |
| M-AUD-01 | Capture | `I/audio/I-capture.md` | `src/RoomEq.Audio/Capture/CaptureEngine.cs` | `…/MeasurementEngineTests.cs` (inertie) | ✅ |
| M-AUD-02 | Lecture | `I/audio/I-lecture.md` | `…/Playback/AudioOutputs.cs` + `SampleProviders.cs` + `PlaybackEngine.cs` + `TestSignalCatalog.cs` | `…/SampleProvidersTests.cs` + `SignalCatalogTests.cs` + `MeasurementEngineTests.cs` | ✅ |
| M-AUD-03 | Routage / mixeur | `I/audio/I-routage.md` | `…/Routing/` (12 fichiers : RingBuffer, TrackSource, AudioDeviceCatalog, VirtualAudioProviders, MixerStore, VolumeSyncPolicy, EndpointVolumeController, DefaultDeviceSetter, AudioDeviceWatcher, DriverSetupManager, ConsoleChannel, ConsoleMixer) | `…/RingBufferTests.cs` + `ConsoleRoutingTests.cs` + `SignalCatalogTests.cs` + `RoomEqDriverTests.cs` | ✅ |
| M-AUD-04 | Pont ASIO | `I/audio/I-pont-asio.md` | `…/Asio/AsioBridgeInterop.cs` + `AsioBridgeHost.cs` + `AsioManager.cs` | `…/AsioBridgeTests.cs` | ✅ |
| M-AUD-05 | Pont macOS | `I/audio/I-pont-macos.md` | `…/Mac/CoreAudioApi.cs` + `CoreAudioCatalog.cs` + `CoreAudioOutput.cs` + `CoreAudioCapture.cs` + `MacAudioBridge.cs` | `…/DeviceBridgeTests.cs` | ✅ |
| M-AUD-06 | Moteur de mesure | `I/audio/I-moteur-mesure.md` | `…/Measurement/RoomMeasurementEngine.cs` | `…/MeasurementEngineTests.cs` | ✅ |
| M-APP-01 | Shell + MVVM | `I/app/I-shell.md` | `src/RoomEq.App/…` (Program, App, MainWindow, MainViewModel, TestSignalMenu, OutputPresetCoordinator) | `…/OutputPresetCoordinatorTests.cs` + `…/SecondaryTabsTests.cs` | ✅ |
| M-APP-02 | Onglet calibration | `I/app/I-onglet-calibration.md` | `…/Views/CalibrationTab…` + `ViewModels/CalibrationViewModel.cs` + `CalibrationOptions.cs` + `LiveMicMonitor.cs` | `…/SecondaryTabsTests.cs` | ✅ |
| M-APP-03 | Onglet mixage | `I/app/I-onglet-mixage.md` | `…/Views/MixerTab…` + `ViewModels/MixerViewModel.cs` + `.Routing` + `.Volume` + `.Driver` + `.Persist` + `ChannelViewModel.cs` + `TrackEqViewModel.cs` | `…/BandTrackChannelTests.cs` | ✅ |
| M-APP-04 | Onglet EQ | `I/app/I-onglet-eq.md` | `…/Views/EqTab…` + `ViewModels/EqViewModel.cs` + `HeadphonePickerViewModel.cs` + `BandViewModel.cs` | `…/EqViewModelTests.cs` + `…/HeadphonePickerTests.cs` | ✅ |
| M-APP-05 | Onglets micro / analyseur / ASIO | `I/app/I-onglets-secondaires.md` | `…/Views/MicTab…` + `AnalyzerTab…` + `AsioTab…` + `ViewModels/MicViewModel.cs` + `.Curve` + `AnalyzerViewModel.cs` + `AsioViewModel.cs` + `RollingBuffer.cs` + `Controls/` (PlotControl, VuMeter, EqControl, MicCurveEditor, dialogues) | `…/MicViewModelTests.cs` + `…/RollingBufferTests.cs` + `…/SecondaryTabsTests.cs` | ✅ |

## Blocs natifs scellés (hors périmètre de recodage — D1)

| ID | Composant | Version scellée | Référence |
|----|-----------|-----------------|-----------|
| N-DRV-01 | Pilote audio virtuel Windows (roomeqvad.sys + INF + CAT) | v2.2.1 | `natif/REFERENCE-NATIF.md` |
| N-ASI-01 | DLL ASIO (RoomEqAsio64.dll + bridge_core) | v2.2.1 | idem |
| N-MAC-01 | Greffon HAL CoreAudio macOS (RoomEqHAL.driver + pkg) | v2.2.1 | idem |
| N-INS-01 | Installateurs (MSI WiX, DriverSetup GUI, AppSetup) | v2.2.1 | idem |

## Convention de statut

- ⬜ non commencé · 🚧 en cours · ✅ recodé + tests verts + spec à jour ·
  🔒 cristallisé (tag git, contrat gelé)
- Un module passe en 🔒 uniquement par tag de version mineur ou plus.
