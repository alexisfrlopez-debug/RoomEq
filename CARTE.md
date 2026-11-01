# CARTE — RoomEq v3 (recodage Triade)

> Couche C du protocole TRIADE pour ce dépôt. Index de navigation à adresses
> stables. **Règle : toute modification de `src/` doit mettre à jour la ligne
> correspondante de la Carte.** Dernière mise à jour : `v3.6.0` (parité
> fonctionnelle AU ↔ VST — module natif M-AUD-07/N-AU-01, décision D23 :
> l'AU gagne l'égaliseur éditable à la souris, la banque casques embarquée
> (321 modèles), les VU-mètres in/out, l'auto-chargement du magasin de
> l'application et le journal ~/Library/Logs/RoomEq/roomeq-au.log ; banque
> + écriture JSON déplacées dans le cœur C++ partagé (ports de RoomEq.Core),
> nouvelle couche d'édition portable `natif/roomeq-au/edit/` testée sous
> Linux ; spec I-AUD-07 v2, 15 671 vérifications natives AU).

| En-tête | Valeur |
|---|---|
| projet | RoomEq v3.6.0 — égaliseur acoustique temps réel |
| profil de cible | x86-64-pc-windows-msvc (.NET 8 + Avalonia 11) + macOS (greffon scellé + greffon AudioUnit `natif/roomeq-au/`) |
| base de référence | v2.2.2 (GitHub alexisfrlopez-debug/RoomEq @ `deff25c`, tag v2.2.2) |
| empreinte legacy | clone git `roomeq-git-ref` (code, CI verte) + miroir Drive `roomeq-legacy` (docs, 284 fichiers) |
| statut | 🔒 v3.6.0 (AU M-AUD-07 en parité fonctionnelle du VST 3.5.x, D23 : égaliseur éditable à la souris, banque casques embarquée 321 modèles, VU-mètres in/out, auto-chargement du magasin, journal ~/Library/Logs — couche d'édition portable testée Linux, banque + écriture JSON déplacées dans le cœur partagé [AU 15 671 vérifications] ; v3.5.2 (correctif ABI M-AUD-08 : rangs `AEffect` dépréciés/pointeurs manquants comblés — l'hôte lit enfin `processReplacing` et traite l'audio, verrouillé par static_asserts + test « vue hôte externe » [430 + mini-hôte 41] ; v3.5.1 : fenêtre du greffon entière — rect kEffEditGetRect = 560×320, source unique editor_geometry.h ; v3.5.0 : égaliseur éditable dans le greffon, banque casques embarquée (321 modèles), VU-mètre + diagnostics ; v3.4.1/v3.4.2 : auto-chargement du preset de l'application + presets plats ignorés ; D22 : règle de livraison — synchro Git+Drive + archivage automatique ; D20 : installation « 1 clic » — `RoomEq-win-Setup.exe` Windows [Velopack, `scripts/build_setup.sh`] + `RoomEq-AU-<v>-macos.pkg` macOS, sans dépendance ajoutée à l'application ; D18 : anti-boucle au moment du routage — M-AUD-03 spec v2 ; D19 : greffon AudioUnit « RoomEq Correction » `.component` — module natif M-AUD-07, CI macOS + auval ; 461 tests verts ; D21 : greffon VST 2 32 bits Windows « RoomEq Correction » `.dll` — module natif M-AUD-08/N-VST-01, ABI VST 2 autodéclarée, CI `plugin-vst`) sur la suite cristallisée v3.0.2 (D14 ; v3.0.0/v3.0.1 = étapes intermédiaires à CI rouge, conservées sans déplacement de tag) — 45 modules 🔒, parité v2.2.2 établie (`docs/PARITE-v2.2.2.md`) |

## Modules — couche I (intention) / B (code)

| ID | Module | Spec I | Code | Tests | Statut |
|----|--------|--------|------|-------|--------|
| M-DSP-01 | FFT radix-2 | `I/dsp/I-fft.md` | `src/RoomEq.Core/Dsp/Fft.cs` | `tests/…/FftTests.cs` | 🔒 |
| M-DSP-02 | Fenêtrage | `I/dsp/I-fenetres.md` | `…/Dsp/Windowing.cs` | `…/WindowingTests.cs` | 🔒 |
| M-DSP-03 | Filtres biquad | `I/dsp/I-biquad.md` | `…/Dsp/BiquadFilter.cs` | `…/BiquadTests.cs` | 🔒 |
| M-DSP-04 | Générateurs de bruit | `I/dsp/I-bruits.md` | `…/Dsp/NoiseGenerator.cs` | `…/NoiseTests.cs` | 🔒 |
| M-DSP-05 | Balayage log (ESS) | `I/dsp/I-balayage.md` | `…/Dsp/LogSweepGenerator.cs` | `…/SweepTests.cs` | 🔒 |
| M-DSP-06 | Outils spectre | `I/dsp/I-spectre.md` | `…/Dsp/SpectrumTools.cs` | `…/SpectrumTests.cs` | 🔒 |
| M-DSP-07 | Mixage canaux | `I/dsp/I-mixage-canaux.md` | `…/Dsp/ChannelMixer.cs` | `…/ChannelMixerTests.cs` | 🔒 |
| M-EQ-01 | Bandes EQ | `I/eq/I-bandes.md` | `src/RoomEq.Core/Eq/EqBand.cs` | `…/EqTests.cs` | 🔒 |
| M-EQ-02 | Chaîne EQ temps réel | `I/eq/I-chaine-eq.md` | `…/Eq/EqChain.cs` | `…/EqChainTests.cs` | 🔒 |
| M-EQ-03 | Presets EQ | `I/eq/I-presets.md` | `…/Eq/EqPreset.cs` | `…/EqTests.cs` | 🔒 |
| M-EQ-04 | Liaisons preset ↔ périphérique | `I/eq/I-presets-peripherique.md` | `…/Eq/DevicePresetStore.cs` | `…/DevicePresetStoreTests.cs` | 🔒 |
| M-CAL-01 | Courbe cible | `I/calibration/I-courbe-cible.md` | `…/Calibration/TargetCurve.cs` | `…/TargetCurveTests.cs` | 🔒 |
| M-CAL-02 | Analyse de balayage | `I/calibration/I-analyse-balayage.md` | `…/Calibration/SweepAnalyzer.cs` | `…/SweepAnalyzerTests.cs` | 🔒 |
| M-CAL-03 | Planificateur de bandes | `I/calibration/I-plan-bandes.md` | `…/Calibration/AutoBandPlanner.cs` | `…/AutoBandPlannerTests.cs` | 🔒 |
| M-CAL-04 | Multi-signaux | `I/calibration/I-multi-signaux.md` | `…/Calibration/MultiSignalAnalysis.cs` | `…/MultiSignalTests.cs` | 🔒 |
| M-CAL-05 | Précision correction | `I/calibration/I-precision.md` | `…/Calibration/CorrectionPrecision.cs` | `…/CorrectionPrecisionTests.cs` | 🔒 |
| M-CAL-06 | Design de correction | `I/calibration/I-design-correction.md` | `…/Calibration/CorrectionDesigner.cs` | `…/CorrectionDesignerTests.cs` | 🔒 |
| M-CAL-07 | Profils micro | `I/calibration/I-profils-micro.md` | `…/Calibration/MicProfile*.cs` | `…/MicProfileLibraryTests.cs` | 🔒 |
| M-CAL-08 | Catalogue signaux test | `I/calibration/I-catalogue-signaux.md` | `…/Calibration/TestSignalCatalog.cs` | `…/MeasurementSignalsTests.cs` | 🔒 |
| M-HPH-01 | Presets casque (AutoEq) | `I/headphones/I-presets-casque.md` | `…/Headphones/HeadphonePresetLibrary.cs` | `…/HeadphonePresetLibraryTests.cs` | 🔒 |
| M-IO-01 | Version test/public | `I/io/I-saveur-app.md` | `…/Io/AppFlavor.cs` | `…/AppFlavorTests.cs` | 🔒 |
| M-IO-02 | Journal diagnostic | `I/io/I-journal.md` | `…/Io/DiagnosticsLog.cs` | `…/DiagnosticsTests.cs` | 🔒 |
| M-IO-03 | Télémétrie Drive | `I/io/I-telemetrie.md` | `…/Io/TelemetryConfig.cs` + `DriveLogUploader.cs` | `…/TelemetryTests.cs` | 🔒 |
| M-IO-04 | Magasin de signaux | `I/io/I-magasin-signaux.md` | `…/Io/SignalPack.cs` + `SignalStore.cs` + `SignalCatalogClient.cs` | `…/SignalPackTests.cs` + `SignalStoreTests.cs` | 🔒 |
| M-IO-05 | Magasins de réglages | `I/io/I-magasins.md` | `…/Io/Stores.cs` | `…/StoresRenameTests.cs` | 🔒 |
| M-IO-06 | Export FabFilter Pro-Q 3 | `I/io/I-export-fabfilter.md` | `…/Io/FabFilterProQ3.cs` | `…/IoTests.cs` (FabFilterProQ3Tests) | 🔒 |
| M-AUD-01 | Capture | `I/audio/I-capture.md` | `src/RoomEq.Audio/Capture/CaptureEngine.cs` | `…/MeasurementEngineTests.cs` (inertie) | 🔒 |
| M-AUD-02 | Lecture | `I/audio/I-lecture.md` | `…/Playback/AudioOutputs.cs` + `SampleProviders.cs` + `PlaybackEngine.cs` + `TestSignalCatalog.cs` | `…/SampleProvidersTests.cs` + `SignalCatalogTests.cs` + `MeasurementEngineTests.cs` | 🔒 |
| M-AUD-03 | Routage / mixeur | `I/audio/I-routage.md` **v2** (anti-boucle au routage) | `…/Routing/` (12 fichiers : RingBuffer, TrackSource, AudioDeviceCatalog, VirtualAudioProviders, MixerStore, VolumeSyncPolicy, EndpointVolumeController, DefaultDeviceSetter, AudioDeviceWatcher, DriverSetupManager, ConsoleChannel, ConsoleMixer) | `…/RingBufferTests.cs` + `ConsoleRoutingTests.cs` + `SignalCatalogTests.cs` + `RoomEqDriverTests.cs` | 🔒 |
| M-AUD-04 | Pont ASIO | `I/audio/I-pont-asio.md` | `…/Asio/AsioBridgeInterop.cs` + `AsioBridgeHost.cs` + `AsioManager.cs` | `…/AsioBridgeTests.cs` | 🔒 |
| M-AUD-05 | Pont macOS | `I/audio/I-pont-macos.md` | `…/Mac/CoreAudioApi.cs` + `CoreAudioCatalog.cs` + `CoreAudioOutput.cs` + `CoreAudioCapture.cs` + `MacAudioBridge.cs` | `…/DeviceBridgeTests.cs` | 🔒 |
| M-AUD-06 | Moteur de mesure | `I/audio/I-moteur-mesure.md` | `…/Measurement/RoomMeasurementEngine.cs` | `…/MeasurementEngineTests.cs` | 🔒 |
| M-AUD-07 | Greffon AudioUnit macOS | `I/audio/I-greffon-au.md` | **natif** `natif/roomeq-au/` (N-AU-01 : cœur C++ + couche d'édition portable `edit/` + AUv2 ausdk + vue Cocoa éditable + CI macOS/auval) | natifs : `natif/roomeq-au/tests/` (15 671 vérifications : parité bit à bit RoomEq.Core, écriture JSON, banque casques, éditeur, auto-chargement) + CI `plugin-au` (auval) | 🔒 |
| M-AUD-08 | Greffon VST 2 32 bits Windows | `I/audio/I-greffon-vst.md` **v3** (ABI verrouillée : rangs AEffect exacts + offsets statiques) | **natif** `natif/roomeq-vst/` (N-VST-01 : ABI VST 2 autodéclarée + cœur C++ réutilisé de roomeq-au + éditeur Win32 + mini-hôte) | natifs : `natif/roomeq-vst/tests/` (430 vérifications, dont vue hôte externe de l'ABI) + mini-hôte ABI (41) + CI `plugin-vst` (PE32/exports) | 🔒 |
| M-APP-01 | Shell + MVVM | `I/app/I-shell.md` | `src/RoomEq.App/…` (Program, App, MainWindow, MainViewModel, TestSignalMenu, OutputPresetCoordinator) | `…/OutputPresetCoordinatorTests.cs` + `…/SecondaryTabsTests.cs` | 🔒 |
| M-APP-02 | Onglet calibration | `I/app/I-onglet-calibration.md` | `…/Views/CalibrationTab…` + `ViewModels/CalibrationViewModel.cs` + `CalibrationOptions.cs` + `LiveMicMonitor.cs` | `…/SecondaryTabsTests.cs` | 🔒 |
| M-APP-03 | Onglet mixage | `I/app/I-onglet-mixage.md` | `…/Views/MixerTab…` + `ViewModels/MixerViewModel.cs` + `.Routing` + `.Volume` + `.Driver` + `.Persist` + `ChannelViewModel.cs` + `TrackEqViewModel.cs` | `…/BandTrackChannelTests.cs` | 🔒 |
| M-APP-04 | Onglet EQ | `I/app/I-onglet-eq.md` | `…/Views/EqTab…` + `ViewModels/EqViewModel.cs` + `EqViewModel.FabFilter.cs` + `HeadphonePickerViewModel.cs` + `BandViewModel.cs` | `…/EqViewModelTests.cs` (dont FabFilter) + `…/HeadphonePickerTests.cs` | 🔒 |
| M-APP-05 | Onglets micro / analyseur / ASIO | `I/app/I-onglets-secondaires.md` | `…/Views/MicTab…` + `AnalyzerTab…` + `AsioTab…` + `ViewModels/MicViewModel.cs` + `.Curve` + `AnalyzerViewModel.cs` + `AsioViewModel.cs` + `RollingBuffer.cs` + `Controls/` (PlotControl, VuMeter, EqControl, MicCurveEditor, dialogues) | `…/MicViewModelTests.cs` + `…/RollingBufferTests.cs` + `…/SecondaryTabsTests.cs` | 🔒 |

## Modules installateur — specs prêtes, code futur (vague 8, D11)

> Specs I rédigées par rétro-ingénierie de l'installateur scellé v2.2.2
> (D11) : le contrat est figé et testable AVANT le recodage. Le binaire
> `RoomEqDriverSetup-2.2.2.exe` reste la référence exploitable jusqu'à
> l'exécution de la vague 8 (uniquement après validation Windows de la v3
> et décision de signature). Les invariants verrouillent les trois défauts
> historiques : 0xE0000205 (INF), 0x6F8 (interop), libellés pnputil FR.

| ID | Module | Spec I | Code | Statut |
|----|--------|--------|------|--------|
| M-INS-01 | Séquence d'installation | `I/installateur/I-sequence-installation.md` | futur `src/RoomEq.Installer/InstallSequence.cs` | ⬜ spec prête |
| M-INS-02 | Contrat Win32 PnP | `I/installateur/I-contrat-win32.md` | futur `…/Win32Pnp.cs` | ⬜ spec prête |
| M-INS-03 | Contrat INF du pilote | `I/installateur/I-contrat-inf.md` | futur `…/InfContract.cs` | ⬜ spec prête |
| M-INS-04 | Contrat outils externes | `I/installateur/I-contrat-outils.md` | futur `…/ToolContract.cs` | ⬜ spec prête |
| M-INS-05 | Consentement mode test | `I/installateur/I-consentement-mode-test.md` | futur `…/TestModePolicy.cs` | ⬜ spec prête |
| M-INS-06 | Désinstallation / mode normal | `I/installateur/I-desinstallation-mode-normal.md` | futur `…/UninstallSequence.cs` | ⬜ spec prête |
| M-INS-07 | Charge utile + ASIO | `I/installateur/I-charge-utile-asio.md` | futur `…/Payload.cs` + `AsioRegistration.cs` | ⬜ spec prête |
| M-INS-08 | Interface graphique / affichage | `I/installateur/I-interface-graphique.md` | futur `…/UiSink.cs` + `Gui/` | ⬜ spec prête |

## Module prospectif — EQ système par APO (proposition session 19)

> Contourne l'exigence Microsoft de signature des pilotes noyau : le mode
> « tout le son d'une sortie Windows traverse l'EQ » passe par un Audio
> Processing Object user-mode chargé par `audiodg.exe` — sans pilote, sans
> mode test, sans dépendance tierce (VB-CABLE). Analyse complète et options
> comparées dans `docs/EQ-SYSTEME-APO.md` ; décision **D24 proposée, en
> attente de validation** avant lancement de la vague 9.

| ID | Module | Spec I | Code | Statut |
|----|--------|--------|------|--------|
| M-AUD-09 | EQ système (APO Windows) | `I/audio/I-apo-systeme.md` | futur `src/RoomEq.Audio/SystemApo/` + **natif** `natif/roomeq-apo/` (N-APO-01) | ⬜ spec prête |

## Blocs natifs scellés (hors périmètre de recodage — D1)

| ID | Composant | Version scellée | Référence |
|----|-----------|-----------------|-----------|
| N-DRV-01 | Pilote audio virtuel Windows (roomeqvad.sys + INF + CAT) | v2.2.2 | `natif/REFERENCE-NATIF.md` |
| N-ASI-01 | DLL ASIO (RoomEqAsio64.dll + bridge_core) | v2.2.2 | idem |
| N-MAC-01 | Greffon HAL CoreAudio macOS (RoomEqHAL.driver + pkg) | v2.2.2 | idem |
| N-INS-01 | Installateurs (MSI WiX, DriverSetup GUI, AppSetup) | v2.2.2 | idem — **contrats reverse-spec : `I/installateur/` (M-INS-01..08, vague 8 future)** |

## Convention de statut

- ⬜ non commencé · 🚧 en cours · ✅ recodé + tests verts + spec à jour ·
  🔒 cristallisé (tag git, contrat gelé)
- Un module passe en 🔒 uniquement par tag de version mineure, ou par
  pré-version de cristallisation (`-dev.N`, D10) — les contrats I restent
  gelés jusqu'à l'ouverture de la version finale.
- **Cristallisation finale (D12-D14)** : les 42 modules applicatifs sont 🔒
  aux tags `v3.0.0`→`v3.0.2` (correctifs successifs de la CI : verrou fichier
  Windows, puis portabilité packaging — binaire identique) — dégel réservé
  aux patchs 3.0.x à contrat stable (R4) ou à la vague 8.
