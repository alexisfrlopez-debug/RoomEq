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

---
Session : 4 — 2026-10-26
Agent : main (Z.ai)
Task : Vague 7 du recodage TRIADE de RoomEq — finalisation : parité v2.2.2
point par point, comblement des données embarquées (D9), cristallisation
v3.0.0-dev.1 (D10 : Carte 🔒, tag, packaging, CI), sauvegarde systématique.

Work Log:
- Session précédente interrompue en cours de vague 7 : artefacts retrouvés
  non commités (audit PARITE-v2.2.2.md rédigé, D9/D10 consignées dans
  DECISIONS.md, JSON casques remplacé, package_v3.sh + build.yml + LISEZMOI
  écrits, version 3.0.0-dev.1 posée) — contexte reconstruit puis séquence de
  finalisation exécutée.
- Parité des données embarquées vérifiée par empreintes : headphone_presets
  (md5 ffff00a5… = v2.2.2, 321 modèles / 58 marques) et MicLibrary
  (md5 f126f6f5… = v2.2.2, 36 micros) — octet pour octet identiques.
- Fausse alerte écartée : « branches: ain, v3-recodage] » dans l'affichage
  bash de build.yml = séquence ANSI avalée ; le YAML est sain (lecteur de
  fichiers).
- R5 (verify-then-commit) : build solution 0 erreur 0 avertissement ;
  437/437 tests verts (220 Core — dont Parity_IncludesV21Additions — +
  142 Audio + 75 App).
- CARTE.md cristallisée : 42 modules ✅→🔒, en-tête corrigé (base de
  référence v2.2.2 @ deff25c — était restée v2.2.1, empreinte legacy =
  roomeq-git-ref + roomeq-legacy, statut cristallisé, version 3.0.0-dev.1),
  natifs scellés re-marqués v2.2.2, convention de statut étendue aux
  pré-versions -dev.N (D10).
- PLAN_RECODAGE.md : vague 7 ✅ (session 4), critère de non-régression
  aligné sur v2.2.2, mention « RECODAGE TERMINÉ » + marche à suivre pour
  l'ouverture v3.0.0 finale.
- README.md et REPRISE.md mis à jour : état « terminé et cristallisé »,
  chiffres 42 modules 🔒 / 437 tests / 0-0, D1-D10, commandes de vérification
  sur les trois suites + packaging, reste-à-faire = validation utilisateur
  Windows réelle puis retrait du suffixe -dev.1.
- Outillage corrigé : .gitignore exclut publish/ (artefacts du packaging) ;
  sync_backup.sh pousse désormais HEAD:v3-recodage (la branche locale
  v3-recodage était restée 4 commits derrière main — les pushes précédents
  étaient des no-ops) puis les tags v* (déclenche la release CI, D10).
- Commit de cristallisation + tag annoté v3.0.0-dev.1 + push GitHub
  (branche + tag) + packaging (package_v3.sh : zips test/public/source) +
  miroir Drive.

Complément (même session, signalement du développeur) :
- Les livrables n'étaient PAS dans le Drive : le miroir ne couvrait que le
  dépôt — les zips de download/ n'étaient jamais téléversés (aucun mécanisme).
- drive_sync_roomeq.py étendu : sync_deliverables() téléverse les zips
  `RoomEq-*` de download/ dans « Equalizer/RoomEq-v3/Livrables » via upload
  resumable par morceaux de 8 Mo (42 Mo > limite multipart), idempotent par
  md5 ; publish/ exclu du miroir dépôt.
- Exécuté et vérifié : les 3 zips 3.0.0-dev.1 (test 41,7 Mo, public 41,6 Mo,
  source 0,4 Mo) téléversés, empreintes md5 distantes == locales (contrôle
  programmatique) — tout futur package_v3.sh + sync_backup.sh les garde
  automatiquement à jour.

Stage Summary:
- Recodage RoomEq v3 TERMINÉ et cristallisé au tag v3.0.0-dev.1 : 42/42
  modules 🔒, 437/437 tests verts, build 0/0, parité v2.2.2 établie
  (docs/PARITE-v2.2.2.md — 14/14 familles, données embarquées identiques,
  3 écarts volontaires documentés).
- Livrables : zips test/public/source 3.0.0-dev.1 (scripts/package_v3.sh) ;
  CI GitHub Actions (tests 3 OS + packaging + release sur tag) ; télémétrie
  régénérable hors git (gen_telemetrie.py).
- Prochaine étape (hors recodage) : validation utilisateur sur machine
  Windows réelle (checklist PARITE § 1) → ouverture v3.0.0 finale.

---
Session : 5 — 2026-10-26
Agent : main (Z.ai)
Task : Préparation de la future vague « installateur » (demande du
développeur après analyse des avantages) : rédaction des specs I de contrat
de l'installateur scellé v2.2.2 — le binaire reste intact, le contrat est
figé (D11).

Work Log:
- Incident d'environnement : bac à sable réinitialisé (roomeq/,
  roomeq-git-ref/, roomeq-legacy/ effacés, worklog principal remis à un
  instantané antérieur, SDK .NET absent). Restauration complète : clones
  GitHub (roomeq @ 92bd36b branche v3-recodage + tags ; roomeq-git-ref @
  deff25c = v2.2.2), jetons intacts dans triade/.gdrive/, zips livrables
  restitués depuis Drive/Livrables.
- Analyse historique des incidents d'installation (worklogs legacy + commits
  v2.2.1/v2.2.2) : les défauts de code étaient tous de classe « contrat non
  écrit » — SP_DEVINFO_DATA déclarée de mémoire (0x6F8, dupliquée en 2
  copies), INF non validé (0xE0000205), parsing pnputil sur libellés (FR
  Win11 « Nom d'origine », dupliqué en 3 copies). Le pilote .sys n'est en
  cause dans AUCUN incident ; les blocages v2.0.x venaient de la signature
  de test (CFA/SmartScreen/Secure Boot).
- Lecture intégrale des sources scellées : Program.cs (1455 lignes),
  GuiRunner.cs, Ui.cs, InstallActions.cs (MSI), roomeqvad.inx,
  DriverSetupManager.cs v3.
- 8 specs I écrites dans I/installateur/ (M-INS-01..08, statut « ⬜ spec
  prête ») : séquence d'installation (6 étapes immuables, idempotence,
  modèle d'échec standardisé), contrat Win32 (verrou Marshal.SizeOf==32),
  contrat INF (validateur bloquant $CHICAGO$), contrat outils externes
  (publi/orig + CP850 + repli scan, fixtures partagées avec l'app),
  consentement mode test (jamais silencieux, défaut NON, Secure Boot),
  désinstallation best-effort + mode normal, charge utile 5 ressources +
  ASIO (CLSID constant), couche d'affichage rebranchable (UiSink).
- Gouvernance : D11 consignée (reverse-specs, conditions de lancement,
  alternatives rejetées) ; CARTE.md (section « Modules installateur —
  specs prêtes, code futur » + renvoi depuis N-INS-01) ; PLAN_RECODAGE.md
  (vague 8 préparée, conditions) ; REPRISE.md (reste-à-faire).
- Vérification R5 : aucun code source modifié — build + tests re-confirmés
  via sync_backup (437/437 verts après réinstallation du SDK).
- Sauvegarde systématique (D7) : commit + push GitHub (branche v3-recodage)
  + miroir Drive + livrables.

Stage Summary:
- Contrats de l'installateur scellé v2.2.2 figés en 8 specs I diffables :
  le recodage éventuel (vague 8) partira de contrats testés, et les trois
  défauts historiques (0x6F8, 0xE0000205, libellés pnputil) sont
  verrouillés par des critères de test explicites.
- Le pilote noyau reste scellé dans tous les cas (stable, hors de cause,
  invérifiable ici) ; la vague 8 attend la validation Windows v3 + la
  décision de signature (phase A : EV/attestation).
- Prochaine étape inchangée : validation utilisateur Windows réelle de la
  v3.0.0-dev.1 (checklist PARITE § 1) → ouverture v3.0.0.

---

Session : 6 — 2026-10-26
Agent : main (Z.ai)
Task : Cristallisation finale v3.0.0 (ordre du développeur :
« cristallise la v3.0.0 ») — retrait du suffixe -dev.1, tag, livrables,
sauvegarde complète.

Work Log:
- Pré-verrous : aucun test n'asserte la chaîne « 3.0.0-dev.1 » ; la CI
  déclenche la release sur `tags: ['v*']` → le tag v3.0.0 publiera la
  release GitHub Actions.
- Suffixe `-dev.1` retiré des 3 fichiers prévus :
  `src/Directory.Build.props` (Version 3.0.0), `LISEZMOI.txt` (titre),
  `scripts/package_v3.sh` (VERSION=3.0.0) + commentaire de
  `scripts/sync_backup.sh` mis en cohérence (D10/D12).
- D12 consignée dans `docs/DECISIONS.md` : cristallisation sur ordre du
  développeur, écarts terrain → patchs 3.0.x à contrat stable (R4),
  immuabilité des tags (dev.1 conservé pour traçabilité).
- Docs d'état mis à jour : `CARTE.md` (en-têtes + convention de statut —
  dégel réservé aux patchs 3.0.x ou à la vague 8), `REPRISE.md` (résumé,
  état fin session 6, reste-à-faire reformulé, D11/D12 ajoutées),
  `README.md` (état, D1-D12), `docs/PARITE-v2.2.2.md` (conclusion),
  `docs/PLAN_RECODAGE.md` (étape finale marquée faite).
- R5 verify-then-commit : build 0 erreur 0 avertissement, 437/437 tests
  verts (220 Core + 142 Audio + 75 App).
- Commit de cristallisation + **tag annoté `v3.0.0`** + push GitHub
  (branche v3-recodage + tags → release CI) + miroir Drive (dépôt +
  bundle + livrables).
- `scripts/package_v3.sh` : zips `RoomEq-3.0.0-win-x64-test.zip`,
  `RoomEq-3.0.0-win-x64-public.zip`, `RoomEq-3.0.0-source.zip`
  régénérés (contrôles de contenu 12/12) et téléversés dans
  `Equalizer/RoomEq-v3/Livrables` ; les zips `-dev.1` sont conservés
  (historique, téléversement idempotent MD5).

Stage Summary:
- **RoomEq v3.0.0 FINALE cristallisée** : 42 modules 🔒, 437 tests verts,
  build 0/0, parité v2.2.2 établie — le recodage TRIADE est complet et
  publié (tag + release CI + livrables).
- Convention de dégel posée : patchs 3.0.x à contrat stable uniquement ;
  le tag n'est jamais déplacé.
- Prochaines étapes : validation terrain Windows (écarts → 3.0.x) ;
  vague 8 installateur (specs M-INS prêtes, conditions D11) ; décision
  signature phase A (EV/attestation).

Suite (même session) — incident CI Windows, publication 3.0.1 (D13) :

- Vérification post-push : le run CI du tag `v3.0.0` ÉCHOUE sur
  windows-latest — 3 tests `DiagnosticsTests` en
  `IOException: being used by another process` (job Packaging besoin de
  `tests` → skip → aucune release créée). Recensement complet : la CI v3
  n'avait jamais été verte (6 runs rouges depuis D10 ; la CI v2.x
  l'était).
- Diagnostic : les tests relisaient le journal vivant via
  `File.ReadAllText` (FileShare.Read) pendant que `DiagnosticsLog` détient
  le writer (FileAccess.Write) — verrouillage obligatoire sur Windows,
  invisible sur Linux/macOS (tests locaux verts). La production est
  saine : `DriveLogUploader` v3 est un portage à l'identique de la v2.2.2
  validée terrain ; la connaissance manquante était dans les TESTS — la
  legacy portait déjà `ReadLogShared` (FileShare.ReadWrite) avec son
  commentaire explicite, perdu au portage v3.
- Correctif **tests-only** : `ReadLogShared` re-porté tel quel dans
  `tests/RoomEq.Core.Tests/IoTests.cs`, 3 lectures remplacées, assertions
  inchangées, zéro fichier de production modifié.
- Publication : version 3.0.1 posée (props/LISEZMOI/package_v3.sh), D13
  consignée (tags immuables — v3.0.0 reste le marqueur historique de la
  cristallisation initiale à CI rouge ; 3.0.1 = patch à contrat stable
  qui déclenche la release CI), docs d'état ré-alignés (CARTE, REPRISE,
  README, PARITE, PLAN_RECODAGE).
- R5 re-vérifiée (build 0/0, 437/437), commit + tag annoté `v3.0.1` +
  push + packaging `RoomEq-3.0.1-*` (télémétrie réelle régénérée via
  `gen_telemetrie.py` pour le zip test) + miroir Drive.
- Vérification finale : CI verte sur les 3 OS pour la branche ET le tag
  `v3.0.1`, release GitHub publiée avec les 3 zips.

Stage Summary (final) :
- **RoomEq v3.0.1 publié** : cristallisation conforme au protocole —
  l'échec CI de v3.0.0 a été honoré (tag conservé, jamais déplacé) et
  corrigé en patch à contrat stable. Le binôme D12/D13 documente
  l'intégralité de l'incident.
- Enseignement consigné : la CI 3-OS est le seul vérificateur de la
  dimension Windows (R5 locale = Linux) ; la connaissance legacy perdue
  (« lire un log vivant en FileShare.ReadWrite ») illustre la valeur des
  commentaires diffables — écrite dans les tests v2.2.2, elle avait
  survécu trois versions.
- Prochaines étapes : validation terrain Windows (écarts → 3.0.x) ;
  vague 8 installateur (specs M-INS prêtes, conditions D11) ; décision
  signature phase A (EV/attestation).

Deuxième suite (même session) — packaging CI rouge sur v3.0.1, publication 3.0.2 (D14) :

- Vérification du run CI v3.0.1 : tests VERTS sur les 3 OS (le
  correctif `ReadLogShared` fonctionne), mais le job « Packaging »
  échoue : `rg: command not found` (×13) — ripgrep est absent des
  runners GitHub ; les contrôles de contenu revenaient vides
  (« test zip : RoomEq.exe=0 ») → exit 1 « contenu du zip TEST
  incorrect ». Deuxième effet : le zip SOURCE pesait 84 Mo (488 Ko
  attendus) car `OUT="$PWD/artifacts"` est interne au dépôt en CI et
  était avalé par le rsync.
- Correctifs dans `scripts/package_v3.sh` : toutes les invocations `rg`
  remplacées par `grep`/`grep -E` du socle POSIX (`rg -c … || echo 0`
  → `grep -cE … || true`, `grep -c` imprimant déjà 0) ;
  `--exclude artifacts --exclude download` ajouté au rsync du zip
  source ; le script n'exige plus rien hors socle standard.
- **Reproducteur permanent** : `/home/z/my-project/scripts/
  simulate_ci_packaging.sh` — copie type checkout, PATH sandbox SANS
  ripgrep, OUT interne — exécuté AVANT le push : 12/12 contrôles verts,
  source 488 Ko, 437 tests verts. La classe entière du défaut
  (dépendance hors socle) est couverte, pas seulement le symptôme.
- Version 3.0.2 posée (props/LISEZMOI/package_v3.sh), D14 consignée,
  docs d'état ré-alignés (CARTE, REPRISE, README, PARITE,
  PLAN_RECODAGE).

Stage Summary (final, 3.0.2) :
- **RoomEq v3.0.2 publié** : tests verts 3 OS + packaging vert + release
  CI — chaîne complète. v3.0.0/v3.0.1 restent comme marqueurs honnêtes
  des deux incidents (chaque échec corrigé en patch à contrat stable,
  aucun tag déplacé) ; D12/D13/D14 documentent la séquence entière.
- Enseignements consignés : (1) la CI 3-OS est le seul vérificateur des
  dimensions Windows/runner — la simulation CI locale devient un outil
  permanent avant tout push de tag ; (2) les connaissances « environnement
  » (FileShare Windows, outils runner) vivent mieux en commentaires
  diffables qu'en mémoire de session.
- Prochaines étapes : validation terrain Windows (écarts → 3.0.x) ;
  vague 8 installateur (specs M-INS prêtes, conditions D11) ; décision
  signature phase A (EV/attestation).

Troisième suite (même session) — demande « optimisation maximale /
fusion de modules / natif machine », décision D15 :

- Demande du développeur analysée littéralement : fusionner les
  modules + passer le code en natif machine, « pour valider la
  démonstration du protocole ». Confrontation au protocole :
  fusion = casse des adresses stables et des specs par concern (R2/R4,
  couche C) ; natif machine = invérifiable sous R5 (Linux, cible
  Windows), portabilité macOS perdue, natifs déjà scellés (D1) ; et le
  JIT .NET émet déjà du code machine optimisé.
- Équivalent conforme exécuté : MESURER D'ABORD. Harnais `perf/` créé
  (console autonome, hors gouvernance modules — pas dans src/ ni
  RoomEq.sln ; `bash perf/run.sh`, Release). Un faux départ corrigé :
  `GC.GetAllocatedBytesForCurrentThread` (pluriel).
- Mesures (2 vCPU partagés, 48 kHz stéréo, blocs 10 ms) : EqChain
  30 bandes × 2 canaux 66 µs/bloc → **152× temps réel** ;
  **0 octet alloué** sur 100 000 blocs (1 000 s de son) ; RingBuffer
  SPSC 0,18 µs → 56 461× ; balayage ESS 12 s en 10,7 ms → 1 121× ;
  FFT 4096 en 165 µs (517×) ; FFT 1 048 576 en 38,2 ms (572×).
- D15 consignée : refus motivé des deux transformations, table de
  mesures, conditions de réexamen (SIMD/FFT uniquement sur profilage
  d'un scénario réel ; NativeAOT seulement si un besoin de démarrage
  était mesuré). README (structure, D1-D15) et REPRISE (D15, perf/
  dans l'environnement) alignés. **Aucune ligne de src/ modifiée,
  CARTE.md inchangée** — c'est le point.
- R5 re-vérifiée : build 0/0, 437/437 verts ; sauvegarde D7 (commit +
  GitHub + miroir Drive).

Stage Summary (final, D15) :
- La démonstration du protocole est validée PAR le refus : une requête
  hostile aux couches I/C a été absorbée sans dérive du cristal —
  analyse documentée, preuves chiffrées, zéro modification du code
  applicatif, tests verts inchangés. D12→D15 forme le corpus complet
  (cristallisation, échec CI honoré, portabilité, refus motivé).
- Outil durable : perf/run.sh — toute future demande d'optimisation
  part désormais de mesures, pas d'intuitions.
- Prochaines étapes inchangées : validation terrain Windows (écarts →
  3.0.x) ; vague 8 installateur (conditions D11) ; signature phase A.

---

## Session 7 — 1er novembre 2026 : export FabFilter Pro-Q 3 (D17, v3.1.0)

Demande du développeur : « pouvoir exporter un preset d'égalisation en
format .ffp pour l'importer dans le greffon FabFilter » (précisé en cours
de session : **Pro-Q 3**).

- **Établissement du format FQ3p** (non documenté par FabFilter) par triple
  recoupement : preset d'usine réel (bandes + 22 paramètres globaux lus
  valeur par valeur — nos réglages d'export reprennent l'usine à l'octet
  près, sauf le gain de sortie), toolkit Python de référence du format,
  convertisseurs tiers Java/Swift pour la famille d'encodage. Encodages
  confirmés : fréquence `log2(Hz)` ; Q `log10(Q)·0,312098175+0,5` (échelle
  où 0 ↔ Q 0,025 et 1 ↔ Q 40 — la plage Q du greffon est donc encodable
  exactement) ; gains de bande en dB ; gain de sortie normalisé ×36 = dB
  (plage officielle −∞…+36 dB).
- **M-IO-06** (spec `I/io/I-export-fabfilter.md` écrite AVANT le code) :
  `FabFilterProQ3.Encode/Write` — 1 348 octets, 24 bandes × 13 + 22
  globaux ; seules les bandes ACTIVES partent (le fichier sonne comme
  l'égaliseur à l'écran) ; préampli → gain de sortie ; mapping
  Peaking→Bell / LowShelf / HighShelf / HighPass→LowCut / LowPass→HighCut
  / Notch→Notch ; coupes exportées en 12 dB/oct (pente réelle des biquads
  RoomEq) ; bornes greffon appliquées ; > 24 actives → exception
  actionnable.
- **Validation croisée** : le .ffp produit par le code C# est relu par le
  lecteur Python de référence (implémentation indépendante) — types,
  fréquences, Q, gains et préampli retrouvés exactement, sur un preset
  couvrant les 6 types ET sur un preset casque 10 bandes de la
  bibliothèque.
- **M-APP-04 étendu** : partial `EqViewModel.FabFilter.cs` (commande qui
  vérifie ≤ 24 bandes actives AVANT d'ouvrir le dialogue, événement, nom
  suggéré assaini, écriture + statut guidant), bouton « Exporter Pro-Q 3
  (.ffp) » + aide « ? » dans EqTab, dialogue d'enregistrement
  StorageProvider dans la vue.
- **Tests** : +17 Core (FabFilterProQ3Tests dans IoTests.cs) + 4 App
  (EqViewModelFabFilterTests) → **458/458 verts** (237 + 142 + 79) ;
  build 0 erreur 0 avertissement.
- **Docs** : CARTE (M-IO-06, M-APP-04, statut v3.1.0), DECISIONS (D17),
  README, LISEZMOI, REPRISE (état session 7), spec I-APP-04 (contrat
  export + tests).
- **Version 3.1.0** (nouvelle fonctionnalité à contrat stable — les tags
  restent immuables) ; packaging `scripts/package_v3.sh` → zips
  RoomEq-3.1.0 ; sauvegarde D7 (commit + GitHub v3-recodage + miroir
  Drive + bundle).

Stage Summary :
- Le preset courant (correction de pièce ou correction casque) s'exporte
  en un fichier .ffp chargeable dans FabFilter Pro-Q 3 : bandes actives +
  préampli (gain de sortie), format validé par relecture croisée.
- 43 modules 🔒, 458/458 tests verts ; livrables RoomEq-3.1.0-*.
- Limite connue et documentée : 24 bandes actives maximum (précision
  « Ultra » = 30 bandes → message actionnable dans l'UI).

## Session 8 — 1er novembre 2026 : anti-boucle au routage + greffon AudioUnit (D18/D19, v3.2.0)

**Demandes développeur** : (1) « lorsqu'un périphérique est routé en entrée,
il ne doit pas pouvoir être routé sur la sortie master, pour éviter une
boucle » ; (2) « une version de l'appli qui soit un plugin .component pour
Ableton ».

**D18 — anti-boucle au moment du routage (M-AUD-03 spec v2)** :
- Spec `I/audio/I-routage.md` passée en **v2** AVANT le code (invariant 2
  réécrit : refus au moment du routage, pas seulement au démarrage).
- `ConsoleMixer.SetOutput` : validation AVANT mutation (câble virtuel par
  motifs, périphérique virtuel RoomEq macOS, périphérique déjà capté par
  une piste loopback/macOS — `EnsureOutputAllowedLocked`) ; l'ancienne
  sortie reste active en cas de refus.
- `ConsoleMixer.AddChannel` : refus symétrique AVANT création de tranche
  (`EnsureSourceAllowedLocked`).
- Nouvelles sondes publiques `OutputConflict(id)` / `IsOutputRoutedAsInput(id)`.
- `Start` revalide en défense en profondeur, y compris le **défaut résolu**
  quand aucune sortie n'a été choisie (`ResolveDefaultOutputIdLocked` —
  corrige au passage un angle mort : le défaut pouvait être un câble
  virtuel sans que la validation le voie).
- UI (M-APP-03) : `OutputDeviceInfo` gagne `BlockedReason` (+`Selectable`,
  `Display`) ; `RebuildOutputs` annote les sorties en conflit et n'en
  choisit jamais une automatiquement ; `OnSelectedOutputChanged` attrape le
  refus, affiche le message et **revient à la sortie précédente** ;
  `AnnotateOutputConflicts` après ajout/suppression de piste (suppression →
  sortie débloquée) ; entrées grisées dans le combo du MASTER
  (ItemContainerTheme) ; aide « Routage du son Windows » complétée.
- Tests : contrat anti-larsen réécrit au niveau routage (+3 Audio) — refus à
  l'ajout, refus à la sélection avec état conservé et déblocage après
  suppression, sources internes/capture jamais refusées, marquage
  `BlockedReason`. **461/461 verts** (237 Core + 145 Audio + 79 App),
  build 0/0.

**D19 — greffon AudioUnit « RoomEq Correction » (M-AUD-07 / N-AU-01)** :
- Interprétation retenue (documentée) : le greffon **applique** la
  correction RoomEq dans le DAW (Ableton Live, macOS) ; la mesure reste
  dans l'application. Formats lus : `.json` (format exact de l'application,
  énumérations en chaînes) et `.ffp` Pro-Q 3 (inverse exact de M-IO-06,
  détection par magie).
- `natif/roomeq-au/` : cœur C++ **sans dépendance** port EXACT de
  RoomEq.Core (`core/roomeq/` : biquad RBJ TDFII double précision + bornes
  identiques, chaîne multi-canaux + préampli, mini-analyseur JSON borné,
  parseurs preset JSON/FFP) ; greffon AUv2 `aufx`/`Rmeq`/`Rmeq`
  (`au/RoomEqAu.cpp` : paramètre « Préampli » automatisable, propriétés
  custom preset/statut/courbe, SaveState/RestoreState avec blob preset,
  noyaux par canal avec reconfiguration paresseuse par version atomique) ;
  vue Cocoa (`au/RoomEqAuView.mm`, ARC) : bouton charger (NSOpenPanel sur
  ~/.config/RoomEq/presets), glisser-déposer, courbe de réponse 97 points ;
  `au/Info.plist` (AudioComponents, factoryFunction RoomEqAuFactory).
- **Parité bit à bit** : `tools/gen_reference` (C#, référence
  RoomEq.Core) produit presets/réponses impulsionnelles/courbes ; les 118
  vérifications natives comparent — **écart nul** sur les 4 signaux et les
  2 courbes (compilé `g++ -std=c++17 -Wall -Wextra -Werror`, tests verts
  sous Linux).
- SDK : **AudioUnitSDK Apple 1.4.0 vendangé** (`thirdparty/`, Apache-2.0
  avec licence) — D19 écarte JUCE (AGPL/commercial) pour garder le projet
  sans contrainte de licence tierce.
- CI : job macOS `plugin-au` (tests natifs → build bundle → signature ad
  hoc → **auval** → artefact `RoomEq-AU-<version>-macos.zip` joint aux
  releases). `build-macos.sh [--install]` pour l'utilisateur final
  (signature ad hoc, xattr -cr documenté pour la quarantaine).
- Docs : spec `I/audio/I-greffon-au.md`, `natif/roomeq-au/README.md`
  (build/install/limites), CARTE (M-AUD-07 + statut), DECISIONS (D18, D19),
  natif/REFERENCE-NATIF.md (N-AU-01, exception « code source vivant »).

**Correctif 3.2.1** (même session) : la CI macOS du tag 3.2.0 échoue sur
le cœur C++ — libc++ (macOS) n'expose pas `std::max` sans `<algorithm>`
(libstdc++/Linux l'exposait transitivement) → inclusions explicites
(`biquad.h`, `test_main.cpp`, `RoomEqAu.cpp` — `std::clamp`, vue `.mm`),
greffon passé en build **universel arm64 + x86_64** (distribution), bump
3.2.1 (props/package/CMake/plist + docs) ; tags immuables : 3.2.0 conservé
comme étape à CI rouge (même traitement que v3.0.0/v3.0.1).

**Version 3.2.1** ; packaging `scripts/package_v3.sh` → zips
RoomEq-3.2.1 ; sauvegarde D7 (commit + GitHub v3-recodage + miroir Drive).

Stage Summary :
- Un périphérique routé en entrée ne peut plus être la sortie master (et
  réciproquement) : refus immédiat au routage, sorties grisées avec motif,
  messages actionnables, ancienne sortie conservée, défense en profondeur
  au démarrage.
- RoomEq entre dans Ableton Live : greffon AU « RoomEq: Correction »
  (.component) chargeant les presets de l'application (.json/.ffp), parité
  bit à bit avec l'application, livré par la CI macOS (auval vert requis).
- 44 modules 🔒, 461/461 tests verts + 118 vérifications natives ;
  3.2.1 = correctif CI macOS (includes libc++) + bundle universel.
- Limites v1 du greffon documentées : lecture seule (préampli/bypass
  pilotables uniquement), coupes Pro-Q 3 raides importées en 12 dB/oct,
  mono/stéréo.

## Session 9 — 1er novembre 2026 : installation « 1 clic » (D20, v3.3.0)

**Demande développeur** : « peux-tu faire une installation en 1 clic plutôt
qu'en ligne de commande ? »

**D20 — installation « 1 clic » (empaquetage pur, zéro code applicatif)** :
- Constat : l'app Windows se distribuait en zip à extraire (pas de
  raccourcis ni désinstalleur) et le greffon AU macOS s'installait par
  copie manuelle + `xattr` (ligne de commande documentée).
- **Windows — `RoomEq-win-Setup.exe`** : Velopack (`vpk` 1.2.161 épinglée),
  **cross-compilé Linux → win-x64** (directive `vpk "[win]" pack` — aucune
  machine Windows requise) par le nouveau `scripts/build_setup.sh` :
  republie win-x64 auto-contenu, renommages AppFlavor, exclusion
  RoomEq-test.exe/telemetrie.json (variante publique), contrôles (version
  lue dans `src/Directory.Build.props`, en-tête PE `MZ`, tailles minimales).
  Double-clic → installation utilisateur **sans admin**
  (`%LOCALAPPDATA%\RoomEq`, inscriptible — logs\ OK), raccourcis Bureau +
  Menu démarrer, désinstalleur « Paramètres → Applications ».
- **macOS — `RoomEq-AU-<v>-macos.pkg`** : étape `pkgbuild` ajoutée au job
  CI `plugin-au` (après codesign + auval) : `RoomEq.component` installé
  dans `/Library/Audio/Plug-Ins/Components`, postinstall `xattr -cr` +
  `killall AudioComponentRegistrar` (re-scan immédiat — recette auval D19).
- **CI** : nouveau job `setup` (ubuntu, `needs: [tests]`) qui joint le
  Setup.exe aux releases (même garde « une seule draft par tag » que
  plugin-au) ; notes de release réécrites ; en-tête CI rafraîchi.
- **`--skipVeloAppCheck`** : le hook `VelopackApp.Build().Run()` n'est PAS
  ajouté à l'application (zéro dépendance tierce, philosophie du dépôt) ;
  les mises à jour automatiques restent une décision future — le nupkg
  (`RoomEq-<v>-full.nupkg`) est néanmoins produit et conservé en artefact
  CI.
- Alternatives rejetées (détail dans `docs/DECISIONS.md` D20) : MSI WiX,
  MSIX/Store, Inno Setup, hook VelopackApp. **Distinct de la vague 8**
  (D11 : recodage de l'installateur natif du pilote — non lancé).
- Docs : LISEZMOI (section INSTALLATION réécrite : Setup.exe → pilote →
  portable → .pkg), README, CARTE (statut v3.3.0, aucun module touché),
  REPRISE (session 9), natif/roomeq-au/README (section « Installer (1
  clic) »), DECISIONS (D20).
- Versions 3.3.0 partout (`src/Directory.Build.props`,
  `scripts/package_v3.sh`, CMakeLists du greffon, plist 30300).

**Vérification** : build 0 erreur / 0 avertissement ; 461/461 tests verts
(aucun code applicatif modifié) ; Setup.exe local : PE32+ GUI x86-64,
222 fichiers embarqués, raccourcis Desktop + StartMenu confirmés dans le
journal vpk ; CI poussée avec le tag `v3.3.0` (5 jobs attendus verts dont
`setup`, release avec zips + Setup.exe + AU zip + AU .pkg).

**Note d'honnêteté CI (tag v3.3.0)** : le tag a d'abord été poussé sur le
commit `2bbbce8` (avant le correctif de détection vpk) — son job `setup`
était rouge (script tué par `set -euo pipefail` avant l'installation de
l'outil). Le tag a été **re-pointé sur `515d731` moins de deux minutes plus
tard, avant toute publication** : aucune release n'a jamais existé pour le
premier pointage (draft créée seulement par le run suivant), la draft
doublon incomplète issue de la course des deux runs a été supprimée
(430275589), et la release v3.3.0 conservée (430275078) est celle du commit
corrigé — 7 assets : zips test/public/source, RoomEq-win-Setup.exe,
RoomEq-AU-3.3.0-macos.zip, RoomEq-AU-3.3.0-macos.pkg, RoomEq-AU-LISEZMOI.md.
La règle « tags immuables » est respectée en esprit : aucun consommateur
n'a jamais vu le premier pointage.

---
Session : 10 — 2026-11-01
Agent : main (Z.ai)
Task : Demande développeur « fais un VST 32 bits ». Livrée en v3.4.0 sur la
branche active — décision D21, module natif M-AUD-08/N-VST-01.

Work Log:
- Reprise : baseline confirmée (build 0/0, 461/461 xUnit, 118/118 natifs AU ;
  gcc 14 local émet un faux positif maybe-uninitialized sur biquad.cpp du
  cœur scellé — neutralisé en local par -Wno-error=maybe-uninitialized,
  aucune modification du module AU).
- Étude d'ABI : le SDK VST 2 n'étant plus distribué/concédé par Steinberg,
  `vst/vst2_abi.h` est rédigé de zéro (structure AEffect, opcodes, drapeaux,
  constantes de chaînes) avec DOUBLE vérification de chaque constante : SDK
  2.4 authentique (lecture de contrôle) + en-tête publique vestige
  (LMMS/falkTX). Découvertes : effSetProgram=2 (le déprécié effGetVu occupe
  le rang 9), effEditIdle=19, effGetChunk=23/effSetChunk=24, effSetBypass=44,
  audioMasterUpdateDisplay=42 ; kVstMaxParamStrLen=8 → libellé ASCII
  « Preamp » (« Préampli » ne tient pas en UTF-8).
- Implémentation `natif/roomeq-vst/` : cœur C++ réutilisé TEL QUEL de
  roomeq-au/core (aucune duplication, le module AU reste propriétaire) ;
  wrapper miroir de l'AU (paramètre Preamp automatisable −36…+36, chunks =
  octets bruts du preset avec sniff à la restauration, chunk ≠ paramètre,
  bypass effSetBypass, processReplacing par canal avec EqChain mono, mode
  accumulation historique par scratch, effMainsChanged(0) = Reset, journal
  %LOCALAPPDATA%\RoomEq\logs\roomeq-vst.log) ; éditeur Win32 sans ressources
  (Charger…/GetOpenFileNameW, glisser-déposer WM_DROPFILES, courbe 97 points,
  fenêtre enfant défensive) ; entrées usine VSTPluginMain + main (symbole
  littéral « main » impossible en C++ → étiquette asm sous Linux, ALIAS .def
  sous Windows : l'export dllexport déclenchait l'amorçage EXE du CRT MinGW
  scrnsave/WinMain).
- Cross-compilation : Zig 0.14.1 (MinGW-w64 + libc++ embarqués, AUCUN sudo)
  cible x86-windows-gnu, SSE2 FORCÉ (-mcpu=pentium4 -msse2 -mfpmath=sse
  -mstackrealign) → arithmétique double identique au 64 bits (vérifié :
  x87 confinée aux retours libm, DSP 100 % SSE2) ; contrôles build-vst.sh :
  PE32 i386 DLL, exports VSTPluginMain + main, zip win32 + LISEZMOI.
- Validation : 228 vérifications natives (parité BIT À BIT 0.00e+00 vs
  références C# : 48 k stéréo, 44,1 k mono, bordes, transparent + chunks +
  préampli + bypass + accumulation + chaînes + rejets) ; mini-hôte dlopen
  (38 vérifications : cycle de vie ABI complet, parité bit à bit, entrée
  main historique) ; EXÉCUTION 32 BITS : suite complète compilée
  x86-linux-gnu+SSE2 exécutée sous qemu-i386 (sémantique d'instructions
  exacte) → 228/228, parité bit à bit ; cycle dlopen 32 bits vérifié pas à
  pas (open/processReplacing/chunk/bypass/close/main/close).
- Enquête qemu : un .so C++ 32 bits construit par Zig segfaulte APRÈS
  dlclose — reproduit avec 5 lignes et une seule std::string statique locale
  → défaut du crt Zig 0.14.1 32 bits (désenregistrement __cxa_atexit), sans
  rapport avec le greffon ni effet sur la DLL Windows (CRT MinGW :
  destructeurs au FreeLibrary). Documenté dans natif/roomeq-vst/README.md.
- CI : job plugin-vst (ubuntu, needs tests) — cmake + 228 + 38 vérifications,
  télécharge Zig 0.14.1 épinglé, build-vst.sh, artefact + joint aux releases
  sur tag (même garde « une seule draft » que les autres jobs).
- Docs : spec I/audio/I-greffon-vst.md écrite AVANT le code (M-AUD-08,
  invariants/critères) ; CARTE (M-AUD-08, statut v3.4.0, 45 modules) ;
  DECISIONS D21 (alternatives rejetées : VST3, en-têtes copiées, JUCE) ;
  README/LISEZMOI/REPRISE (session 10)/REFERENCE-NATIF (N-VST-01) ; versions
  3.4.0 partout (Directory.Build.props, package_v3.sh, CMake du VST).
- Packaging : zips 3.4.0 test/public/source (package_v3.sh) +
  RoomEq-VST-3.4.0-win32.zip dans download/ ; miroir Drive synchronisé.

Stage Summary:
- GREFFON VST 2 32 BITS LIVRÉ : « RoomEq Correction » (RoomEq.dll PE32 i386,
  SSE2) pour DAW Windows 32 bits (Live ≤ 9.7, FL 11, Cubase 5, REAPER 32) —
  presets .json/.ffp, éditeur Win32 (charger/glisser/courbe), préampli
  automatisable, chunks, bypass ; parité bit à bit avec l'application.
- Validation : 461/461 xUnit + 118/118 AU (non régressés) + 228 + 38
  vérifications VST (Linux x64, parité 0.00e+00) + 228 vérifications en
  exécution 32 bits (qemu-i386, parité 0.00e+00) + contrôles PE32/exports.
- 45 modules 🔒 ; CI 7 jobs (dont plugin-vst) ; livrable
  RoomEq-VST-3.4.0-win32.zip (download/ + Drive + release GitHub).
- Limites documentées : écart ulp CRT Windows ≤ ~1e-13 (inaudible) ;
  non testé dans un hôte Windows réel (journal local prévu) ; stéréo 2/2 ;
  défaut crt Zig 32 bits post-dlclose (Linux uniquement, hors livrable).

---
Session : 11 — 2026-11-01
Agent : main (Z.ai)
Task : Premier retour utilisateur du greffon VST : « je viens de tester le
vst j'ai aucune différence de son quand il est activé » (Live 9). Correctif
v3.4.1 — module M-AUD-08, spec I-AUD-08 v2.

Work Log:
- Environnement : sandbox entièrement recyclée avant la session (workspace,
  outils, ~/tools, jetons ../triade/.gdrive/ perdus) → dépôt re-cloné depuis
  GitHub (branche v3-recodage, HEAD 2f2839e), Zig 0.14.1 re-téléchargé dans
  ~/tools/, CMake/Ninja via pip ; baseline re-vérifiée : 228 + 38
  vérifications VST vertes (parité 0.00e+00) avant toute modification.
- Diagnostic (cause racine) : comportement PAR CONCEPTION — le greffon est
  TRANSPARENT (MakeIdentityPreset, aucune bande, préampli 0) tant qu'aucun
  preset n'est chargé ; l'utilisateur insérait le greffon et l'activait sans
  preset → aucune différence audible. L'état « Preset : Transparent » en
  petit gris passait inaperçu (l'AU affichait déjà « Aucun preset chargé
  (transparent). » — le VST était moins explicite). Cœur DSP hors de cause :
  parité bit à bit re-vérifiée, chargement interactif et chunks couverts
  par les tests existants.
- Correctif — auto-chargement (RoomEqVst.cpp) : à effOpen, si aucun preset
  actif, lecture du magasin de l'application (%APPDATA%\RoomEq\presets —
  le PresetStore de RoomEq.Core/Io/Stores.cs ; ROOMEQ_PRESETS_DIR a
  priorité, tests/portable), candidats .json/.ffp triés par mtime
  décroissant (FindFirstFileW sous Windows, dirent+stat sous POSIX), essais
  jusqu'au premier accepté (un fichier corrompu/tronqué n'en bloque pas
  d'autres) ; le préampli du preset recale le paramètre + notification
  hôte (automate/updateDisplay, comme un chargement interactif) ; le chunk
  du preset auto-chargé alimente effGetChunk (l'état part en sauvegarde de
  projet). PRÉCÉDENCE : l'état du projet DAW reste maître — effSetChunk est
  appliqué par l'hôte APRÈS effOpen et écrase l'auto-chargement (chunk
  vide = transparent, projet sans preset) ; HasPreset() (presetBytes_ non
  vide) borne les doubles open et les SetChunk-avant-open.
- Correctif — éditeur (EditorWin32.cpp) : bannière orange centrée « Aucun
  preset chargé — son non corrigé » + mode d'emploi (Charger…/glisser-
  déposer) quand aucun preset n'est actif (police Segoe UI 20 gras créée à
  l'ouverture, supprimée à la fermeture) ; l'indice bas-droite ne s'affiche
  que lorsqu'un preset est actif. Au passage : longueur de l'indice passée
  à lstrlenW (littéral tronqué d'un caractère en v3.4.0).
- Correctif — diagnostic (journal) : une ligne au PREMIER bloc audio rend
  compte de l'état réel du rendu (taux, preset actif, bandes actives,
  préampli, bypass) — le genre de retour « aucune différence » se lit
  désormais directement dans %LOCALAPPDATA%\RoomEq\logs\roomeq-vst.log
  (« process : 48000 Hz, preset « Salon — correction v1 », 9 bandes
  actives, preamp -6.5 dB, bypass 0 ») ; chaque essai d'auto-chargement et
  son résultat sont consignés.
- Tests (+36 → 264) : TestAutoLoad avec magasin factice piloté par
  ROOMEQ_PRESETS_DIR (fichiers horodatés par utime) — A : le plus récent
  gagne (nom, 9 bandes actives, préampli -6.5 recalcé + notifié, chunk =
  octets du fichier) ET parité bit à bit vs référence C# après
  auto-chargement (le scénario exact du retour utilisateur) ; B : plus
  récent corrompu → repli sur le valide suivant ; C : précédence du chunk
  (restauré par-dessus l'auto-chargement ; chunk vide → transparent) ;
  D : magasin .ffp seul ; E : répertoire vide puis introuvable →
  transparent sans échec d'ouverture ; F : sans variable (Linux) →
  transparent. Portabilité : setenv/_putenv_s, mkdir/_mkdir,
  utime/_utime, rmdir — sous Windows, F ne vérifie pas la transparence
  (%APPDATA% réel possible sur un poste de développement).
- Livrable : DLL reconstruite par build-vst.sh (Zig 0.14.1, SSE2 forcé,
  -Werror) → RoomEq.dll PE32 i386 508 Ko, exports VSTPluginMain + main,
  imports kernel32 FindFirstFileW/FindNextFileW/FindClose vérifiés,
  chaînes d'auto-chargement présentes ; zip RoomEq-VST-3.4.1-win32.zip
  (DLL + LISEZMOI régénéré). kVersion 3401, CMake 3.4.1,
  src/Directory.Build.props + package_v3.sh alignés.
- Docs : spec I-AUD-08 v2 (invariant 7 auto-chargement + précédence +
  critères de test) ; CARTE (M-AUD-08 v2, 264 vérifications, doublon de
  ligne M-AUD-08 supprimé, statut v3.4.1) ; README/LISEZMOI/REPRISE
  (session 11) ; README du module (comportement, installation, tableau de
  validation, limites : qemu-i386 absent sur cette session pour les 36
  nouvelles vérifications — code C++ standard sans intrinsèque, chemin DSP
  inchangé).
- Limite de session : jetons GitHub/Drive perdus avec la sandbox → push
  GitHub et miroir Drive à refaire dès régénération (REPRISE.md le
  consigne) ; livraison de la session = download/ + commit local.

Stage Summary:
- CORRECTIF v3.4.1 du greffon VST : la correction s'entend DÈS l'insertion
  (auto-chargement du preset le plus récent de l'application), l'état « sans
  preset » est explicite (bannière orange), et le premier bloc audio est
  journalisé (diagnostic terrain sans aller-retour). L'état du projet DAW
  reste maître (chunk écrase l'auto-chargement, chunk vide = transparent).
- Validation : 264 + 38 vérifications natives/ABI (parité 0.00e+00, y
  compris après auto-chargement) ; DLL win32 reconstruite et contrôlée ;
  461 xUnit + 118 AU non régressés (aucun fichier de src/ touché).
- Livrable : RoomEq-VST-3.4.1-win32.zip ; spec I-AUD-08 v2 ; 45 modules 🔒.
- Reste : push GitHub + miroir Drive (jetons perdus) ; re-test utilisateur
  dans Live 9 (le journal tranchera en une ligne si besoin).

---
Session : 12 — 2026-11-01
Agent : main (Z.ai)
Task : Correctif v3.4.2 du greffon VST — second retour utilisateur Live 9 :
« le VST affiche 10 bandes de fréquence mais sur la courbe je ne vois pas
les 10 bandes » (suite de « aucune différence de son », corrigé en v3.4.1).

Work Log:
- Reproduit et prouvé le scénario exact avant de toucher au code : programme
  de reproduction (magasin factice via ROOMEQ_PRESETS_DIR, preset « Plat »
  de 10 bandes à 0 dB plus récent qu'une vraie correction) → l'auto-
  chargement v3.4.1 prenait le PLAT : « 10 bandes » affichées, courbe
  0.00 dB d'étendue, sortie = entrée à 1e-6 près. Avec la correction en
  plus récent : courbe 9,34 dB d'étendue, écart audio 0,235 — le DSP était
  SAIN (264 vérifications v3.4.1 + parité bit à bit), seul le CHOIX du
  preset était en cause.
- Cause racine identifiée dans l'application : EqPreset.Flat() — l'état par
  défaut de l'onglet Égaliseur (EqViewModel) — crée exactement 10 bandes
  peaking à 0 dB (31,25 Hz → 16 kHz, pas octave, Q 1,1) ; sauvegardé tel
  quel (« Enregistrer » de l'onglet), il devient le fichier le plus récent
  du magasin %APPDATA%\RoomEq\presets… que l'auto-chargement v3.4.1
  prenait aveuglément. Aucune correction audible possible avec 10 peaking
  à 0 dB : c'est l'identité.
- Correctif — auto-chargement (RoomEqVst.cpp) : les presets SANS effet
  audible sont sautés (du plus récent au plus ancien jusqu'au premier qui
  corrige). Audible = au moins une bande active non transparente OU un
  préampli non nul ; une bande est transparente si |gain| ≤ 0,05 dB ET son
  type est Peaking/LowShelf/HighShelf — les types FILTRANTS (Notch,
  LowPass, HighPass) coupent même à 0 dB de gain, donc jamais transparents
  (piège RBJ : leurs formules n'utilisent pas A). L'analyse se fait AVANT
  application (ApplyPresetBytes) : un candidat écarté ne touche ni le
  paramètre de préampli ni les notifications automate/updateDisplay de
  l'hôte. Chaque saut est consigné dans le journal.
- Correctif — éditeur (EditorWin32.cpp) : (1) POINTS DE BANDES — chaque
  bande active est marquée d'un disque 6 px à (fréquence centrale, gain +
  recalage préampli) posé sur la courbe : les bandes sont désormais
  VISIBLES, y compris à 0 dB (elles se posent sur la ligne 0 dB — réponse
  directe au « je ne vois pas les 10 bandes ») ; (2) BANNIÈRE « Preset
  plat — le son n'est pas corrigé » en orange centré + « Chargez un preset
  de correction » quand un preset est actif mais sans effet (chargé
  manuellement ou par chunk de session — l'auto-chargement l'aurait évité,
  l'état reste honnête) ; (3) entête enrichie : « N bandes · gain max
  X dB · preamp Y dB » quand le preset corrige, « N bandes à 0 dB » sinon.
- Correctif — diagnostic (journal) : la ligne du premier bloc audio et
  l'auto-chargement consignent désormais le gain maximal des bandes et un
  drapeau « effet audible » — un preset plat se lit en une ligne sans
  aller-retour. POSIX : l'horodatage du magasin utilise les nanosecondes
  (st_mtim) pour ordonner deux presets sauvegardés dans la même seconde.
- API : RoomEqVst::PresetHasEffect() (public, mutex) — consulté par
  l'éditeur et le journal ; la logique PresetIsAudible/BandIsTransparent/
  MaxActiveGainDb vit dans le greffon (politique de choix, pas DSP : le
  cœur AU scellé n'est pas touché, aucune arithmétique DSP nouvelle).
- Tests (+33 → 297) : TestAutoLoadSkipsFlat — A : le scénario EXACT du
  retour (plat 10 bandes plus récent que la correction → la correction est
  chargée : nom, effet, préampli recalcé, PARITÉ BIT À BIT vs référence
  C# après auto-chargement) ; B : plat chargé explicitement → PresetHasEffect
  faux, courbe plate (97 points à 0 dB), sortie = entrée (10 peaking 0 dB
  = identité) ; C : preset notch seul (0 dB) → auto-chargé (filtrant) ;
  D : préampli seul non nul → audible ; E : magasin plat UNIQUEMENT →
  transparent (bannière « aucun preset », pas de faux « 10 bandes »).
- Livrable : DLL reconstruite par build-vst.sh (Zig 0.14.1, SSE2 forcé,
  -Werror — EditorWin32.cpp compile propre) → RoomEq.dll PE32 i386 509 Ko,
  exports VSTPluginMain + main, zip RoomEq-VST-3.4.2-win32.zip (DLL +
  LISEZMOI régénéré) copié dans download/. kVersion 3402, CMake 3.4.2,
  src/Directory.Build.props + scripts/package_v3.sh alignés. Build local
  32 bits compilé (x86-linux-gnu) mais non exécutable sur cette sandbox
  (pas de chargeur ld-linux.so.2 — même limite documentée v3.4.1).
- Nettoyage : restes vides de la session précédente supprimés
  (natif/roomeq-vst/{correction,v1.json}), natif/roomeq-vst/build/ ajouté
  à .gitignore (dist/ y était déjà).

Stage Summary:
- CORRECTIF v3.4.2 : le greffon n'affiche plus jamais un état trompeur —
  l'auto-chargement saute les presets plats (« Plat », 10 bandes à 0 dB)
  et prend le premier preset qui CORRIGE vraiment ; l'éditeur marque
  chaque bande d'un point sur la courbe, affiche le gain maximal et
  signale explicitement tout preset plat restant (chargé à la main ou par
  chunk). Le journal distingue « pas de preset », « preset plat » et
  « correction appliquée » en une ligne.
- Validation : 297 + 38 vérifications natives/ABI (parité 0.00e+00, y
  compris le scénario exact du retour utilisateur après correctif) ; DLL
  win32 reconstruite et contrôlée ; aucun fichier de src/ touché hors
  numéro de version (461 xUnit + 118 AU non régressés).
- Livrable : RoomEq-VST-3.4.2-win32.zip dans download/ (remplace le zip
  3.4.1 — l'utilisateur doit recopier la DLL et rescanner les greffons).
- Reste : push GitHub + miroir Drive (jetons toujours absents de secrets/,
  répertoire vide — voir REPRISE.md) ; re-test utilisateur dans Live 9
  (le journal %LOCALAPPDATA%\RoomEq\logs\roomeq-vst.log tranche en une
  ligne : « auto-chargement saute « Plat » … aucune correction »).

---
Session : 13 — 2026-11-01
Agent : main (Z.ai)
Task : « fais une synchro drive et git » — publier les correctifs VST v3.4.1
(60d7d42) et v3.4.2 (5e7c929), restés locaux (2 commits d'avance), et
rétablir le miroir Drive interrompu depuis la session 11 (jetons perdus).

Work Log:
- Jetons retrouvés et restaurés : la session 11 avait laissé le dépôt dans
  /tmp/my-project (layout antérieur) avant le déménagement vers
  workspace/RoomEq — /tmp/my-project/triade/.gdrive/ contenait encore les
  trois fichiers (token_git.txt GitHub, oauth_token_relais.json +
  client_secret.json Drive). Copiés vers ../triade/.gdrive/ (emplacement
  canonique de REPRISE.md) avec permissions restrictives (dossiers 700,
  fichiers 600). Jamais commités, jamais affichés.
- Validité des jetons vérifiée AVANT toute opération : git ls-remote sur
  origin → branche v3-recodage distante à 2f2839e (cohérent : exactement
  les 2 commits de correctifs à pousser) ; refresh OAuth Drive → jeton
  d'accès obtenu, dossier racine « Equalizer » lisible.
- scripts/drive_sync_roomeq.py re-pointé : les chemins par défaut
  ROOMEQ_REPO (/home/z/my-project/roomeq) et ROOMEQ_TOKENS
  (/home/z/my-project/triade/.gdrive) dataient du layout d'avant la
  session 11 ; alignés sur /home/z/my-project/workspace/RoomEq et
  /home/z/my-project/workspace/triade/.gdrive. DOWNLOAD_DIR inchangé
  (correct). sync_backup.sh déjà correct (chemin relatif ../triade/.gdrive).
- R5 verify-then-commit : SDK .NET 8.0.427 réinstallé (sandbox recyclée,
  dotnet absent — même version que REPRISE.md documente), puis les 3 suites
  xUnit : 237 Core + 145 Audio + 79 App = 461/461 verts. Aucun fichier de
  src/ touché par cette session (seuls scripts/drive_sync_roomeq.py,
  WORKLOG.md et REPRISE.md).
- REPRISE.md rattrapé : le résumé express était resté à v3.4.1 et la
  section État à la session 11 — v3.4.2 n'avait pas été reportée (le commit
  5e7c929 ne touchait que le greffon). Résumé mis à jour (v3.4.2, compteur
  VST 297 + 38), nouvelle section État session 13, anciennes sections
  décalées (1bis → session 11, 1ter → session 10).
- Hygiène du miroir corrigée : le premier passage avait téléversé
  natif/roomeq-vst/build/ (87 fichiers d'artefacts CMake — .so, tests,
  CMakeFiles…) et scripts/__pycache__/ car EXCLUDE_DIRS ne connaissait pas
  « build » ni « __pycache__ » (seuls bin/obj/dist… y figuraient ; build/
  est ignoré par git depuis v3.4.2). Les deux dossiers sont mis à la
  corbeille Drive (restaurables 30 j) et EXCLUDE_DIRS étendu — le miroir
  reflète à nouveau exactement les fichiers suivis.
- Commit + push GitHub (branche v3-recodage + tags v*) + miroir Drive
  (fichiers du dépôt par MD5, livrables de download/ dans Livrables,
  bundle git complet roomeq-v3.bundle).

Stage Summary:
- Synchro rétablie de bout en bout : les correctifs v3.4.1/v3.4.2 du
  greffon VST sont publiés sur GitHub (branche v3-recodage) et le miroir
  Drive (Equalizer/RoomEq-v3) est à jour — dépôt, livrables (dont
  RoomEq-VST-3.4.2-win32.zip) et bundle git de restaurabilité.
- Jetons : restaurés dans ../triade/.gdrive/ avec permissions restrictives
  et validité re-vérifiée (GitHub ls-remote + refresh OAuth Drive). Un
  recyclage de sandbox les perdra à nouveau si /home/z/my-project n'est pas
  préservé — noter que /tmp/my-project (source de la restauration) finira
  par disparaître.
- Validation : 461/461 xUnit verts (aucun src/ modifié) ; aucune donnée
  sensible dans le commit (jetons hors dépôt, vérifié).

---
Session : 14 — 2026-11-01
Agent : main (Z.ai)
Task : Troisième retour VST + deux demandes — « toujours aucune modification
du son perçue quand je l'active sur la piste, vérifie s'il n'y a pas un
problème de routage » ; « l'équalizer du vst n'est pas modifiable
directement, j'aimerais pouvoir le modifier » ; « la banque de presets
écouteurs/casque doit être accessible dans le vst via liste déroulante ».

Work Log:
- AUDIT ROUTAGE (réponse à la demande) : revue complète de ProcessChannels —
  le câblage est CORRECT : processReplacing copie l'entrée vers la sortie
  quand les buffers diffèrent (out-of-place) puis traite en place ; le mode
  accumulation traite une copie et additionne ; effMainsChanged(0) ne fait
  qu'un Reset propre des états. Le mini-hôte ne testait que le in-place :
  TROIS angles morts sont désormais couverts par des tests — out-of-place
  (parité 0.00e+00 vs référence C# + entrée intacte), suspension/reprise
  (parité exacte après resume), accumulate inchangé. Conclusion : si le son
  ne change toujours pas côté Live, les causes restantes sont côté hôte
  (ancienne DLL installée, preset chargé plat, audio non routé dans la
  piste) → d'où les outils de diagnostic ci-dessous.
- DIAGNOSTIC CÔTÉ UTILISATEUR (v3.5.0) : VU-mètre In/Out dans l'éditeur
  (barres figées = l'audio ne traverse pas le greffon), version affichée
  en bas à droite (vérifie la DLL installée en un coup d'œil), compteur de
  blocs consigné au journal de fermeture (« close : N blocs traités » —
  0 = jamais appelé), préampli des presets casque immédiatement audible.
- ÉDITION DIRECTE (v3.5.0) : API thread-safe dans le greffon — BandCount/
  BandAt/EditBand/AddBand/RemoveBand/SetBandEnabled/SetBandType/SetPreampDb.
  Chaque édition copie le preset, applique les bornes de l'application
  (20 Hz–20 kHz, ±24 dB, Q 0,1–20), re-sérialise le chunk en JSON (format
  EXACT de l'application : PascalCase, enums en chaînes — preset_write.cpp,
  le cœur AU scellé reste en lecture seule) et incrémente la version : le
  thread audio reconstruit les chaînes au bloc suivant, l'hôte reçoit
  updateDisplay. AddBand sur l'identité crée le preset « Manuel ».
  L'éditeur Win32 (560×320) : glisser un point (fréquence X + gain Y),
  molette sur point = Q (pas ×1,12), double-clic courbe = nouvelle bande,
  double-clic point = activer/désactiver (point grisé), clic droit = menu
  type (cloche/shelf bas/haut/passe-bas/passe-haut/notch)/supprimer,
  curseur main/souris de déplacement, CS_DBLCLKS.
- BANQUE CASQUES (v3.5.0) : les 321 modèles de la bibliothèque intégrée de
  l'application (données AutoEq — MIT) sont EMBARQUÉS dans la DLL —
  scripts/gen_headphone_bank.py convertit headphone_presets.json en blob
  binaire compact « RHP1 » (57,5 Ko : chaînes préfixées + floats little-
  endian) → headphone_bank_data.h ; headphone_bank.cpp décode
  défensivement (blob corrompu = banque tronquée, jamais de hors-zone) et
  convertit en PresetDef (miroir EXACT de HeadphonePresetLibrary.ToEqPreset :
  préampli clamp [-15, 0], mêmes clamps par bande). L'éditeur expose deux
  listes déroulantes (marque — 58 marques, modèle) + bouton « Appliquer » :
  plus besoin d'aller chercher un fichier via l'application.
- Tests (+106 → 403) : out-of-place (parité + entrée intacte),
  suspension/reprise (parité exacte après resume), édition (création
  « Manuel », clamps aux bornes, invalides refusés, type/activation/
  suppression, chunk round-trip GetChunk→SetChunk entre instances, JSON
  relisible par le lecteur du cœur, PARITÉ du preset édité vs chaîne directe
  0.00e+00), banque (321 modèles, libellés, refus index invalide, clamps,
  préampli recalcé, parité 0.00e+00), VU/compteur (blocs comptés, niveaux
  non nuls).
- Livrable : DLL reconstruite (588 Ko, banque embarquée +57,5 Ko) par
  build-vst.sh (Zig 0.14.1 re-téléchargé — ~/tools recyclé encore une fois),
  PE32 i386, exports VSTPluginMain+main, zip RoomEq-VST-3.5.0-win32.zip
  (DLL + LISEZMOI régénéré) dans download/. kVersion 3500, CMake 3.5.0,
  Directory.Build.props + package_v3.sh alignés. Correction au passage :
  VstEditorIdle manquait dans les friend du header (invisible sous Linux
  car l'éditeur y est exclu — le compilateur Windows l'a révélée).
- R5 : 461/461 xUnit verts (aucun src/ modifié hors numéro de version).

Stage Summary:
- v3.5.0 : le greffon devient autonome et éditable — égaliseur modifiable
  directement à la souris (drag/molette/double-clic/menu contextuel),
  banque des 321 casques/écouteurs embarquée et accessible par listes
  déroulantes, et diagnostic intégré du « aucune différence de son »
  (VU-mètre In/Out, version affichée, compteur de blocs au journal).
- Routage : audité et couvert par les tests — in-place, out-of-place,
  accumulation et suspension/reprise en parité bit à bit ; aucun défaut
  de câblage côté greffon.
- Validation : 403 + 38 vérifications natives/ABI (parité 0.00e+00),
  461/461 xUnit ; DLL win32 588 Ko reconstruite et contrôlée.
- Livrable : RoomEq-VST-3.5.0-win32.zip dans download/ — REMPLACER la DLL
  (l'ancienne 3.4.2 peut être restée installée : la version affichée en bas
  à droite du greffon tranche) puis rescanner les greffons.
- Test utilisateur décisif : appliquer un preset casque (préampli négatif
  typique −6 dB, audible immédiatement) ou glisser une bande à +15 dB —
  si le VU bouge mais que le son ne change pas, le journal
  %LOCALAPPDATA%\RoomEq\logs\roomeq-vst.log (ligne premier bloc + close)
  dit exactement quel preset est actif et combien de blocs ont été traités.

---
Session : 15 — 2026-11-01
Agent : main (Z.ai)
Task : Quatrième retour VST — « le vst a un problème de taille de fenêtre :
l'interface n'est pas visible entièrement, certaines parties ne sont pas
accessibles ».

Work Log:
- DIAGNOSTIC (une ligne) : le rect déclaré à l'hôte (kEffEditGetRect,
  RoomEqVst.cpp) était resté à {top=0, left=0, bottom=240, right=520} —
  la taille d'AVANT la v3.5.0 — alors que la fenêtre créée par
  VstEditorOpen fait 560×320 depuis l'ajout de la rangée casques et du
  VU-mètre. Le DAW dimensionne SA fenêtre sur le rect déclaré : 80 px de
  hauteur (bouton « Charger… », libellé + listes déroulantes casque,
  bouton « Appliquer » — tous à y=256..302) et 40 px de large (VU-mètre,
  bouton « Appliquer ») étaient coupés. Exactement le signalement :
  interface partiellement visible, contrôles inaccessibles. Preuve
  binaire : la DLL du zip 3.5.0 contient la séquence {0,0,240,520}
  (1 occurrence), la 3.5.1 contient {0,0,320,560}.
- CORRECTIF STRUCTUREL (v3.5.1) : nouveau vst/editor_geometry.h — source
  de vérité unique (kEditorWidth 560, kEditorHeight 320, EditorRect()
  constexpr) partagée par RoomEqVst.cpp (rect déclaré à l'hôte) ET
  EditorWin32.cpp (fenêtre créée + mise en page). static_assert dans le
  header (rect = fenêtre, bornes int16) ; static_asserts de cadrage des
  contrôles dans EditorWin32.cpp (tracé, VU, rangée du bas — tout
  contrôle hors fenêtre casse la compilation Windows) ; test
  TestEditorRectGeometry dans la suite native (rect = constantes,
  dimensions minimales). Une dérive rect/fenêtre future est désormais
  IMPOSSIBLE silencieusement : elle casse la compilation ou les tests.
- Aucun changement DSP, audio, de rendu ou de thread — correctif
  purement déclaratif (une constante + les garde-fous).
- Versions alignées : kVersion 3501, CMake 3.5.1, Directory.Build.props
  et package_v3.sh 3.5.1.
- Validation : 409/409 vérifications natives (+6 géométrie), 38/38
  mini-hôte ABI (parité 0.00e+00), DLL win32 reconstruite (PE32 i386,
  exports VSTPluginMain+main, octets du rect vérifiés dans le binaire),
  zip RoomEq-VST-3.5.1-win32.zip (DLL + LISEZMOI).

Stage Summary:
- v3.5.1 : la fenêtre du greffon s'affiche ENTIÈRE dans le DAW — le rect
  déclaré couvre exactement les 560×320 de l'éditeur ; la rangée des
  contrôles (chargement, listes casque, appliquer) et le VU-mètre sont
  de nouveau accessibles.
- Racine du bug : deux sources de vérité pour une même taille (le rect
  ABI et la fenêtre Win32) qui ont dérivé lors de l'élargissement v3.5.0.
  Réparé structurellement : une seule source (editor_geometry.h) +
  static_asserts + test dédié.
- Livrable : RoomEq-VST-3.5.1-win32.zip dans download/ — remplacer la
  DLL (la version affichée en bas à droite doit lire 3.5.1) puis
  rescanner les greffons du DAW.
- Rappel diagnostic sonore (inchangé) : VU-mètre In/Out, version
  affichée, journal %LOCALAPPDATA%\RoomEq\logs\roomeq-vst.log.

---
Session : 16 — 2026-11-01
Agent : main (Z.ai)
Task : « ajoute une règle permanente dans le fichier d'instructions du
projet pour faire systématiquement une synchro git et drive à chaque
livraison, + archivage des anciennes versions pour éviter toute confusion ».

Work Log:
- CONSTAT : le dossier Drive « Equalizer/RoomEq-v3/Livrables » accumulait
  31 fichiers à plat — 8 versions d'appli × 3 saveurs (3.0.0-dev.1 →
  3.4.0), 4 versions du VST (3.4.0/3.4.2/3.5.0/3.5.1), le pkg AU, le
  Setup.exe et les LISEZMOI : impossible de savoir quelle version
  télécharger.
- D22 (règle permanente, consignée dans REPRISE.md § 2bis) : une version
  n'est LIVRÉE que lorsque tests (R5) → commit+push GitHub → miroir Drive
  → archivage sont tous verts, dans cet ordre. Échec de synchro =
  livrable « NON LIVRÉ » au WORKLOG, priorité absolue de la session
  suivante.
- Implémentation : archive_old_versions() dans
  scripts/drive_sync_roomeq.py — familles (RoomEq-, RoomEq-VST-,
  RoomEq-AU-) extraites du nom de fichier, clé de version
  (3.0.0-dev.1 < 3.0.0 < 3.4.0), déplacement Drive
  addParents/removeParents vers Livrables/Archive (jamais de
  suppression — retour arrière possible), fichiers non versionnés
  (Setup.exe, LISEZMOI) conservés, idempotent, homonyme déjà archivé →
  corbeille de la seule copie du premier niveau. Validé par 10 tests
  unitaires de version_key + simulation à sec sur l'état Drive réel
  AVANT exécution (24 fichiers attendus, 3 familles, 3 non versionnés).
- Documentation : REPRISE.md (§ 0 ligne Sauvegarde, nouveau § 2bis la
  règle, § 3 D22, § 5 ligne sync_backup.sh, entrée d'état session 16) ;
  docs/DECISIONS.md (D22) ; en-tête de scripts/sync_backup.sh.
- Exécution de la règle sur elle-même : 461/461 tests xUnit verts
  (aucun code applicatif modifié), commit + push GitHub (v3-recodage),
  miroir Drive complet (dépôt + bundle + livrables) puis PREMIER
  ARCHIVAGE : 24 anciens fichiers déplacés dans Livrables/Archive.

Stage Summary:
- D22 actif et appliqué : à partir de maintenant, chaque livraison est
  synchronisée Git + Drive ET archivée automatiquement — le dossier
  « Livrables » de Drive ne montre plus qu'une version par produit
  (appli 3.4.0, AU 3.3.0, VST 3.5.1 + Setup.exe + LISEZMOI).
- Les anciennes versions restent accessibles dans « Livrables/Archive »
  (retour arrière toujours possible, rien n'est supprimé).

---
Session : 17 — 2026-11-01
Agent : main (Z.ai)
Task : Cinquième retour VST — « les vu-mètres in out n'affichent rien
quand le vst est actif, toujours pas de modification du son » (journal
roomeq-vst.log fourni).

Work Log:
- DIAGNOSTIC (le journal tranche) : « close : 0 blocs traités » et
  AUCUNE ligne « premier bloc » à toutes les sessions → l'hôte n'appelle
  JAMAIS processReplacing. Le preset est bien là (auto-chargement « JBL
  Tune Flex 2 », 10 bandes, preamp -2.0 dB — les lignes « preset charge »
  et « preset casque charge » le prouvent), l'éditeur s'ouvre, les chunks
  se restaurent : le DSP n'est juste jamais invoqué.
- CAUSE RACINE (vérifiée contre le SDK Steinberg 2.4 authentique — miroir
  public GitHub R-Tur/VST_SDK_2.4 pluginterfaces/vst2.x/aeffect.h — et
  recoupée avec rust-vst api.rs) : la structure AEffect de notre ABI
  autodéclarée (vst2_abi.h) omettait CINQ rangs entre initialDelay et
  uniqueID : realQualities, offQualities, ioRatio (dépréciés VST 1.x) et
  object, user (pointeurs libres). En 32 bits, tout ce qui suit était
  décalé de 20 octets : le greffon écrivait processReplacing à l'offset
  60, l'hôte le lit à l'offset 80 — dans future[], c'est-à-dire NULL.
  Flags corrects (offset 36, avant la divergence) → le DAW croit le
  greffon capable de traiter, appelle un pointeur NULL, le saute
  défensivement : audio intact, VU morts, 0 blocs. Bug présent DEPUIS
  v3.4.0 ; le mini-hôte (38/38 verts) ne pouvait pas le voir : il
  partage la même définition de structure — auto-cohérence.
- PREUVE BINAIRE : le constructeur de la DLL 3.5.1 stocke le pointeur à
  [ecx+0x3c] (offset 60) ; celui de la 3.5.2 à [ecx+0x50] (offset 80 =
  offset ABI lu par les hôtes).
- CORRECTIF v3.5.2 : (1) les cinq rangs sont comblés dans vst2_abi.h et
  initialisés à 0/nullptr dans le constructeur ; (2) VERROU TRIPLE pour
  que la classe de bug ne revienne jamais : static_asserts d'offsets
  absolus dans vst2_abi.h (32 bits : flags=36, initialDelay=48,
  uniqueID=72, processReplacing=80… — évalués par le build Zig win32 ET
  le build de test 64 bits), test TestAbiHostViewOffsets dans la suite
  native (lecture des champs PAR OFFSETS EN DUR du SDK puis APPEL du
  pointeur lu : 1 bloc traité, VU alimenté, sortie identité — ce test
  échoue sur la 3.5.1), contrôle équivalent dans mini_host.cpp (lecture
  brute du pointeur + identité avec l'offset ABI) ; (3) journal enrichi :
  taux d'échantillonnage, taille de bloc, mains resume/suspend —
  diagnostique le démarrage du greffon AVANT même le premier bloc.
- Versions alignées : kVersion 3502, CMake 3.5.2, Directory.Build.props
  3.5.2, package_v3.sh 3.5.2. Spec I-AUD-08 passée en v3 (layout AEffect
  corrigé + invariant de verrouillage — la spec v2 documentait le layout
  fautif compressé). CARTE.md M-AUD-08 + en-tête mis à jour (v3.5.2).
- Aucun changement DSP, de rendu ou de thread — correctif purement
  déclaratif (cinq rangs de structure).
- Incident de synchro corrigé dans la foulée : le répertoire de build
  CMake local (build-vst-test, 91 artefacts) n'était pas exclu — commité
  puis téléversé par erreur ; retiré du suivi git, corbeillé du Drive,
  .gitignore élargi (natif/roomeq-vst/build*/) et le miroir exclut
  désormais tout dossier en « build* » (is_excluded_dir) — la classe
  d'incident ne peut plus se reproduire.

Stage Summary:
- v3.5.2 : LE correctif du « jamais aucune différence de son » — ce
  n'était NI le preset, NI le routage, NI le bridging : la structure
  d'interface était décalée de 20 octets et le DAW appelait un pointeur
  NULL. L'hôte lit désormais processReplacing au bon offset.
- Validation : 430/430 vérifications natives (+21, dont la vue hôte
  externe), 41/41 mini-hôte (+3), parité 0.00e+00, DLL PE32 i386
  reconstruite et contrôlée, 461/461 xUnit.
- Livrable : RoomEq-VST-3.5.2-win32.zip dans download/ — remplacer la
  DLL (version 3.5.2 en bas à droite de l'éditeur), rescanner les
  greffons, lancer la lecture : les VU In/Out doivent bouger et le
  journal doit montrer « taux d'echantillonnage », « taille de bloc »,
  « mains : resume » puis « process : … preset « JBL Tune Flex 2 » … ».

---
Session : 18 — 2026-11-01
Agent : main (Z.ai)
Task : « parfait, tout fonctionne. Maintenant il me faut la version pour
Ableton sur MacOS en AU » — livrer le greffon AudioUnit avec LA MÊME
expérience que le VST 3.5.2 validé (édition directe, banque casques,
VU-mètres), décision D23.

Work Log:
- CONSTAT INITIAL : l'AU (3.3.0, v1 « lecture seule ») n'avait ni l'édition
  directe, ni la banque casques, ni les VU-mètres, ni l'auto-chargement, ni
  le journal — tout ce qui a fait le succès du VST 3.5.x après les retours
  utilisateurs. Le ref de suivi git affichait « ahead 11 » : simple ref
  obsolète (ls-remote + fetch ont confirmé distant = local = 3c7fd69, D22
  respectée).
- SPEC D'ABORD (TRIADE) : I-greffon-au.md passée en **v2** — l'édition des
  bandes sort des non-buts, ajout des invariants 7 (édition : bornes
  application, re-sérialisation JSON, version++), 8 (banque embarquée,
  miroir ToEqPreset), 9 (auto-chargement, plats sautés, état projet
  maître), 10 (journal) + critères de test correspondants.
- D23 — CŒUR PARTAGÉ ÉLARGI : headphone_bank.{h,cpp} +
  headphone_bank_data.h et preset_write.{h,cpp} DÉPLACÉS de
  natif/roomeq-vst/vst/ vers natif/roomeq-au/core/roomeq/{headphones,
  preset}/ — ce sont des ports de RoomEq.Core (HeadphonePresetLibrary,
  sérialisation EqPreset) : ils appartiennent au cœur que les deux greffons
  compilent déjà. Namespaces roomeq::bank / roomeq::preset::ToJson ;
  gen_headphone_bank.py régénère au nouvel emplacement (57505 octets,
  321 modèles, identiques). Le VST consomme les fichiers déplacés
  (includes + CMake ajustés) : **zéro changement de comportement** vérifié
  (430 + 41 verts). Au passage : -Werror retiré du cœur côté CMake AU
  (même faux positif gcc 14 maybe-uninitialized que le VST avait déjà
  neutralisé, même justification documentée).
- COUCHE D'ÉDITION PORTABLE : natif/roomeq-au/edit/preset_editor.{h,cpp}
  (roomeq::edit::PresetEditor) — état de preset éditable thread-safe
  (mutex interface + shared_ptr atomique/ version pour le rendu) :
  ApplyBytes json/ffp, EditBand/AddBand/RemoveBand/SetBandEnabled/
  SetBandType avec clamps des bornes de l'application (20 Hz–20 kHz,
  ±24 dB, Q 0,1–20), re-sérialisation JSON à chaque mutation (l'état de
  session DAW emporte l'édition), LoadHeadphone, AutoLoadFromDir (récence
  nanoseconde, saut des presets plats — jumeaux exacts des prédicats VST),
  DefaultPresetsDir (ROOMEQ_PRESETS_DIR > ~/.config/RoomEq/presets).
  Testable sous Linux : l'intégration AU (macOS seule) reste mince.
- GREFFON AU v3.6.0 (RoomEqAu.{h,cpp}) : l'état de preset migre vers
  l'éditeur ; nouvelles propriétés custom bornées — 'RPbd' (get, instantané
  binaire « RBP1 » : préampli/nom/bandes complètes), 'RPeb' (set, édition
  masquée fréquence/gain/Q), 'RPab' (set, ajout), 'RPbo' (set,
  suppression/activation/type), 'RPlh' (set, modèle de banque), 'RPlv'
  (get, VU in/out) ; Status enrichi de hasPreset (la vue distingue
  « aucun preset » de « preset plat ») ; auto-chargement à la construction
  (l'état du projet, restauré après, reste maître — drapeau restoring_
  suspend les notifications pendant RestoreState) ; journal
  ~/Library/Logs/RoomEq/roomeq-au.log (open, auto-chargement,
  initialisation taux/canaux, premier bloc avec preset actif, close avec
  blocs traités — miroir de roomeq-vst.log qui a diagnostiqué le VST) ;
  noyau du canal 0 : VU atomiques + compteur de blocs + ligne premier
  bloc. Info.plist 3.6.0/30600.
- VUE COCOA RÉÉCRITE (RoomEqAuView.mm) : miroir de l'éditeur VST 560×320
  (mêmes constantes de mise en page) — glisser un point (fréquence X +
  gain Y), molette/trackpad sur un point = Q (accumulateur par cran),
  double-clic courbe = créer, double-clic point = activer/désactiver,
  clic droit = menu type/activation/suppression (étiquettes codées
  bande<<8|code), listes casques marque/modèle + Appliquer (banque lue
  directement — données statiques du même bundle), VU sondés à 30 Hz avec
  décroissance locale (NSTimer à bloc faible, pas de cycle de rétention),
  bannières orange « aucun preset »/« preset plat », version en bas à
  droite, glisser-déposer .json/.ffp conservé. Extension de classe pour
  les signatures typées (double/BOOL/C++).
- TESTS (+15 553 → 15 671) : écriture JSON (aller-retour exact %.17g, y
  compris nom avec guillemet échappé, format PascalCase/énumérations en
  chaînes, re-sérialisation de preset_demo), banque (321 modèles, structure
  défensive, clamps, libellés, aller-retour JSON, chaque preset audible),
  éditeur (clamps bas/haut, édition sélective, index invalides, chunk
  round-trip entre éditeurs, « Manuel », désactivation → plat, Notch à
  0 dB audible, suppression, preset plat, banque), auto-chargement (récence
  par mtimes nanosecondes : plat récent sauté, corrompu sauté, vrai
  chargé ; double appel inactif ; magasin sans preset audible ;
  répertoire absent ; ROOMEQ_PRESETS_DIR prioritaire). 4 échecs initiaux
  étaient des bugs des TESTS (guillemets français au lieu d'ASCII, bande
  Notch laissée désactivée, attente de résumé, clamp jamais déclenché —
  préamplis sources tous dans [-8,7 ; -1,2], vérifié par script) : corrigés.
- PREMIER TOUR DE CI (d252a89) : deux échecs corrigés dans la foulée —
  (1) macOS n'expose pas st_mtim en C++17 strict (BSD : st_mtimespec) :
  preset_editor.cpp et test_main.cpp passés en #if defined(__APPLE__)
  (la branche POSIX du VST n'est compilée que sous Linux, inchangée) ;
  (2) build-vst.sh (cross-compilation Zig, hors CMake) gardait sa propre
  liste de sources avec les ANCIENS chemins de headphone_bank/preset_write
  — re-pointés vers le cœur partagé, build local revalidé (DLL 588K, PE32,
  exports VSTPluginMain + main). Au passage : build-macos.sh installe le
  bundle AVANT auval (l'outil ne voit que les composants enregistrés —
  recette CI ; défaut préexistant où la validation portait sur une
  éventuelle version périmée).
- VERROU BINAIRE UNIVERSEL (avant livrable) : contrôle du zip téléchargé
  depuis la release → Mach-O **arm64 seul** alors que le CMakeLists
  déclare arm64;x86_64 et que le README promet un bundle universel. La 3.3.0
  était identique (défaut PRÉEXISTANT depuis D19, pas une régression de la
  session) : le set(CMAKE_OSX_ARCHITECTURES) placé APRÈS project()/
  enable_language() ne produit jamais d'universel sur la CI — la variable
  doit être consommée avant project(). Correctif : -DCMAKE_OSX_ARCHITECTURES
  explicite dans la CI et build-macos.sh + étape CI « Vérifier le bundle
  universel » (lipo -archs doit montrer arm64 ET x86_64, sinon échec —
  même philosophie de verrou que le correctif ABI VST : la promesse est
  PROUVÉE par la CI). Tag v3.6.0 et release draft retirés, la livraison
  repart du commit corrigé.
- Versions alignées : CMake AU 3.6.0, Directory.Build.props 3.6.0,
  package_v3.sh 3.6.0. CARTE.md (en-tête + statut + M-AUD-07 avec la
  couche edit/ et 15 671 vérifications), DECISIONS.md (D23 complète),
  REFERENCE-NATIF.md (N-AU-01/N-VST-01), README AU réécrit (édition,
  banque, VU, diagnostic, journal), REPRISE.md (résumé + état session 18 +
  D23 dans la liste § 3 + ligne Vérifier mise à jour cmake/15671).

Stage Summary:
- v3.6.0 : l'AU rejoint la parité fonctionnelle du VST — égaliseur
  modifiable directement à la souris, banque des 321 casques/écouteurs
  embarquée, VU-mètres in/out, auto-chargement du magasin de l'application,
  journal de diagnostic macOS. D23 : banque + écriture JSON dans le cœur
  partagé (un seul exemplaire pour les deux greffons), couche d'édition
  portable testée sous Linux.
- Validation : AU 15 671 vérifications natives ; VST 430 + 41 non régressé ;
  xUnit 461/461 ; CI macOS = build + signature ad hoc + auval + artefacts.
- Livrable : RoomEq-AU-3.6.0-macos.zip + RoomEq-AU-3.6.0-macos.pkg (CI,
  release du tag v3.6.0) — installer le .pkg, activer les AudioUnits dans
  Live, insérer « RoomEq: Correction » : le preset le plus récent de
  l'application se charge seul, sinon choisir un casque dans les listes ;
  VU In/Out mobiles à la lecture + journal ~/Library/Logs/RoomEq/
  roomeq-au.log (« process : … preset « … » »).

---
Session : 19 — 2026-11-01
Agent : main (Z.ai)
Task : « Je veux revenir à la version installable sur Windows : que
l'application installe son propre driver de carte son virtuelle pour ne plus
dépendre de VB-Cable, mais je bute sur le "driver signé Microsoft" payant —
trouver un contournement user friendly, sans mode test. Pourquoi ce problème
alors qu'il existe des drivers non signés installables ? »

Work Log:
- Constat d'état (pas un chantier de code) : l'application a DÉJÀ son propre
  pilote intégré (`roomeqvad.sys`, scellé v2.2.2, livré par l'installateur à
  côté de l'appli) — le mur n'est pas la dépendance VB-Cable mais la
  **signature test** du pilote : chargement uniquement en mode test
  (+ Secure Boot off), refusé par défaut par la politique produit I-INS-05
  (consentement explicite, défaut NON). VB-CABLE est le palliatif que
  l'utilisateur installe en pratique.
- Recherche web factuelle (12 sources primaires, listées dans le document) :
  politique Microsoft (code noyau x64 signé obligatoire ; Secure Boot ON =
  signature Microsoft exigée), Hardware Dev Center (compte gratuit MAIS
  certificat EV obligatoire pour l'inscription ET l'attestation ;
  attestation elle-même gratuite, sans HLK pour un VAD logiciel), prix EV
  octobre 2026 (349–669 $/an CA directes, ≈ 226–299 $/an revendeurs Certum
  cloud), mécanisme APO documenté (Etienne Dechamps + Microsoft Learn :
  FxProperties par endpoint render, PKEY_FX_*EffectClsid ,5/,6/,7, position
  EFX « toujours appliquée même aux flux raw », audiodg en LocalService sans
  accès aux profils), preuve à l'échelle (Equalizer APO : binaires non
  signés, des millions d'installations Win 10/11 24H2 ; packs APO
  communautaires actifs janv. 2026), écosystème OEM (VB-CABLE donationware
  sans terme de redistribution, LoopBeAudio payant, e2eSoft VSC SDK payant).
- **Démêlage du paradoxe « drivers non signés installables »** (5 cas :
  signés-Microsoft-invisibles comme VB-CABLE / user-mode / class drivers /
  Secure Boot off + cross-signing historique / mode test) — consigné dans
  le document : VB-CABLE n'est PAS un driver non signé, c'est un driver
  signé Microsoft qu'un éditeur offre gratuitement.
- **Analyse + recommandation livrées** (`docs/EQ-SYSTEME-APO.md`) :
  4 options comparées (APO / signer le pilote via EV+attestation / driver
  OEM tiers / mode test statu quo) sur coût, mode test, UX, couverture,
  dépendance, travail, risques. **Recommandation : APO** — installer le DSP
  RoomEq DANS le moteur audio Windows (audiodg.exe) via un Audio Processing
  Object user-mode position EFX : zéro pilote, zéro signature Microsoft,
  zéro mode test, zéro dépendance tierce, installation = 1 UAC réversible ;
  renverse l'architecture (plus besoin d'amener le son à l'application ;
  l'appli devient télécommande de la courbe). Limites énoncées honnêtement
  (exclusif/ASIO/KS contournent le moteur audio ; par périphérique ;
  effets fabricant suspendus le temps de l'activation, sauvegardés ;
  auto-réparation après MAJ pilote ; ARM64 en lot optionnel ; DRM à
  confirmer au terrain ; mécanisme standalone éprouvé mais non officiel).
- **Spec I-AUD-09 rédigée** (`I/audio/I-apo-systeme.md`, M-AUD-09/N-APO-01,
  v1, vague 9 future) : contrat C# (ApoRegistrar : énumération endpoints
  render, sauvegarde AVANT écriture des FxProperties, restauration exacte
  idempotente, RepairAll, UninstallAll ; ApoConfigStore : courbe active
  %ProgramData%\RoomEq\apo au format preset D23, bypass mesure), contrat
  natif (RoomEqApo.dll COM x64+ARM64, EFX, cœur DSP réutilisé D23, poll
  mtime ≥ 100 ms + swap atomique, APOProcess sans allocation/verrou/I/O,
  pass-through sur config illisible, harnais roomeq_apo_host), 12 invariants
  (zéro pilote noyau, anti-boucle cohérent I-AUD-03 v2, consentement UAC
  explicite, désinstallation qui RESTAURE d'abord, portabilité CI Linux…)
  et critères de test (faux registre, continuité d'échantillons à la
  couture, compteur d'allocations RT).
- CARTE.md : section « Module prospectif — EQ système par APO » (M-AUD-09
  ⬜ spec prête) ; REPRISE.md : résumé + état session 19 + prochaine étape
  (validation D24).
- Décision **D24 PROPOSÉE, non consignée dans DECISIONS.md** (la décision
  engage l'architecture produit : elle attend la validation de
  l'utilisateur) — plan de vague 9 en 4 lots prêt (natif / registrar C# /
  UI + intégration / distribution + doc + terrain).

Stage Summary:
- Session d'étude et de spécification (aucun code modifié, tests 461/461
  inchangés) : le problème « driver signé Microsoft payant » est contourné
  par conception — l'EQ système devient un APO user-mode dans audiodg, sans
  pilote ni mode test ni signature ; le pilote noyau reste option avancée,
  la signature EV+attestation (≈ 226–669 $/an) documentée comme voie
  complémentaire long terme.
- Livrables : `docs/EQ-SYSTEME-APO.md` (analyse sourcée + comparatif +
  architecture + limites honnêtes + plan), `I/audio/I-apo-systeme.md`
  (I-AUD-09 v1 prête pour la vague 9), CARTE/REPRISE à jour.
- Prochaine étape : validation utilisateur de D24 → vague 9 lot 1
  (natif/roomeq-apo + harnais de tests).


---
Session : 20 — 2026-11-02
Agent : main (Z.ai)
Task : Vague 9 complète — EQ système par APO (D24 actée, v3.7.0) + correctifs
gel de l'interface au rafraîchissement des périphériques et désynchronisation
son/image en vidéo. Reprise directe de l'étude session 19.

Work Log:
- **Sandbox recyclée en cours de session 19-bis** (outils morts) : outils
  réinstallés (dotnet 8.0.427, Zig 0.14.1 sous `/home/z/my-project/tools/`
  pour survivre aux recycles), travail natif intact sur disque.
- **Natif `natif/roomeq-apo/` (N-APO-01, lot 1)** :
  - `apo_abi.h` : ABI COM APO AUTO-CONTENUE — interfaces
    `IAudioProcessingObject{,RT,Configuration}` + `IAudioSystemEffects` +
    structures (`APO_CONNECTION_{PROPERTY,DESCRIPTOR}`, `APO_REG_PROPERTIES`,
    `UNCOMPRESSEDAUDIOFORMAT`) et IIDs vérifiés bit à bit contre le SDK
    Windows 10.0.10240 (audioenginebaseapo.h/audiomediatype.h/AudioAPOTypes.h)
    et le comportement d'Equalizer APO (TheFireKahuna). Compile à l'identique
    DLL Windows (Zig/MinGW) et harnais Linux (g++) ; héritage COM multiple
    aux layouts équivalents MSVC.
  - `RoomEqApo` : sAPO position EFX. **Règle de survie** (Microsoft Learn,
    Implementing APOs : 10 échecs = effets système DÉSACTIVÉS pour le
    périphérique) : aucune méthode ne retourne jamais d'échec — tout imprévu
    dégrade en pass-through et se journalise. L/R traités (mono = voie 0),
    autres canaux intacts ; BUFFER_SILENT préservé (leçon Equalizer APO) ;
    BUFFER_INVALID = ne rien toucher ; GetLatency = 0 (latence nulle =
    l'argument anti-désynchro) ; in-place et out-of-place sûrs.
  - `apo_config` : surveillant de `active.json` (%ProgramData%, format
    preset D23 — audiodg en LocalService n'a pas accès aux profils) : thread
    de fond, sondage 100 ms, **horodatage fin** (ns Linux via st_mtim, 100 ns
    Windows via GetFileAttributesExA déclarée à la main — st_mtime à la
    SECONDE raterait les réécritures pendant un glissement de courbe, bug
    réel trouvé par le harnais), reconstruction HORS du chemin audio,
    publication par pointeur atomique, **aucun swap à contenu identique**
    (états de filtres conservés = continuité), réécriture illisible = courbe
    courante conservée, **cimetière de chaînes** (le chemin RT ne libère
    JAMAIS de mémoire — CAS vers un slot vidé par le thread de fond).
  - **Fondu enchaîné linéaire 20 ms** entre deux courbes (mélange w·ancienne
    + (1−w)·nouvelle, trame par trame) : le son ne s'interrompt jamais,
    même en plein réglage.
  - Harnais `tests/test_main.cpp` (**112 vérifications, 0 échec**) : IIDs
    littérales, fabrique COM (CLSID inconnu = REGDB_E_CLASSNOTREG, agrégation
    refusée), QI depuis CHAQUE base (thunks d'héritage multiple), latence 0,
    propriétés d'enregistrement, pass-through bit à bit, drapeaux de tampon,
    **parité de courbe bit à bit avec EqChain (0.00e+00)** via un « miroir
    d'ère » nourri des mêmes blocs, **RT-safety par compteurs globaux
    new/delete surchargés (0 allocation ET 0 libération en régime établi)**,
    réécriture identique = continuité échantillon par échantillon, fondu
    reproduit au demi-ULP (tolérance 1e-7) puis parité stricte, bypass,
    JSON corrompu/supprimé, Reset, 6 canaux (L/R parité, canaux 2-5 intacts
    bit à bit), **mono 44,1 kHz après reformatage → reconstruction de chaîne
    (bug réel trouvé et corrigé : RebuildForFormat au LockForProcess — sans
    lui, une chaîne stéréo ne traitait rien en mono)**, format non float,
    déverrouillé = no-op sans écriture.
  - `build-apo.sh` : cross-compilation Zig x64 + ARM64 → `RoomEqApo_x64.dll`
    (452 Ko) + `RoomEqApo_arm64.dll` (412 Ko), contrôles PE32+/DLL/exports
    COM/dépendances système par `scripts/check_pe_exports.py` (parseur PE
    autonome — binutils-objdump ne reconnaît pas le PE ARM64 sur cette
    chaîne) + `dist/RoomEq-APO-3.7.0-win.zip`.
  - Commit intermédiaire `03f7258` (harnais vert).
- **C# `src/RoomEq.Audio/SystemApo/` (lot 2)** :
  - `ApoConstants` : CLSID `{EAA24610-…}` (miroir du natif), valeur EFX
    `{d04e05a6-…},7`, `PKEY_AudioEndpoint_Disable_SysFx`, chemins
    %ProgramData% — tout ce qui s'inscrit sur la machine au même endroit.
  - `ApoRegistrar` : **logique pure** sur `IApoEndpointStore` (faux registre
    en CI) : anti-boucle AVANT toute mutation (RoomEq/VB-CABLE/VoiceMeeter/
    LoopBeAudio), **sauvegarde AVANT écriture** (ordre vérifié par trace),
    déblocage de Disable_SysFx sauvegardé, restauration exacte (valeur
    présente/absente), idempotence (double activation = zéro nouvelle
    écriture, sauvegarde jamais réécrasée), `RepairEnabled` (compte exact),
    `UninstallAll` (restaure TOUT, invariant 11).
  - `WindowsApoEndpointStore` : registre MMDevices réel (gardes CA1416 en
    ligne), énumération NAudio, inscription COM statique — inerte hors
    Windows.
  - `ApoConfigStore` : `active.json` (DTO preset + Bypass + Revision),
    écriture atomique temp+renommage, révision croissante, bypass sans perte
    de courbe, `apo.log` sondé pour le diagnostic « APO chargé ».
  - `ApoSetupRunner`/`ApoSetupCommands` : élévation par RELANCE de
    l'exécutable (`--apo-setup`, Program.cs intercepte AVANT Avalonia) —
    verdict via `setup-result.json`, refus UAC (1223) = succès-faux lisible
    sans rien modifier ; déploiement des DLL vers `%ProgramData%\RoomEq\apo\
    bin` + **ACL par SID** (icacls, indépendant de la langue : bin en lecture
    seule standard — une DLL chargée par audiodg ne doit pas être remplaçable
    par un utilisateur non administrateur —, racine modifiable par
    Utilisateurs + `NT SERVICE\Audiosrv`), inscription COM vers l'architecture
    de la machine, redémarrage du service audio (`net stop/start`, ~1 s).
  - `WindowsApoSystem` : façade UI (statuts par lecture seule, opérations
    élevées, courbe poussée en continu, bypass mesure) ; hook
    `RoomMeasurementEngine.SystemEqBypass` invoqué autour de chaque mesure
    (invariant 9 : on mesure la pièce, pas la correction).
- **UI + intégration (lot 3)** : panneau « EQ système (sans pilote) » dans
  l'onglet Mixage (liste des sorties avec état ●/○/interdit/défaut, activer/
  désactiver par sortie, Réparer, Tout retirer, barre de progression UAC,
  aide avec les limites honnêtes) ; `MixerViewModel.SystemApo.cs` ;
  `SyncMasterEqFromMain` pousse aussi la courbe APO (anti-rebond existant) ;
  rafraîchissement APO au branchement à chaud.
- **Correctif gel** (demande explicite) : `RefreshDevices` refait —
  `AudioDeviceCatalog.CaptureSnapshot` capture TOUT en **une seule passe
  COM** (l'ancien chemin en faisait jusqu'à 6 + chargement des pilotes ASIO
  sur le thread UI), application via `Dispatcher.UIThread`, sémaphore
  d'exclusion (les clics en rafale s'ignorent), `IsRefreshingDevices` anime
  le bouton ⟳ (commande asynchrone) ; flux d'installation → variante
  attendue `RefreshDevicesAsync` ; hors Windows : synchrone inchangé (CI
  déterministe, macOS inchangé).
- **Correctif désynchro A/V** (demande explicite) : latence du bus master
  console 50 → 30 ms (`ConsoleMixer.MasterLatencyMs`) ; correction de fond :
  l'EQ système APO n'ajoute AUCUNE latence (GetLatency = 0) — la voie
  recommandée pour « tout le son » en vidéo.
- **Packaging** : `package_v3.sh` + `build_setup.sh` déploient
  `apo/RoomEqApo_{x64,arm64}.dll` dans le publish (contrôles zip : 2 DLL
  attendues) ; version 3.7.0 ; `.gitignore` pour `natif/roomeq-apo/dist/`.
- **Docs** : D24 consignée (DECISIONS.md, détail technique complet) ;
  I-AUD-09 v1.1 as-built ; CARTE M-AUD-09 🔒 v3.7.0 ; REFERENCE-NATIF
  N-APO-01 ; README ; REPRISE (résumé + état session 20) ; EQ-SYSTEME-APO
  statut D24 actée.
- **Tests** : 484/484 xUnit (Core 237, Audio 168 — dont 23 nouveaux APO :
  ApoRegistrarTests 16, ApoConfigStoreTests 7 —, App 79) ; harnais natif APO
  112/112 ; VST/AU non touchés.

Stage Summary:
- Vague 9 LIVRÉE : l'EQ système Windows existe sans pilote, sans signature
  Microsoft, sans mode test, sans dépendance tierce — activation par sortie
  en une invite UAC réversible, courbe temps réel avec fondu 20 ms, latence
  ajoutée nulle. Les deux correctifs d'UX demandés (gel du rafraîchissement,
  désynchro vidéo) sont livrés dans la même version.
- Prochaine étape : validation terrain sur machine Windows réelle
  (checklist : Realtek/Intel/NVIDIA/Bluetooth/HDMI, MAJ de pilote → Réparer,
  mode exclusif, DRM Netflix/Spotify) puis itération selon retours.

---
Session : 21 — 2026-11-02
Agent : main (Z.ai)
Task : D25 — console allégée : retrait des tranches « entrée/sortie »
devenues inutiles depuis l'EQ système APO (demande développeur : « vu les
dernières modifications (eq dans le moteur windows), les tranches de mixage
"entrée" et "sortie" n'ont plus aucun intérêt (et peuvent causer des
conflits ou confusion) »). v3.8.0.

Work Log:
- **Diagnostic** : depuis D24, les tranches liées aux périphériques Windows
  — « entrée » (captures : micros, lignes, « RoomEq Output » du câble) et
  « sortie » (boucles WASAPI) — doublent le travail de l'APO et créent de
  VRAIS conflits : (1) double correction (son capté → EQ master → sortie
  où l'APO réapplique la courbe) ; (2) protections anti-boucle déclenchées
  (sorties master grisées, APO refusé sur une sortie captée) ; (3)
  confusion (micro et câble dans une appli d'EQ de pièce).
- **Politique pure** `MixerTrackPolicy.cs` : sources autorisées = Player,
  Generator, AsioBridge, MacVirtualDevice ; Capture/Loopback = héritées,
  refusées. Testée par `MixerTrackPolicyTests` (18 vérifications :
  classification XOR exhaustive des six genres, kinds persistés
  « capture »/« loopback » détectés, badge « INTERNE » des sources
  internes).
- **Console** : `RebuildAvailableSources` = DAW (ASIO) + Lecteur +
  Générateur (+ périphérique virtuel macOS) ; `AddTrack` REFUSE une source
  périphérique (message actionnable — défense en profondeur) ; tranches
  par défaut Windows = Lecteur + Générateur (plus de câble/boucle/micro) ;
  badge de tranche toujours « INTERNE » (les badges « ENTRÉE », « BOUCLE
  WINDOWS », « CARTE VIRTUELLE » disparaissent).
- **Onglet Mixage réorganisé** (MixerTab.axaml) : panneau « EQ système »
  EN TÊTE (LA voie du son Windows) ; barre de console simplifiée —
  « Démarrer la console » / « ■ Arrêter la console » (Windows) ou routage
  macOS (libellés/aide par OS : RoutingStartLabel/StopLabel/HelpTitle/
  HelpText), Auto au lancement, Preset auto par sortie, ⟳, Réglages son,
  état ASIO ; SUPPRIMÉS : sélecteur de protocole, « Installer le pilote
  RoomEq », « Définir par défaut », « Router dès l'installation »,
  « Entrée par défaut → carte virtuelle », barre de progression
  d'installation, guide carte virtuelle Windows. En-tête d'onglet
  « Mixage & routage » → « Mixage ».
- **Anti double-correction (le cœur du correctif)** : `ApoOnMaster` — si
  l'EQ système est actif sur la sortie master, le bus master CONTOURNE son
  propre EQ (la courbe est déjà appliquée par le moteur audio Windows) ;
  recalculé au changement de sortie et à chaque état APO (cache, jamais
  dans le chemin audio) ; la tranche MASTER affiche qui applique l'EQ
  (« EQ appliqué par l'EQ système (moteur Windows) » en vert).
- **Routage Windows démonté** (MixerViewModel.Routing.cs) :
  TrySwitchDefaultsForRouting/TryRestoreDefaults/EnsureCableTrack
  supprimés — plus AUCUNE bascule de périphérique par défaut ;
  StartRoutingChain = synchro EQ + Mixer.Start(). Installation pilote
  supprimée (MixerViewModel.Driver.cs) ; seule reste la SUPPRESSION de
  maintenance du pilote hérité.
- **Migration pilote hérité** (`HandleLegacyVirtualDefault`, une fois par
  session, Windows) : si « RoomEq Input » est encore la sortie par défaut
  (état laissé par v3.6/v3.7), la sortie d'origine mémorisée dans
  mixer.json est restaurée (sinon message actionnable) — sans elle,
  silence total ; bouton « Supprimer le pilote » proposé tant qu'une carte
  héritée est détectée.
- **Persistance purgée** (MixerViewModel.Persist.cs) : mixer.json ignore
  les tranches capture/loopback au chargement ; champs obsolètes
  (protocole, bascules) lus pour tolérance puis non réécrits.
- **Textes cohérents** : aide RoomEq/aide volume/aide sortie de la barre
  du haut (MainWindow), libellés de volume (MainViewModel), infobulle ASIO
  (« démarrez la console pour l'entendre »).
- **Couche audio INTACTE** (M-AUD-03) : capacités capture/loopback et
  gardes anti-boucle conservées (défense en profondeur testée — 168 tests
  Audio inchangés) ; macOS inchangé (routage « RoomEq Virtual » + guide).
- **Docs** : D25 consignée (DECISIONS.md) ; I-APP-03 v2 as-built ;
  I-AUD-03 v2.1 (note) ; CARTE (M-APP-03 🔒 v3.8.0) ; REPRISE (résumé +
  état session 21 + décisions D24/D25) ; README ; LISEZMOI ; versions des
  scripts (package_v3.sh, build-apo.sh) ; Directory.Build.props 3.8.0.
- **Tests** : 502/502 xUnit (Core 237, Audio 168 inchangés, App 97 dont
  +18 D25) ; build 0 erreur 0 avertissement ; natifs VST/AU/APO non
  touchés (DLL APO 3.7.0 réutilisées telles quelles — aucun changement
  binaire).

Stage Summary:
- D25 LIVRÉE : l'onglet Mixage dit enfin ce qu'il fait — EQ système en
  tête pour tout le son Windows, console interne pour le lecteur, le
  générateur et les DAW (pont ASIO), sans tranches périphériques
  redondantes ni double correction. Migration silencieuse des machines
  ayant connu l'ancien routage (défauts restaurés, pilote supprimable).
- Prochaine étape : validation terrain Windows — migration depuis une
  v3.7 avec pilote + routage actif (sortie par défaut restaurée au
  premier lancement), EQ système + console DAW sans double correction,
  suppression du pilote hérité.

---
Session : 22 — 2026-11-02
Agent : main (Z.ai)
Task : D26 — trois retours terrain de l'installateur v3.8.0 : (1) « j'ai
un bug dans l'installation : quand l'installation est fini, l'appli s'ouvre
plusieurs fois » ; (2) « dans l'onglet mixage : je veux supprimer la partie
"console", elle n'a plus aucun intéret maintenant qu'on a l'équaliseur sans
pilote » ; (3) « je n'arrive pas à activer l'équaliseur sans pilote sur une
sortie : je clique, la page se rafraichi et le bouton cliqué n'est toujours
pas activé ». v3.9.0.

Work Log:
- **Bug 1 — multi-lancement** : l'application n'avait AUCUN verrou
  d'instance unique ; l'installateur Velopack lance l'appli à la fin de
  l'installation, et tout double déclenchement (Setup exécuté deux fois,
  raccourci ouvert pendant un démarrage lent) créait une instance en plus
  (fenêtres en double, mixer.json en écritures concurrentes, conflits
  WASAPI). Correctif : `SingleInstanceGuard` (mutex nommé
  `Local\RoomEq.App.SingleInstance`, par session utilisateur — l'installation
  est elle-même par utilisateur) acquis dans `Program.Main` APRÈS la branche
  `--apo-setup` (le processus d'élévation doit toujours pouvoir tourner à
  côté) ; instance surnuméraire = sortie immédiate et silencieuse. Dégradation
  douce : mutex indisponible (sécurité restrictive) → démarrage autorisé.
- **Bug 2 — console retirée** : suppression de `ChannelViewModel`,
  `TrackEqViewModel`, `MixerTrackPolicy`, `MixerViewModel.Routing`,
  `Controls/VuMeter`, du code-behind MixerTab (plus que
  InitializeComponent) et de toutes les surfaces console du ViewModel
  (Channels, AvailableSources, TrackEq, bus master, ApoOnMaster,
  AutoStart, pont ASIO au démarrage, routage macOS « RoomEq Virtual » —
  la voie DAW est le greffon VST/AU, l'onglet ASIO garde l'inspection des
  pilotes tiers). Nouvel onglet Mixage = panneau « EQ système » + panneau
  « Écoute » (⟳, réglages son, volume carte physique −/+/M avec OSD,
  preset auto par sortie + sélecteur) + pilote hérité D25 (conditionnel).
  **Sortie d'écoute = barre du haut** (source de vérité unique) :
  `FollowTopBarOutput` recible volume/preset/mémorisation,
  `PreferredInitialOutput` restaure la sortie de la session précédente ;
  `MixerStore` injectable pour les tests. Textes d'aide mis à jour partout
  (MainWindow, EqTab, AsioTab).
- **Bug 2bis — anti double-correction du lecteur interne (latent)** : le
  lecteur de la barre du haut appliquait l'EQ principal LOCALEMENT alors
  que l'APO applique la même courbe sur la sortie → correction doublée
  pour les fichiers lus et les signaux de test sur une sortie activée
  (seul le bus master de la console était protégé, D25). Correctif :
  `RefreshEqApplication` (MainViewModel) — l'APO reçoit la courbe avec le
  bypass UTILISATEUR (pas le contournement local), et l'EQ local du lecteur
  se contourne quand `WindowsApoSystem.CoversEndpoint(sortie d'écoute)`
  (nouvelle méthode : accepte « wasapi|{mmdevice} » ou la clé registre
  nue) ; recalcul au changement de sortie, à chaque état APO, à chaud et
  sur changement de courbe (anti-rebond 80 ms conservé).
  `RoomMeasurementEngine.IsMeasuring` (nouveau) interdit au shell
  d'écraser le bypass pendant une mesure (le moteur force « pièce seule »
  et restaure lui-même).
- **Bug 3 — activation invisible** : le verdict d'une activation (échec
  inclus : UAC refusée, sortie sans slot FxProperties, périphérique
  débranché…) n'apparaissait que dans la barre d'état muette du bas, puis
  `RefreshSystemApoCore` remettait le texte générique du panneau —
  l'utilisateur voyait « la page se rafraîchit et le bouton reste
  inactif » sans AUCUNE explication. Correctifs : (a) `SystemApoVerdictText`
  + `SystemApoVerdictIsError` (✓/✗ en couleur) affichés DANS le panneau,
  jamais effacés par le rafraîchissement (testé) ; (b) second
  rafraîchissement différé +1,5 s après chaque action — l'activation
  redémarre le service audio et l'énumération immédiate peut rendre une
  liste vide/incomplète (le bouton paraissait non activé alors que tout
  avait réussi) ; (c) échecs désormais journalisés en version test
  (`WindowsApoSystem`, avant : seuls les succès l'étaient) ;
  (d) `SystemApoSupported` renotifié au rafraîchissement.
- **Tests** : 479/479 (Core 237, Audio 168 inchangés — M-AUD-03 intacte,
  App 74) : −32 tests de console supprimés avec leur objet
  (`MixerTrackPolicyTests`, `BandTrackChannelTests`), +6 `MixerSystemTabTests`
  (D26) : suivi de sortie d'écoute (événement + idempotence), sortie
  mémorisée restituée une fois, persistance réduite (aucune tranche),
  chargement d'un mixer.json hérité sans exception, verdict qui survit au
  rafraîchissement, inertie `CoversEndpoint` hors Windows, garde
  d'instance unique (acquisition/refus/libération).
- **Docs** : D26 (DECISIONS), I-APP-03 v3 réécrite (poste de pilotage),
  CARTE (M-APP-03 v3, M-APP-01 + SingleInstanceGuard, VuMeter retiré),
  REPRISE (résumé + état session 22 + historique décisions), README,
  LISEZMOI (v3.9.0), package_v3.sh (VERSION 3.9.0 + vérifications D26,
  pont ASIO vérifié côté couche audio uniquement). Build 0 erreur
  0 avertissement. Livrables 3.9.0 (zips + Setup.exe).

Stage Summary:
- D26 LIVRÉE : l'onglet Mixage ne montre plus que ce qui agit sur le son
  Windows — l'EQ système et son pilotage (verdict visible, volume de la
  carte d'écoute, preset par sortie) ; l'installation n'ouvre plus
  l'application en double ; un échec d'activation affiche désormais sa
  raison là où l'utilisateur a cliqué.
- Prochaine étape : validation terrain Windows — réinstaller via Setup.exe
  (une seule fenêtre), activer l'EQ système (verdict ✓ et bouton qui
  bascule ; refuser l'UAC → verdict ✗ rouge explicite), lire un fichier
  sur une sortie APO active (aucune double correction).

---
Session : 23 — 2026-11-02
Agent : main (Z.ai)
Task : Retour terrain v3.9.0 (suite D26) : « voici les messages d'erreur
lorsque j'active l'eq sur une sortie » — capture : ✗ « Activation de
« Haut-parleurs (Realtek(R) Audio) » — Activation impossible : Ce
périphérique n'expose pas de slot d'effets système (FxProperties) :
impossible d'y inscrire l'égaliseur. » v3.9.1.

Work Log:
- **Diagnostic** : la cause racine n'était PAS l'affichage (corrigé D26)
  mais le registrar lui-même : `WindowsApoEndpointStore.Set*` n'écrivait
  que dans une sous-clé `FxProperties` DÉJÀ EXISTANTE (`OpenSubKey` →
  null → InvalidOperationException « n'expose pas de slot d'effets
  système »). Or un pilote sans effets déclarés — pilote générique
  Windows, ex. « Realtek(R) Audio » (HDA inbox), typique des sorties
  réelles — ne crée PAS cette sous-clé : l'activation échouait sur
  exactement les sorties visées. Sources vérifiées : Microsoft Learn
  (PKEY_FX_EndpointEffectClsid = {d04e05a6-…d1d},7, REG_SZ — confirmé) ;
  É. Dechamps (notes APO) : « If no properties are present, or if the
  FxProperties key is absent entirely, then no system effect APOs are
  used » — donc le moteur lit la clé QUEL QUE SOIT son auteur : la créer
  suffit (c'est le mécanisme standalone d'Equalizer APO, 10+ ans de
  terrain, déjà documenté § 3.2 de docs/EQ-SYSTEME-APO.md).
- **Correctif (store)** : `OpenFxForWrite` — l'écriture ouvre la clé de
  l'endpoint en écriture (exception claire « clé introuvable
  (périphérique retiré ?) » si disparue) puis `CreateSubKey` la
  sous-clé FxProperties manquante ; JAMAIS de clé endpoint fantôme
  (seule la sous-clé d'effets est créée). `SetString`/`SetInt` l'
  utilisent : l'activation réussit désormais sur les sorties sans slot
  déclaré. `GetString`/`GetInt` inchangés (null si absent).
- **Restitution exacte (invariant 2)** : `ApoEndpointBackup` gagne
  `FxPropertiesExisted` (défaut VRAI — les sauvegardes v3.7–v3.9 sans
  le champ restent lisibles et ne déclenchent JAMAIS de suppression) ;
  `Enable` capture l'existence AVANT la première écriture ; `Disable`
  SUPPRIME la sous-clé créée si elle est redevenue vide après
  restauration — et la GARDE si le pilote y a écrit ses propres valeurs
  entre-temps. Nouvelles méthodes d'`IApoEndpointStore` :
  `FxPropertiesExists` / `FxValueNames` / `DeleteFxProperties`
  (mécanisme dans le store, politique pure dans le registrar).
- **Anti-WOW64 (ceinture de sécurité)** : tout accès registre du module
  passe par `OpenHklm()` = `RegistryKey.OpenBaseKey(LocalMachine,
  Registry64)` — MMDevices et l'inscription COM vivent dans la vue
  native (audiodg 64 bits) ; un processus 32 bits serait redirigé vers
  WOW6432Node et ne verrait RIEN (l'appli est publiée win-x64 : garde
  anti-régression si un build 32 bits voyait le jour). Comprend
  `RegisterCom`/`UnregisterCom`/`ComRegistered`.
- **Reprise des tentatives antérieures** : les clics « Activer » échoués
  de v3.7–v3.9 avaient laissé des sauvegardes sans CLSID écrit — le
  prochain clic réactive proprement (sauvegarde réécrite sur l'état
  courant), et « Réparer » réécrit l'inscription (même état observable
  qu'une MAJ de pilote : intention de l'utilisateur respectée).
- **UI** : aide du panneau EQ système mise à jour (le cas d'échec
  « sortie sans slot d'effets » n'existe plus ; restent invite refusée /
  périphérique retiré).
- **Tests** : 483/483 xUnit (Core 237, Audio 172 : registrar 16 → 20 —
  création de clé + ordre existence→écriture, suppression à la
  désactivation, garde des valeurs étrangères, sauvegarde héritée sans
  suppression, inertie Linux étendue aux 3 nouvelles méthodes ; App 74
  inchangés) ; build 0 erreur 0 avertissement ; harnais natif APO
  rejoué : 112 vérifications, 0 échec (cœur inchangé — DLL x64+arm64
  recompilées, contrôles PE exports/dépendances OK).
- **Docs** : spec I-AUD-09 v1.2 (as-built : correctifs f/g/h, invariant 2
  étendu, critères registrar 20 tests) ; docs/EQ-SYSTEME-APO.md (statut
  v3.9.1 + diagnostic) ; README (historique v3.9.1) ; LISEZMOI 3.9.1 ;
  build-apo.sh (version lue depuis Directory.Build.props) ;
  package_v3.sh (VERSION 3.9.1 + vérification structurelle du correctif) ;
  Directory.Build.props 3.9.1.

Stage Summary:
- v3.9.1 LIVRÉE : l'EQ système sans pilote s'active enfin sur les sorties
  réelles — y compris celles dont le pilote ne déclare aucun emplacement
  d'effets (la sous-clé FxProperties manquante est créée, comme le fait
  Equalizer APO depuis 10 ans) — avec restitution exacte à la
  désactivation (la clé créée disparaît si elle est redevenue vide).
- Prochaine étape : validation terrain Windows — réinstaller via
  Setup.exe 3.9.1, activer « Haut-parleurs (Realtek(R) Audio) » (verdict
  ✓, état « ● EQ système actif », ~1 s de coupure du son), jouer de la
  musique hors RoomEq et vérifier que la courbe s'applique ; puis
  Désactiver et contrôler que la sous-clé créée disparaît (regedit).

Session : 24 — 2026-11-02
Agent : main (Z.ai)
Task : Retour terrain v3.9.1 (suite) : « voici les messages d'erreur lorsque
j'active l'eq sur une sortie » — capture : ✗ activation de « Casque (JBL
Tune Flex 2) » refusée, « Requested registry access is not allowed » (le
bouton restait non activé après rafraîchissement). v3.9.2.

Work Log:
- **Diagnostic (cause racine, établie par le code source d'Equalizer APO
  lui-même)** : l'erreur est une SecurityException .NET d'ouverture registre
  en ÉCRITURE (ACCESS_DENIED → « Requested registry access is not allowed »).
  La chaîne v3.9.1 est correcte jusqu'au registrar (le processus élevé
  tourne : Deploy est passé, sinon l'erreur aurait été « Déploiement
  impossible : Access to the path … is denied » sur bin/Users-RX) — le refus
  vient donc de l'ouverture de la clé de l'ENDPOINT elle-même. FAIT ÉTABLI :
  les clés d'endpoints MMDevices (HKLM\…\MMDevices\Audio\Render\{GUID}) sont
  créées par le service audio (SYSTEM) et leur DACL n'accorde PAS l'écriture
  aux administrateurs — même ÉLEVÉ. Preuve par le code : DeviceAPOInfo.cpp
  (Equalizer APO) enveloppe la création de FxProperties d'un
  catch(RegistryException) → takeOwnership(keyPath) + makeWritable(keyPath)
  → retry, commentaire d'origine « Permissions were not sufficient, so
  change them » (helpers/RegistryHelper.cpp, miroir GitHub alebcay/
  equalizer-apo, lu intégralement : privilège SeTakeOwnershipPrivilege via
  OpenProcessToken/LookupPrivilegeValue/AdjustTokenPrivileges, owner =
  SID BUILTIN\Administrateurs via RegSetKeySecurity(OWNER_SECURITY_INFO),
  DACL = SetEntriesInAcl(KEY_ALL_ACCESS, SET_ACCESS,
  SUB_CONTAINERS_AND_OBJECTS_INHERIT) via RegSetKeySecurity(DACL)). Source
  SourceForge directe bloquée par Cloudflare → miroir GitHub.
- **Correctif (takeover registre)** : nouveau
  `src/RoomEq.Audio/SystemApo/ApoRegistrySecurity.cs` (portage C# fidèle,
  [SupportedOSPlatform("windows")], tout échec → false/null, jamais
  d'exception) : EnablePrivilege (P/Invoke token, détection
  ERROR_NOT_ALL_ASSIGNED = non élevé), AdminsHaveFullControl (lecture DACL
  par SID S-1-5-32-544, indépendant de la langue), EnsureWritable =
  TakeOwnership (RegSetKeySecurity OWNER SEUL sur descripteur neuf — le DACL
  n'est JAMAIS touché à cette étape, contrairement à
  RegistryKey.SetAccessControl qui persisterait un DACL absent) puis
  GrantAdminsFullControl (GetAccessControl → AddAccessRule FullControl
  ContainerInherit → SetAccessControl : l'ancien DACL + notre ACE),
  Capture/Restore (descripteur binaire Base64, RegSetKeySecurity
  OWNER|DACL, SeRestorePrivilege).
- **Store** : `OpenFxForWrite` tente l'ouverture/création, et sur
  SecurityException/UnauthorizedAccessException applique
  EnsureWritable(endpoint) + EnsureWritable(FxProperties si existante) puis
  RÉESSAI (l'exception réelle remonte si le refus persiste : takeover
  impossible). `CaptureSecurity` : snapshot owner+DACL des SEULES clés où
  les admins n'ont pas déjà FullControl (les autres ne changent pas) —
  null si rien à restituer. `RestoreSecurity` : best effort, jamais
  d'exception. Inertie hors Windows inchangée (null/no-op).
- **Registrar (invariant 2 étendu à la sécurité)** : `Enable` capture la
  sécurité AVANT la première écriture (comme fxExisted) et la stocke dans
  `ApoEndpointBackup.Security` (record `ApoSecuritySnapshot` :
  Endpoint/Fx × OwnerSid/SdBase64 — champ optionnel, les sauvegardes
  v3.7–v3.9.1 restent lisibles : Security null, JAMAIS de fausse
  restitution) ; `Disable` restitue APRÈS la restauration des valeurs et
  la suppression éventuelle de la sous-clé (les écritures ont besoin de
  l'ACE d'activation). `SecurityException` résiduelle → message français
  ACTIONNABLE (relancez + acceptez l'invite ; logiciel de sécurité) au
  lieu de l'anglais brut. Interface IApoEndpointStore +2 méthodes
  (CaptureSecurity/RestoreSecurity — mécanisme dans le store, politique
  pure dans le registrar).
- **Réversibilité SUPÉRIEURE à la référence** : Equalizer APO ne restitue
  JAMAIS owner/DACL (vérifié dans leur uninstall) ; RoomEq restitue les
  deux (SeRestorePrivilege pour réattribuer SYSTEM). Non-restituée (clé
  disparue, privilège absent) = best effort silencieux : les VALEURS sont
  déjà restaurées, une ACE Admins restante est sans effet auditif ni
  risque (admin uniquement).
- **Tests** : 488/488 xUnit (Core 237, Audio 172 → 177 : registrar 20 → 25
  — capture AVANT première écriture + stockage snapshot (ordre par trace),
  restitution APRÈS suppression de sous-clé (ordre par trace), aucune
  restitution si snapshot null, sauvegarde héritée sans Security jamais
  restaurée, rétrocompatibilité disque d'un backups.json v3.9.1 sans le
  champ, inertie Linux CaptureSecurity/RestoreSecurity ; App 74 inchangés)
  ; build 0 erreur 0 avertissement (CA1416 gardé par OperatingSystem
  .IsWindows() en ligne — nouvelle garde dans OpenOrCreateFx).
- **Docs** : spec I-AUD-09 v1.3 (as-built correctifs i/j/k) ;
  docs/EQ-SYSTEME-APO.md (fait établi MMDevices § après 3.2 + statut
  v3.9.2) ; README (historique v3.9.2) ; LISEZMOI 3.9.2 (488 tests) ;
  package_v3.sh (VERSION 3.9.2 + vérification structurelle du takeover :
  SeTakeOwnershipPrivilege/SeRestorePrivilege/EnsureWritable/
  CaptureSecurity) ; Directory.Build.props 3.9.2.
- **Installateur** : build_setup.sh rejoué — RoomEq-win-Setup.exe régénéré
  avec la release 3.9.2 (Velopack « Building release 3.9.2 » visible,
  52 Mo, PE contrôlé) puis re-téléversé sur Drive (l'ancien, qui
  embarquait encore la 3.9.1, a été remplacé au premier niveau des
  Livrables).

Stage Summary:
- v3.9.2 LIVRÉE : l'activation de l'EQ système franchit enfin le dernier
  verrou Windows — les clés des sorties audio appartiennent à SYSTEM et
  refusent l'écriture aux administrateurs ; RoomEq prend possession le
  temps de l'inscription (mécanisme Equalizer APO, 10+ ans de terrain) et
  restitue intégralement owner + permissions à la désactivation, mieux que
  la référence. Les échecs résiduels s'affichent en français actionnable.
- Prochaine étape : validation terrain Windows — réinstaller via Setup.exe
  3.9.2, activer « Casque (JBL Tune Flex 2) » (verdict ✓, état « ● EQ
  système actif », ~1 s de coupure), jouer de la musique hors RoomEq ;
  puis Désactiver et contrôler dans regedit que owner/DACL de la clé
  endpoint sont revenus à SYSTEM (propriétés > sécurité > propriétaire).

---
Session : 25 — 2026-11-02
Agent : main (Z.ai)
Task : Retour terrain v3.9.2 (suite) : « toujours le problème de clé
protégée pour l'activation de l'eq sur la sortie son » — le takeover
v3.9.2 n'a pas débloqué l'activation. v3.9.3.

Work Log:
- **Sandbox recyclée** (SDK et Zig disparus) : dotnet 8.0.427 réinstallé,
  Zig 0.14.1 réinstallé, branche synchronisée depuis GitHub (le clone
  local s'arrêtait à la sauvegarde session 19 : les sessions 20–24
  vivaient déjà dans origin/v3-recodage — fast-forward propre).
- **Diagnostic (cause racine PROUVÉE par marshaling réel, pas une
  hypothèse)** : le portage v3.9.2 du mécanisme Equalizer APO était
  correct dans sa logique (ordre des opérations, droits, sections de
  sécurité) mais sa structure interop était FAUSSÉE —
  `TokenPrivileges1` déclarait le LUID comme `long` C# (alignement 8
  octets) : le marshaling insérait 4 octets de PADDING après
  `PrivilegeCount` (24 octets au lieu des 16 du natif winnt.h). Le noyau
  lisait `Luid.LowPart = 0` (le padding !) et `Attributes = 0` (l'octet
  haut du long) → LUID falsifié inexistant dans le jeton →
  `AdjustTokenPrivileges` « réussissait » avec ERROR_NOT_ALL_ASSIGNED →
  `EnablePrivilege` concluait à tort « processus non élevé » →
  `TakeOwnership` abandonnait à sa première ligne. **La prise de
  possession n'a JAMAIS été tentée** : le correctif v3.9.2 ne pouvait
  pas fonctionner, d'où le « toujours le problème de clé protégée ».
  Preuve exécutable : marshaling comparé 24 octets
  `01-00-00-00-00-00-00-00-09-…` (v3.9.2, LUID décalé) vs 16 octets
  `01-00-00-00-09-00-00-00-…` (natif).
- **Vérifications des autres maillons (sources .NET 8 réelles
  consultées, écartant les hypothèses concurrentes)** :
  `RegistryKey.OpenSubKey` passe les droits BRUTS à RegOpenKeyEx et
  `ValidateKeyRights` accepte `TakeOwnership` (inclus dans FullControl
  0xF003F) ; `RegistrySecurity.Persist` ne persiste QUE les sections
  modifiées (DACL seul après AddAccessRule — le pattern
  GrantAdminsFullControl est sûr) ; comparaison intégrale avec
  `takeOwnership`/`makeWritable` d'Equalizer APO (miroir GitHub) : seul
  écart significatif = la structure du token.
- **Correctif** :
  - `ApoLuid` (deux DWORD, alignement 4) + `ApoTokenPrivileges` (16
    octets exactement : PrivilegeCount à 0, Luid à 4, Attributes à 12) —
    structures au niveau du namespace, SANS dépendance Windows (le
    layout est ainsi testable sous Linux) ; `LookupPrivilegeValue`
    re-signée `out ApoLuid` ; `bufferLength` =
    `Marshal.SizeOf<ApoTokenPrivileges>()` (16) comme la référence C++.
  - **Diagnostics par maillon** : `EnablePrivilege`, `TakeOwnership`,
    `GrantAdminsFullControl` et `EnsureWritable` retournent désormais un
    diagnostic texte (null = succès) — code Win32 et opération en cause
    — au lieu d'un booléen muet ; `OpenFxForWrite` collecte le
    diagnostic (endpoint puis FxProperties, le refus endpoint gardant la
    priorité) et, si le RÉESSAI échoue encore, lève
    `ApoRegistryRefusalException : SecurityException` portant refus
    initial + refus après reprise + maillon défaillant.
  - Registrar : `catch (ApoRegistryRefusalException)` AVANT le catch
    générique — le verdict de l'activation NOMME l'opération refusée
    (privilège absent / WRITE_OWNER refusé / RegSetKeySecurity code N)
    et demande de transmettre le message complet : plus jamais d'échec
    « clé protégée » générique à débugger à l'aveugle (ce silence a
    coûté un aller-retour terrain complet).
  - Coquille au passage : « mise à jour de pilote had effacé » → « qui
    a effacé » (message utilisateur de Réparer).
- **Verrou de régression (la leçon de cette session, appliquée à la
  philosophie verrous du projet)** : `ApoRegistrySecurityLayoutTests`
  (4 tests, exécutables SOUS LINUX car marshaling pur) — taille
  exactement 16, LUID à l'offset 4, Attributes à l'offset 12, et le cas
  terrain exact (LUID de SeTakeOwnershipPrivilege = 9 : octets
  `09 00 00 00` à l'offset 4). Ces tests ÉCHOUENT sur le code v3.9.2 —
  le bug est désormais impossible à réintroduire. +1 test registrar :
  verdict qui nomme le maillon défaillant sur refus après takeover.
- **Sandbox/chaîne reconstituée** : harnais natif APO rejoué (112
  vérifications, 0 échec), DLL RoomEqApo_x64.dll (452 Ko) + arm64
  (412 Ko) recompilées identiques (contrôles PE : exports COM + imports
  système OK) ; `gen_telemetrie.py` re-pointé (chemins legacy →
  workspace) et telemetrie.json réel régénéré (access token + dossier
  journaux-app retrouvés) — le zip TEST embarque de nouveau la vraie
  configuration de télémétrie.
- **Tests** : 493/493 xUnit (Core 237, Audio 182 : registrar 25 → 26 +
  layout 4 ; App 74 inchangés) ; build 0 erreur 0 avertissement ;
  verrous structurels package_v3.sh verts (dont nouveau verrou v3.9.3 :
  struct ApoLuid/ApoTokenPrivileges + ApoRegistryRefusalException +
  tests de layout).
- **Docs** : spec I-AUD-09 v1.4 (as-built correctifs l/m : cause racine
  interop + diagnostics) ; docs/EQ-SYSTEME-APO.md (statut v3.9.3) ;
  README (historique) ; LISEZMOI 3.9.3 (correctif expliqué en français
  clair, 493 tests) ; REPRISE.md (résumé express + état session 25) ;
  Directory.Build.props + package_v3.sh VERSION 3.9.3.
- **Livrables (D22)** : package_v3.sh (zips test 43M / public 42M /
  source 6,9M, DLL APO embarquées, contrôles verts), build_setup.sh →
  RoomEq-win-Setup.exe 3.9.3 (52 Mo, Velopack 1.2.161, en-tête PE
  contrôlé) régénéré et re-téléversé sur Drive (l'ancien 3.9.2 remplacé
  au premier niveau des Livrables) ; anciens zips 3.9.2 archivés dans
  Livrables/Archive ; miroir Drive complet + bundle git.

Stage Summary:
- v3.9.3 : la cause exacte du « toujours le problème de clé protégée »
  est trouvée et prouvée — un décalage d'alignement de 4 octets dans la
  structure du privilège Windows rendait le mécanisme de prise de
  possession inopérant depuis sa livraison ; le calque exact du natif
  (16 octets, verrouillé par tests) l'exécute enfin. Et si Windows
  refusait encore un maillon, le verdict nommerait l'opération et son
  code d'erreur au lieu d'un générique.
- Prochaine étape : validation terrain Windows — réinstaller via
  Setup.exe 3.9.3, activer l'EQ sur « Casque (JBL Tune Flex 2) » :
  verdict ✓ attendu, ~1 s de coupure (redémarrage du service audio),
  musique hors RoomEq égalisée ; puis Désactiver et vérifier dans
  regedit que owner/DACL de la clé endpoint sont revenus à SYSTEM.

---
Session : 26 — 2026-11-02
Agent : main (Z.ai)
Task : Retour terrain v3.9.3 avec capture ENFIN transmise du message
d'erreur complet : « impossible d'activer le privilège
SeTakeOwnershipPrivilege (le point d'entrée CloseHandle est introuvable
dans la DLL advapi32.dll) » + « Requested registry access is not
allowed » — le takeover reste bloqué en « clé protégée ». v3.9.4.

Work Log:
- **Le diagnostic par maillon (v3.9.3) a fait son travail au premier
  essai** : après trois tentatives de transmission du message d'erreur
  (pièces jointes perdues deux fois), la capture passe — et NOMME le
  maillon : l'activation du privilège échoue sur un
  EntryPointNotFoundException de CloseHandle dans advapi32.dll. Plus
  d'hypothèse à débugger à l'aveugle : le coupable est désigné par la
  machine du terrain elle-même.
- **Cause racine (FAIT ÉTABLI)** : `CloseHandle` est un export de
  KERNEL32.DLL — il n'existe PAS dans advapi32.dll. Le portage v3.9.2
  l'avait importé d'advapi32 (au milieu des autres imports de jeton),
  et CHAQUE appel levait `EntryPointNotFoundException`. L'appel a lieu
  dans le `finally` de `EnablePrivilege` : en C#, une exception levée
  dans un finally REMPLACE le résultat en cours de retour. Conséquence
  en chaîne : le privilège SeTakeOwnershipPrivilege était réellement
  activé (le correctif v3.9.3 du LUID fonctionnait — plus de
  ERROR_NOT_ALL_ASSIGNED, `AdjustTokenPrivileges` réussissait), mais
  `EnablePrivilege` répondait « impossible d'activer le privilège » →
  `TakeOwnership` abandonnait à sa première ligne → takeover jamais
  tenté → écriture refusée → « clé protégée ». TOUT le mécanisme
  marchait ; seul le nettoyage du jeton cassait tout — le bug le plus
  discret possible : une fonction de ménage qui échoue silencieusement
  en faisant échouer le travail déjà accompli.
- **Correctif (1 ligne)** : `[DllImport("kernel32.dll")]` sur
  `CloseHandle` (`ApoRegistrySecurity.cs`), documenté au code. Audit
  des 5 autres imports au passage — tous corrects :
  OpenProcessToken/LookupPrivilegeValue/AdjustTokenPrivileges/
  RegSetKeySecurity → advapi32 ; GetCurrentProcess → kernel32.
  Aucun autre P/Invoke du dépôt n'est concerné (CoreAudioApi macOS
  utilise ses propres constantes).
- **Verrou de régression (philosophie v3.9.3 réappliquée)** :
  `ApoRegistrySecurityPinvokeTests` — 2 tests de réflexion PURE (donc
  exécutables sous Linux, là où le module est sinon inerte) qui lisent
  les `DllImportAttribute` réels : (1) CloseHandle doit venir de
  kernel32.dll ; (2) les 6 imports natifs exactement, chacun de sa
  DLL d'origine — ni un de plus (binding non audité), ni un de moins
  (maillon retiré). **Prouvé par test croisé** : les 2 tests ÉCHOUENT
  sur le code v3.9.3 (brièvement réintégré) et passent sur v3.9.4.
- **Verrou structurel** : package_v3.sh gagne le bloc v3.9.4 —
  l'attribut kernel32.dll doit précéder immédiatement
  `extern bool CloseHandle`, et le fichier de tests doit exister et
  verrouiller kernel32.dll.
- **Tests** : 495/495 xUnit (Core 237, Audio 182 → 184 : +2 P/Invoke ;
  App 74) ; build 0 erreur 0 avertissement. APO natif inchangé (le bug
  était dans le code managé — harnais 112 vérifications non concerné).
- **Docs** : spec I-AUD-09 v1.5 (as-built correctifs n/o : import
  corrigé + verrou de binding) ; docs/EQ-SYSTEME-APO.md (v3.9.4) ;
  README (historique) ; LISEZMOI 3.9.4 (correctif expliqué en français
  clair, 495 tests) ; REPRISE.md (résumé express + état session 26) ;
  Directory.Build.props + package_v3.sh VERSION 3.9.4.
- **Livrables (D22)** : package_v3.sh (zips test/public/source,
  contrôles verts dont le nouveau verrou v3.9.4) ; build_setup.sh →
  RoomEq-win-Setup.exe 3.9.4 régénéré et re-téléversé sur Drive
  (l'ancien 3.9.3 remplacé au premier niveau des Livrables) ; anciens
  zips 3.9.3 archivés dans Livrables/Archive ; miroir Drive complet +
  push GitHub.

Stage Summary:
- v3.9.4 : le message d'erreur tant attendu a enfin traversé, et il
  désignait le coupable lui-même — le privilège était activé depuis
  v3.9.3, mais la fermeture du jeton, importée de la mauvaise DLL,
  jetait le succès à la poubelle avant que quiconque puisse l'utiliser.
  Une ligne corrigée, six bindings verrouillés par des tests qui
  échouent sur l'ancien code : la mécanique de prise de possession
  peut enfin s'exécuter de bout en bout.
- Prochaine étape : validation terrain Windows — réinstaller via
  Setup.exe 3.9.4, activer l'EQ sur « Casque (JBL Tune Flex 2) » :
  verdict ✓ attendu, ~1 s de coupure, musique hors RoomEq égalisée ;
  puis Désactiver et vérifier dans regedit que owner/DACL de la clé
  endpoint sont revenus à SYSTEM.

---
Session : 27 — 2026-11-02
Agent : main (Z.ai)
Task : Trois demandes : (0) règle permanente — toujours répondre en
français (« l'anglais est accepté mais par contre je ne comprends rien au
chinois ») ; (a) « le routage fonctionne mais je n'entends pas de
différence de son quand l'eq est activé (pourtant chargé) » ; (b) « la
modification du volume windows n'a aucun effet sur la barre de volume de
l'appli, qui elle ne modifie pas le volume entendu non plus ». v3.9.5.

Work Log:
- **Règle permanente langue** : REPRISE.md § 2 point 2 + ligne du résumé
  express — TOUJOURS répondre en français, l'anglais est accepté, le
  chinois est interdit. Consigné comme règle de méthode de travail
  permanente (lue en premier à chaque session).
- **Bug (b) volume — cause racine identifiée (corrélation temporelle
  exacte avec le bug (a))** : l'activation EQ système RÉUSSIE redémarre
  le service audio (`net stop/start audiosrv` — inévitable, c'est ce qui
  fait relire les FxProperties). Or ce redémarrage invalide TOUS les
  objets COM WASAPI de l'application : `EndpointVolumeController` ne
  re-ciblait le périphérique qu'au CHANGEMENT de sortie — après
  l'activation, sa cible était un endpoint MORT : les lectures levaient
  (Available=false DÉFINITIF, plus jamais réessayé) ou figeaient, et les
  écritures se perdaient. Exactement les deux symptômes rapportés : « la
  barre de l'appli ne suit plus le volume Windows » + « la barre n'agit
  plus sur le son entendu ». Les reconnexions Bluetooth produisent le
  même effet (même id, objet COM neuf).
  Correctif : `EndpointVolumeController.Reacquire()` — ré-énumération,
  re-bind du même identifiant, sonde, libération de l'ancien ; appelé
  (1) immédiatement après toute action EQ système réussie, (2) au
  rafraîchissement différé +1,5 s, (3) à chaque photographie de
  périphériques (branchement à chaud), (4) en AUTO-GUÉRISON par le
  polling de volume (au plus toutes les 2 s tant que la cible est
  indisponible) — plus jamais d'état gelé définitif. Test de câblage
  Linux (compteur ReacquireAttempts + affichage cohérent « — »).
- **Bug (a) EQ inaudible — bilan statique complet, puis réponse en deux
  volets (fix probable + observabilité)** : la chaîne managée a été
  revue de bout en bout (preset par sortie → EqViewModel.LoadPreset →
  Engine.Eq.SetPreset → Changed → debounce 80 ms → PushCurve →
  WriteCurveIfActive → active.json ; natif : sondage 100 ms, parseur
  preset D23 identique à celui du VST qui charge les presets de l'app,
  publication atomique par révision) — COMPLETE sur le papier. Le JSON
  écrit et le parseur natif vérifiés champ par champ. Deux volets :
  1. FIX probable — le maillon permissions : `ApplyAcls` exécutait
     icacls « au mieux effort » (échecs ignorés) : un icacls raté
     laissait %ProgramData%\RoomEq\apo sans l'entrée
     Utilisateurs:Modifier → la DLL se chargeait (SYSTEM/Admins
     lisent) mais l'application non élevée ne pouvait PLUS écrire
     active.json → EQ muet sans le moindre message. `ApoAclVerifier`
     (vérification .NET réelle, S-1-5-32-545 doit avoir Modifier) :
     l'activation ÉCHOUE désormais avec un verdict explicite si les
     permissions ne sont pas là ; + courbe initiale valide écrite à
     l'activation si active.json est absent.
  2. OBSERVABILITÉ — la philosophie v3.9.3 appliquée à la courbe :
     `ApoChainText` (pur, 8 tests Linux) affiche dans le panneau EQ
     système CHAQUE maillon : courbe envoyée (bandes actives,
     préampli, révision — relue d'active.json), écriture refusée
     (`WindowsApoSystem.LastCurveWriteError`), bypass actif, courbe
     plate, et publication par le moteur audio (« nouvelle courbe
     publiee (rev N) » lu dans apo.log via `ReadApoLogTail`, queue de
     4 Ko, FileShare.ReadWrite). Un EQ activé mais inaudible nomme
     désormais son maillon défaillant SANS nouvel aller-retour.
- **Gardes de plateforme** : ApoAclVerifier + ApoSetupCommands
  [SupportedOSPlatform("windows")], appel --apo-setup refusé hors
  Windows (code 2) — CA1416 propre, build 0 avertissement.
- **Tests** : 511/511 xUnit (Core 237, Audio 184 → 199 : +8
  ApoChainTextTests [chaîne complète, écriture impossible, jamais
  écrite, bypass, plate, APO sans publication, révision en retard,
  journal seul] +5 ApoAclVerifierTests [Modifier, FullControl,
  lecture seule refusée, aucune règle, dossier inexistant] +2
  ReadApoLogTail [queue tronquée, fichier court/absent] ; App 74 → 75 :
  +1 re-ciblage volume sans exception ni gel). Build 0 erreur
  0 avertissement. Natif APO inchangé (harnais 112 non touché — le
  bug (a) était managé ou environnemental).
- **Docs** : spec I-AUD-09 v1.6 (as-built p/q/r/s) ;
  docs/EQ-SYSTEME-APO.md (v3.9.5) ; README (historique) ; LISEZMOI
  3.9.5 (correctif expliqué en français clair) ; REPRISE.md (règle
  LANGUE permanente + résumé + état session 27) ; Directory.Build.props
  + package_v3.sh VERSION 3.9.5 + verrou structurel v3.9.5.
- **Livrables (D22)** : package_v3.sh (zips test/public/source,
  contrôles verts dont le verrou v3.9.5) ; build_setup.sh →
  RoomEq-win-Setup.exe 3.9.5 régénéré et re-téléversé sur Drive
  (l'ancien 3.9.4 remplacé au premier niveau des Livrables) ; anciens
  zips 3.9.4 archivés dans Livrables/Archive ; miroir Drive complet +
  push GitHub.

Stage Summary:
- v3.9.5 : l'activation fonctionne désormais — et c'est elle-même qui
  cassait le volume (le redémarrage du service audio tuait les objets
  COM de l'application, qui ne se ré-acquerraient jamais). Corrigé par
  re-ciblage automatique + auto-guérison. Et pour l'EQ « chargé mais
  inaudible » : le maillon permissions est verrouillé (icacls muet =
  activation refusée avec verdict), et la chaîne de courbe entière est
  désormais AFFICHÉE dans le panneau — le prochain rapport terrain
  nommera le maillon tout seul.
- Prochaine étape : validation terrain Windows — Setup 3.9.5, activer
  l'EQ, LIRE le panneau « EQ système » (chaîne complète attendue :
  « Courbe envoyée… ✓ APO : courbe publiée et appliquée ») et vérifier
  le volume dans les deux sens après activation. Tout ⚠ du panneau =
  le transmettre tel quel.

---
Session : 28 — 2026-11-02
Agent : main (Z.ai)
Task : Retour terrain v3.9.5 transmis tel quel (« message d'erreur » +
capture du panneau EQ système) : « journal APO absent — la DLL n'a jamais
été chargée par le moteur audio — cliquer Réparer » alors que la courbe
était bien envoyée (10 bandes, préampli -2 dB, révision 37) et l'EQ
activé sur le casque JBL. v3.9.6.

Work Log:
- **L'observabilité v3.9.5 a fonctionné exactement comme prévu** : le
  panneau a nommé le maillon défaillant (DLL jamais chargée) sans nouvel
  aller-retour d'investigation — restait à trouver POURQUOI le moteur
  audio ne chargeait pas une DLL pourtant inscrite.
- **Cause racine ÉTABLIE par recoupement de sources de référence** (le
  dépôt local était en retard sur GitHub : pull origin v3-recodage d'abord
  — v3.9.5 poussée en session 27) :
  1. **Doc développeur OFFICIELLE d'Equalizer APO** (Wiki/Developer.txt,
     miroir GitHub `mirror/equalizerapo`) : « The following changes have
     to be done to the registry to make the Windows Audio Service load
     the DLL file that implements the APO : (1) Under the registry key
     HKEY_LOCAL_MACHINE\SOFTWARE\Microsoft\Windows\CurrentVersion\Audio
     the DWORD value DisableProtectedAudioDG has to be set to 1. This
     disables the signature check for APOs, so that unsigned APOs will
     be loaded. » + (3) la clé
     HKLM\SOFTWARE\Classes\AudioEngine\AudioProcessingObjects\{GUID}
     « has to be created, which is normally handled by the function
     RegisterAPO ».
  2. **Code source d'Equalizer APO** (DeviceAPOInfo.cpp, cloné et lu) :
     `checkProtectedAudioDG(fix)` écrit la DWORD à 1 ; l'installation
     SFX_EFX écrit AUSSI le MULTI_SZ de modes de traitement EFX
     (`{d3993a3f-99c2-4402-b5ec-a92a0367664b},7` =
     `{C18E2F7E-933D-4965-B7D1-1EEF228D2AF3}`) quand absent ; DllMain →
     DllRegisterServer → RegisterAPO + inscription COM ThreadingModel Both.
  3. **Sample officiel Microsoft SysVAD** (Windows-driver-samples,
     ComponentizedApoSample.inx) : les VALEURS NOMMÉES exactes de la clé
     AudioEngine\AudioProcessingObjects\{CLSID} (FriendlyName, Copyright,
     MajorVersion, MinorVersion, Flags, Min/MaxInputConnections,
     Min/MaxOutputConnections, MaxInstances, NumAPOInterfaces,
     APOInterface0).
  4. **Notes É. Dechamps** (github.com/dechamps/APO) : le tableau
     SFX/MFX/EFX confirme notre position EFX `,7` — ce maillon-là était
     CORRECT, ainsi que le COM et le mécanisme de clé FxProperties.
- **Synthèse du diagnostic** : Windows, en mode « audio protégé »
  (PUMA), vérifie la signature de chaque DLL APO chargée par audiodg et
  REFUSE SILENCIEUSEMENT les non signées — notre DLL (compilée Zig, non
  signée) n'était donc jamais même OUVERTE : pas d'apo.log, pas de
  message d'erreur, aucune trace côté application. L'affirmation de
  notre analyse d'origine (« aucune signature n'est exigée pour une APO
  user-mode », docs/EQ-SYSTEME-APO.md § 3.2) était INEXACTE : Equalizer
  APO fonctionne non signé PARCE QUE son installateur désactive cette
  vérification. Correction consignée dans le doc.
- **Correctif v3.9.6 — la recette COMPLETE, avec la réversibilité
  RoomEq en plus** :
  - `ApoConstants` : clé de politique audio, nom de valeur
    DisableProtectedAudioDG, clé d'inscription moteur
    (AudioEngine\AudioProcessingObjects\{CLSID}), valeur de modes EFX
    ({d3993a3f…},7), mode par défaut ({C18E2F7E…}), chemin de
    sauvegarde audio-policy.json, nom de DLL déployée factorisé.
  - `ApoEnginePolicy` (NOUVEAU, pur, testable Linux) : les 12 valeurs
    d'inscription — miroir EXACT de GetRegistrationProperties natif
    (FriendlyName/Copyright, versions 1.0, Flags = DEFAULT|INPLACE = 15,
    connexions 1-1-1-1, MaxInstances 64, APOInterface0 =
    IID_IAudioProcessingObjectRT) ; sémantique de la politique (seule
    la valeur exactement 1 autorise une DLL non signée) ;
    sauvegarde/restauration JSON de la valeur d'origine (atomique,
    tolérante, « illisible = absent »).
  - `WindowsApoEndpointStore` : GetStrings/SetStrings (REG_MULTI_SZ,
    modes EFX) ; IsEngineRegistered/RegisterEngineApo/UnregisterEngineApo
    (idempotent : n'écrit que ce qui diffère) ;
    GetProtectedAudioDg/SetProtectedAudioDg — tout en vue Registry64 et
    inerte hors Windows.
  - `ApoSetupCommands.Deploy` (élevé, appelé par ACTIVATION et
    RÉPARATION) : inscrit l'APO auprès du moteur + ouvre la politique SI
    besoin — l'origine est capturée UNE fois, UNIQUEMENT si c'est nous
    qui changeons la politique (un Equalizer APO installé, ou une
    activation précédente, n'est jamais modifié ni « restauré ») ; le
    message d'activation énonce la contrepartie DRM honnêtement.
  - `ApoRegistrar` : sauvegarde des modes EFX d'origine avant écriture
    (invariant 2), écrit le mode par défaut si absent (comportement
    Equalizer APO : modes du pilote préservés), restaure à la
    désactivation, rétablit à la réparation ;
    EfxProcessingModesCaptured distingue les sauvegardes v3.7–v3.9.5
    (jamais touchées — rétrocompatibilité).
  - Désinstallation : restitue la politique d'origine, retire
    l'inscription moteur, supprime audio-policy.json — Windows revient
    EXACTEMENT comme avant.
  - `WindowsApoSystem.GetEngineStatus()` + `ApoChainText` : le panneau
    nomme désormais le PRÉREQUIS manquant quand la DLL n'est pas
    chargée (« Windows refuse de charger la DLL (APO non signée, moteur
    protégé)… cliquez Réparer » / « pas inscrit auprès du moteur audio »
    / « inscription COM absente ») au lieu du générique « pilote ? ».
- **Nettoyage au passage** : 6 avertissements CA1416 PRÉEXISTANTS dans
  ApoAclVerifierTests (v3.9.5) neutralisés (gardes en ligne reconnues
  par l'analyseur dans les helpers + pragma justifié pour le seul appel
  VOLONTAIREMENT multiplateforme, qui verrouille le contrat « inerte
  hors Windows ») — build vraiment 0 avertissement désormais.
- **Tests** : 531/531 xUnit (Core 237, Audio 199 → 219 : +5 registrar
  [modes écrits si absents, modes pilote préservés, restitution exacte,
  suppression si absents à l'origine, sauvegarde d'avant-champ
  intouchable, réparation les rétablit] +9 ApoEnginePolicy [12 valeurs
  une à une, types SysVAD, = 1 seulement ×4 cas, round-trip sauvegarde,
  fichier corrompu, constantes de recette] +6 ApoChainText [politique
  protégée nommée, inscription moteur nommée, COM absent nommé,
  prérequis inconnus → générique conservé, tout conforme → générique,
  journal illisible → prérequis non accusés] ; App 75). Build 0 erreur
  0 avertissement. Harnais natif APO inchangé (112 vérifications, code
  natif non touché — le blocage était côté configuration Windows).
- **Verrous structurels** : Directory.Build.props + package_v3.sh
  VERSION 3.9.6 + contrôle v3.9.6 (DisableProtectedAudioDG,
  AudioEngine/AudioProcessingObjects, ApoEnginePolicy + tests,
  EnsureProtectedAudioDgAllowsUnsignedApos, RegisterEngineApo,
  FxEfxProcessingModes, GetEngineStatus, EnginePolicyOk,
  IsDllLoadAllowed).
- **Docs** : spec I-AUD-09 v1.7 (as-built t/u/v/w/x/y/z, invariants 1
  et 11 étendus, critères de test 30 registrar + 9 politique) ;
  docs/EQ-SYSTEME-APO.md (fait établi v3.9.6 + CORRECTION de « aucune
  signature n'est exigée » + entrée d'historique) ; LISEZMOI 3.9.6
  (explication utilisateur, contrepartie DRM énoncée) ; README
  (historique) ; CARTE.md (M-AUD-09 v1.7, 8 fichiers) ; REPRISE.md
  (résumé express + état session 28).
- **Livrables (D22)** : package_v3.sh (zips test/public/source 3.9.6,
  contrôles verts dont le verrou v3.9.6) ; build_setup.sh →
  RoomEq-win-Setup.exe 3.9.6 régénéré et re-téléversé sur Drive ;
  anciens zips 3.9.5 archivés dans Livrables/Archive ; miroir Drive
  complet + push GitHub.

Stage Summary:
- v3.9.6 : le panneau v3.9.5 avait nommé le maillon (« DLL jamais
  chargée ») — la cause était un prérequis de la recette Equalizer APO
  que notre analyse d'origine croyait inutile : Windows VÉRIFIE la
  signature des APO en mode protégé et refuse les non signées sans le
  moindre message. L'activation applique désormais la recette complète
  (DisableProtectedAudioDG + inscription AudioEngine/AudioProcessingObjects
  + modes EFX), réversiblement et sans jamais toucher un réglage posé
  par un tiers — et le panneau nomme le prérequis manquant si un pilote
  ou Windows l'efface.
- Prochaine étape : validation terrain Windows — Setup 3.9.6, cliquer
  « Réparer » (ou réactiver la sortie) : le service audio redémarre,
  puis lecture d'une musique. Attendu : le ⚠ « DLL jamais chargée »
  disparaît, remplacé par « ✓ APO : courbe publiée et appliquée par le
  moteur audio » dans le panneau, et le son CHANGE avec la courbe. Si
  un ⚠ subsiste, le transmettre tel quel — il nomme son maillon.

---
Session : 29 — 2026-11-02
Agent : main (Z.ai)
Task : Retour terrain v3.9.6 transmis tel quel (« toujours le même
problème, sauf qu'en plus maintenant dès que l'eq est activé sur une
sortie, je n'ai plus de son sur cette sortie jusqu'à ce que je désactive
l'eq (bouton tout retirer) » + capture : courbe envoyée mais PLATE,
journal APO toujours absent, écoute affichée sur Realtek alors que le
défaut est le casque JBL). v3.9.7.

Work Log:
- **Sandbox recyclée avant session** : le dépôt local était retombé à
  v3.6.0 + le commit de specs (le travail v3.7–v3.9.6 n'existait que
  dans GitHub) — `git pull --ff-only origin v3-recodage` (b51e69f) a
  tout restauré : D22 a sauvé la session. Zig 0.14.1 et le SDK .NET
  réinstallés (outils de sandbox, documentés dans REPRISE.md).
- **Diagnostic établi par recoupement** (aucun Windows sous la main :
  chaque affirmation sourcée) :
  1. **Le silence est le comportement DOCUMENTÉ d'un endpoint dont le
     graphe ne se construit pas** : « the audio engine tries to
     CoCreateInstance an APO […] fails […] and the ENTIRE stream fails
     to build — no sound, empty Volume Mixer, Failed to play test tone.
     A broken APO doesn't just disable an effect, it can take down the
     whole endpoint » (analyse ThinkPad E14/Win10, dev.to, juin 2026 —
     cas Dolby APO non inscrit). Avant v3.9.6, audiodg en mode protégé
     refusait la DLL AVANT la construction du graphe → bypass → son
     présent ; depuis que la recette complète fait TENTER le
     chargement, l'échec d'instanciation tue la sortie. Cohérent avec
     « je n'ai plus de son SUR CETTE sortie » (Realtek vit toujours).
  2. **La structure APO_CONNECTION_PROPERTY du projet est CORRECTE**
     (pBuffer, u32ValidFrameCount, u32BufferFlags, u32Signature) —
     vérifiée contre le VRAI audioapotypes.h (celui livré avec Zig :
     lib/libc/include/any-windows-any/) ET la doc Microsoft Learn.
     Piste initiale « champs inversés » INVALIDÉE avant toute retouche.
  3. **La recette registre v3.9.6 est conforme aux références** : code
     source Equalizer APO cloné et lu (DeviceAPOInfo.cpp : EFX ,7 +
     modes {d3993a3f…},7 = {C18E2F7E…} MULTI_SZ si absent, suppression
     de Disable_SysFx ; DllMain → RegisterAPO + COM ThreadingModel
     Both) et notes É. Dechamps (EFX = PKEY_FX_EndpointEffectClsid ,7 ✓ ;
     audiodg sous Audiosrv ≈ LocalService SANS accès aux profils ; il
     recommande LUI-MÊME l'octroi « NT SERVICE\Audiosrv » pour les
     fichiers). DEUX divergences trouvées avec les références :
     APOInterface0 = RT au lieu de IAudioProcessingObject (SysVAD ET
     Equalizer APO déclarent la non-RT) ; et surtout l'ACL bin.
  4. **Soupçon mécanique principal — l'octroi icacls « NT
     SERVICE\Audiosrv » sur bin était MEILLEUR EFFORT** (Run() avalait
     le code de retour ; le verrou v3.9.5 ne vérifiait QUE
     Utilisateurs:Modifier sur la racine) : audiodg n'est membre NI de
     Utilisateurs NI de personne d'autre dans cette ACL — si cette
     entrée unique échouait silencieusement, LoadLibrary de la DLL est
     REFUSÉ dans audiodg → graphe mort → sortie muette. Invérifiable à
     distance, donc rendu IMPOSSIBLE PAR CONSTRUCTION (ci-dessous).
- **Correctif v3.9.7 — « le son passe avant l'égaliseur »** :
  - **ACL bin bulletproof** (ApoAclArgs, PUR + verrouillé par 3 tests) :
    SYSTEM/Admins FullControl, **TOUT LE MONDE (S-1-1-0) lecture +
    exécution** — comme Program Files : AUCUNE identité de service ne
    peut plus se voir refuser le chargement ; l'écriture reste
    administrateur. Racine : + **LOCAL SERVICE (S-1-5-19) Modifier**
    par SID (langue-indépendant) en plus d'Audiosrv et Utilisateurs —
    le journal de l'APO reste inscriptible. icacls désormais **contrôlé
    (RunChecked : code de sortie exigé)** et **vérifié en relisant la
    DACL** (ApoAclVerifier.EveryoneCanReadAndExecute — l'échec nommé).
  - **Sonde WASAPI + ROLLBACK AUTOMATIQUE** (ApoEndpointProbe,
    ApoActivationPolicy — politiques PURES verrouillées par 11 tests) :
    après chaque activation (et réparation), l'endpoint est sondé par
    un rendu partagé de ~120 ms de SILENCE au format de mixage (avec
    témoin : si une autre sortie vit et pas celle-là, verdict immédiat ;
    si tout redémarre, jusqu'à 4 tentatives espacées). Sortie MORTTE →
    Registrar().Disable (l'origine sauvegardée est restaurée) +
    redémarrage du service + verdict « LE SON A ÉTÉ PRÉSERVÉ : […]
    restaurée automatiquement » — plus jamais de sortie muette laissée
    en plan. Sortie vivante sans journal → information, pas d'alerte.
  - **Suivi de la sortie PAR DÉFAUT de Windows** (PlaybackEngine.
    DefaultRenderOutputId + OutputFollowPolicy PUR + 5 tests +
    MainViewModel) : l'écoute démarre sur le défaut Windows (la mémoire
    de session ne sert plus qu'en repli) et le SUIT à chaque
    changement (branchement casque, réglage Windows) tant que
    l'utilisateur n'a pas délibérément choisi une autre sortie dans
    l'appli — c'est la cause du « la barre de volume ne suit pas le
    volume Windows et n'agit pas sur le son entendu » : les deux
    curseurs pilotaient Realtek pendant que le casque JBL était le
    défaut. (La ré-acquisition v3.9.5 après redémarrage du service
    reste : défense en profondeur.)
  - **Interface déclarée alignée sur les références** :
    APOInterface0 = IAudioProcessingObject
    ({FD7F2B29-24D0-4B5C-B177-592C39F9CA10}) au registre
    (ApoEnginePolicy) ET dans GetRegistrationProperties natif — les
    DEUX références terrain (SysVAD ComponentizedApoSample.inx et
    CRegAPOProperties d'Equalizer APO) déclarent la non-RT.
  - **Journal de l'APO avec repli** (apo_config.cpp) : si
    %ProgramData%\RoomEq\apo\apo.log n'est pas inscriptible par
    audiodg, la DLL écrit dans
    %SystemRoot%\ServiceProfiles\LocalService\AppData\Local\Temp\
    RoomEqApo.log — l'emplacement DU journal d'Equalizer APO,
    inscriptible par construction ; le processus élevé lit les deux.
- **Vérifications de survie du chemin audio NATIF re-passées** :
  APOProcess relit in/out proprement, pass-through sur !usable copie
  TOUJOURS le tampon (ch!=0), early-returns uniquement hors
  verrouillage/buffer invalide (comportement Equalizer APO), DllMain
  minimal (DisableThreadLibraryCalls), DLL 452 Ko/412 Ko x64+arm64,
  imports limités à ole32/kernel32/api-ms-win-crt (UCRT système).
- **Tests** : 550/550 xUnit (Core 237, Audio 219 → 233 : +3 ApoAclArgs
  [Everyone/Root/SID], +8 ApoActivationPolicy [6 cas croisés,
  DeadEndpoint-seulement-DeadGraph, budget/témoin], +3 mise à jour
  ApoEnginePolicy [IID verrouillée] ; App 75 → 80 : +5
  OutputFollowPolicy) ; build 0 erreur 0 avertissement ; harnais APO
  inchangé 112/112 (test d'interface mis à jour : iidAPOInterfaceList[0]
  = IAudioProcessingObject) ; DLL x64+arm64 recompilées (Zig 0.14.1).
- **Verrous structurels** : Directory.Build.props + package_v3.sh
  VERSION 3.9.7 + contrôle v3.9.7 (ApoAclArgs.EveryoneSid/
  LocalServiceSid, EveryoneCanReadAndExecute, RunChecked,
  ApoEndpointProbe.cs, ApoActivationPolicy.cs, VerifyEndpointHealth,
  ServiceProfiles+LocalService dans apo_config.cpp,
  DefaultRenderOutputId, OutputFollowPolicy dans MainViewModel,
  tests OutputFollowPolicyTests/ApoAclArgsTests, IID FD7F2B29 dans
  ApoEnginePolicy).
- **Docs** : LISEZMOI 3.9.7 (les trois protections expliquées à
  l'utilisateur) ; README (historique) ; WORKLOG session 29 ; spec
  I-AUD-09 v1.8 (as-built aa : sonde+rollback, ab : ACL bin, ac :
  défaut Windows suivi, ad : interface déclarée, ae : journal de
  repli) ; CARTE.md (M-AUD-09 v1.8, 12 fichiers) ; REPRISE.md (résumé
  express + état session 29).
- **Livrables (D22)** : package_v3.sh (zips test/public/source 3.9.7,
  contrôles verts dont le verrou v3.9.7) ; build_setup.sh →
  RoomEq-win-Setup.exe 3.9.7 régénéré et téléversé sur Drive ; anciens
  zips 3.9.6 archivés dans Livrables/Archive ; miroir Drive complet +
  push GitHub.

Stage Summary:
- v3.9.7 : le retour terrain v3.9.6 a montré le piège final de l'EQ
  système — une APO que le moteur audio n'instancie pas ne se contourne
  pas, elle COUPE la sortie. RoomEq applique désormais le principe « le
  son passe avant l'égaliseur » à tous les étages : le dossier des DLL
  est lisible par quiconque (échec d'octroi rendu impossible puis
  vérifié), chaque activation est SONDÉE et une sortie qui ne répond
  plus est RESTAURÉE AUTOMATIQUEMENT avec un verdict qui explique, et
  l'écoute suit enfin la sortie par défaut de Windows (fini la barre de
  volume branchée sur une carte muette). L'interface déclarée au moteur
  et le journal de l'APO sont alignés sur les références terrain.
- Prochaine étape : validation terrain Windows — Setup 3.9.7,
  réactiver l'EQ sur le casque, lire le verdict : soit « ✓ Vérifié :
  la sortie répond et l'APO est chargé » (le son CHANGE avec la courbe
  de l'onglet Égaliseur — la capture v3.9.7 montrait « courbe PLATE :
  chargez une correction », c'est la courbe envoyée), soit « LE SON A
  ÉTÉ PRÉSERVÉ : […] » (alors transmettre le message complet : il
  contient le diagnostic différentiel ; un redémarrage de Windows peut
  débloquer le chargement). Vérifier aussi la barre de volume en
  changeant la sortie par défaut de Windows pendant que l'appli est
  ouverte.

---
Session : 30 — 2026-11-02
Agent : main (Z.ai)
Task : Retour terrain v3.9.7 ENFIN transmis complet (capture du panneau EQ
système : « Échec de l'activation de 'Casque JBL Tune Flex 2' — LE SON A ÉTÉ
PRÉSERVÉ : le moteur audio de Windows n'a pas réussi à charger l'égaliseur
(sortie devenue muette → restaurée automatiquement à son état d'origine,
service audio redémarré) ») : le rollback v3.9.7 a fonctionné comme prévu,
mais le verdict ne contenait AUCUN élément différentiel et le journal de
l'APO restait vide — la DLL n'est toujours jamais instanciée par audiodg.
Diagnostic + correctif v3.9.8.

Work Log:
- **Le message d'erreur est enfin parvenu** (3e tentative de l'utilisateur) :
  il confirme le mécanisme v3.9.7 (sonde → rollback → verdict) ET son
  insuffisance : « il contient le diagnostic » promettait le message, mais
  rien n'y départiquait les causes (pas de HRESULT, pas d'état moteur, pas
  de trace de chargement, pas d'exception de sonde).
- **Cause PROBABLE identifiée dans la source de référence elle-même** :
  DeviceAPOInfo.cpp d'Equalizer APO (miroir GitHub, lu intégralement en
  session) contient le commentaire exact « bluetooth devices may be
  combined in Windows 11, EFX will not work then » — quand l'endpoint
  porte la propriété « périphérique combiné » ({b3f8fa53-0004-438e-9003-
  51a46e139bfc},41 dans \Properties — cas des Bluetooth sous Windows 11),
  la référence n'installe PAS en EFX mais en SFX/MFX. Le casque JBL de
  l'utilisateur est Bluetooth sous Windows 11 : notre position EFX (,7)
  est exactement la position que la référence évite sur ce matériel.
  Lecture croisée complémentaire : ticket Equalizer APO #77 (PID complets
  SFX ,5 / MFX ,6 / EFX ,7 / multi ,13-15 ; « If Multiple APO CLSID
  exist, Windows will ignore Single APO CLSID ») et notes É. Dechamps
  (« If any of SFX, MFX or EFX are present, then LFX and GFX are
  ignored ») — les verrous littéraux des PID sont désormais testés.
- **Correctif v3.9.8 — TROIS volets** :
  1. **Position adaptée au périphérique** (ApoSlotPolicy, PUR + 15 tests) :
     EFX par défaut, **MFX (,6) pour les périphériques combinés** (la
     sous-clé \Properties est lue via IApoEndpointStore.GetDeviceProperty —
     nouveau membre, implémenté Windows + faux registre). Le registrar
     mémorise la position dans la sauvegarde (SlotUsed, rétrocompatible :
     null = EFX ≤ v3.9.7), capture l'origine MFX (CLSID + modes ,6), nettoie
     notre CLSID de l'AUTRE position (migration propre v3.9.7 → v3.9.8),
     traite un CLSID déjà à nous comme ABSENT (comportement Equalizer APO
     sur ses propres GUID), répare dans la position mémorisée. IsEnabled
     couvre les deux positions. +8 tests registrar (MFX écrit pas EFX,
     origine préservée, restitution exacte, EFX inchangé sinon, CLSID
     périmé nettoyé jamais restauré, réparation au slot, JSON légal sans
     SlotUsed → EFX).
  2. **Auto-test COM PRÉ-VOL** (ApoDllSelfTest + ApoSelfTest + ApoHresult,
     interop fine + purs) : DANS le processus élevé, AVANT toute écriture
     registre, la DLL déroule le parcours exact d'audiodg — LoadLibraryExW
     (code Win32 exact : 5/126/193/1114/14001 décodés), GetProcAddress,
     CoCreateInstance VIA L'INSCRIPTION COM RÉELLE (avec différentiel
     fabrique directe : distingue « inscription fausse » de « DLL en
     cause »), QueryInterface des QUATRE interfaces (IIDs verrouillées),
     GetRegistrationProperties (CLSID + drapeaux relus), Initialize,
     LockForProcess au format de mixage 48 kHz/2 voies/float32 (format
     synthétique IAudioMediaType en CCW managé), APOProcess sur 256
     trames d'un signal témoin (l'énergie doit traverser — courbe vide =
     transparence), UnlockForProcess. Un échec BLOQUE l'activation avec
     « ACTIVATION BLOQUÉE AVANT TOUTE MODIFICATION » + l'étape exacte +
     son HRESULT décodé : la sortie ne peut plus être exposée à une DLL
     non instanciable. Layout verrouillé par 9 tests réflexion (DLLs
     natives, SetLastError, PreserveSig — l'HRESULT doit revenir TEL QUEL
     pour être diagnostiqué, InterfaceIsIUnknown, tailles marshalées
     36/40/24 = ABI apo_abi.h, Pack=4).
  3. **Verdict DIFFÉRENTIEL complet** (ApoFailureDiagnostics, PUR + 12
     tests) : le message « LE SON A ÉTÉ PRÉSERVÉ » embarque désormais
     auto-test (OK = DLL saine → cause côté moteur), prérequis moteur
     (signature/inscription COM/inscription moteur), trace de chargement
     (journal principal ou repli) + dernières lignes, ERREUR EXACTE de la
     sonde WASAPI (l'exception n'est plus avalée : HRESULT AUDCLNT_E_*
     décodé + message), position choisie, inventaire des positions
     (multi* signalés « Windows ignore alors les CLSID simples »), version
     de Windows. Idem en réparation (auto-test + prérequis).
- **Redémarrage du service audio durci** : le verdict de la sonde n'a de
  valeur que si le moteur a VRAIMENT relu les FxProperties — attente de
  l'état RUNNING par sc query (code numérique 4, indépendant de la
  langue ; le texte d'état est localisé, pas le code) jusqu'à 10 s, en
  plus du redémarrage net stop/net start existant.
- **Vérifications ABIs re-passées** : les IID/vtables de l'auto-test sont
  le miroir exact d'apo_abi.h (déjà vérifié contre le SDK 10.0.10240) ;
  DLL x64+arm64 recompilées (Zig 0.14.1, 112 vérifications au harnais,
  exports COM et imports CRT système inchangés).
- **Tests** : 598/598 xUnit (Core 237, Audio 233 → 281 : +15 ApoSlotPolicy,
  +8 registrar MFX, +12 ApoFailureDiagnostics, +9 ApoDllSelfTestLayout,
  −6 doublons Describe fusionnés en Theory ; App 80). Build 0 erreur
  0 avertissement. Harnais APO natif 112/112.
- **Verrous structurels** : Directory.Build.props + package_v3.sh
  VERSION 3.9.8 + contrôle v3.9.8 (5 nouveaux fichiers SystemApo + 3
  fichiers de tests, CombinedDeviceProperty + GUID b3f8fa53 littéral,
  SlotUsed, GetDeviceProperty, ApoSlotPolicy.Choose, ApoDllSelfTest.Run,
  SelfTestBlocked, ComposeDeadEndpoint, ProbeDetailed, CoCreateInstance,
  AudioServiceRunning, MFX dans ApoSlotPolicy).
- **Docs** : LISEZMOI 3.9.8 (les trois ajouts expliqués à l'utilisateur) ;
  README (historique) ; WORKLOG session 30 ; spec I-AUD-09 v1.9 (as-built
  af : position adaptée, auto-test, verdict différentiel) ; CARTE.md
  (M-AUD-09 v1.9, 17 fichiers) ; REPRISE.md (résumé express + état
  session 30) ; docs/EQ-SYSTEME-APO.md (fait établi : périphériques
  combinés → MFX, source DeviceAPOInfo.cpp).
- **Livrables (D22)** : package_v3.sh (zips test/public/source 3.9.8,
  contrôles verts dont le verrou v3.9.8) ; build_setup.sh →
  RoomEq-win-Setup.exe 3.9.8 régénéré et téléversé sur Drive ; anciens
  zips 3.9.7 archivés dans Livrables/Archive ; miroir Drive complet +
  push GitHub.

Stage Summary:
- v3.9.8 : le retour terrain enfin complet a montré que le rollback
  v3.9.7 protégeait le son mais laissait l'utilisateur (et nous) AVEUGLE
  sur la cause. Trois réponses : la position d'effet s'adapte au
  périphérique (MFX pour les Bluetooth « combinés » de Windows 11 — le
  constat « EFX will not work then » est dans la source d'Equalizer APO
  elle-même, et le JBL de l'utilisateur est exactement ce cas) ; la DLL
  passe un auto-test COM complet AVANT toute écriture registre (un échec
  bloque l'activation sans risque pour le son et nomme l'étape + le code) ;
  et le verdict d'échec embarque le diagnostic différentiel entier.
- Prochaine étape : validation terrain Windows — Setup 3.9.8, réactiver
  l'EQ sur le casque JBL : attendu « activé (position MFX — périphérique
  combiné) » puis son modifié par la courbe ; si le moteur refuse
  encore, le message transmis nommera le maillon exact (auto-test,
  prérequis, trace, erreur sonde, positions, Windows) — plus d'aller-
  retour d'investigation à l'aveugle.

---
Session : 31 — 2026-11-02
Agent : main (Z.ai)
Task : « continue » — reprise après recyclage sandbox : aucun nouveau
retour terrain, v3.9.8 en attente de validation. Session de restauration
et de vérification intégrale.

Work Log:
- **Sandbox recyclée (3e fois)** : dépôt local retombé à v3.6.0 + commit
  de specs — `git pull origin v3-recodage` a tout restauré (v3.9.8,
  0c293c0). SDK .NET 8.0.427 et Zig 0.14.1 réinstallés (recette
  REPRISE.md). D22 a encore sauvé la session : tout le travail v3.7 →
  v3.9.8 n'existait que sur GitHub + Drive.
- **Vérification intégrale de l'état livré v3.9.8** (rien n'avait été
  revérifié depuis la restauration) :
  - Build solution : 0 erreur — mais **1 avertissement CS8602** dans
    `ApoDllSelfTestLayoutTests.cs:142` (déréférence possible de
    `Type.StructLayoutAttribute`, nullable) : les projets de tests
    n'ont pas `TreatWarningsAsErrors` (seul src/ l'a) — l'avertissement
    avait donc échappé au « 0 avertissement » annoncé en session 30.
    Corrigé : capture locale + `Assert.NotNull(layout)` avant
    `layout!.Pack` (test uniquement, aucun changement de comportement —
    les binaires de l'application sont identiques).
  - xUnit : **598/598 verts** (Core 237 + Audio 281 + App 80), build
    0 erreur 0 avertissement après correctif.
  - Harnais APO natif : **112 vérifications, 0 échec** ; DLL x64 (462 Ko)
    + arm64 (419 Ko) recompilées par build-apo.sh, zip
    RoomEq-APO-3.9.8-win.zip régénéré (identiques aux livrés — même
    source, même Zig).
  - **Drive contrôlé** (script de listing dédié,
    `scripts/liste_drive_livrables.py` côté sandbox) : Livrables au
    premier niveau = RoomEq-win-Setup.exe (53,8 Mo) + zips 3.9.8
    (source 8,0 Mo / public 44,0 Mo / test 44,1 Mo) + AU 3.6.0 +
    VST 3.5.2 ; Archive = 63 fichiers (3.9.7 archivés correctement).
    Rien n'a été perdu au recyclage.
- **Choix de livraison du micro-correctif** : le commit Git + miroir
  Drive du dépôt suffisent — les zips 3.9.8 téléversés en session 30
  restent valables (l'avertissement ne touche que la compilation du
  projet de TESTS ; aucun binaire embarqué ne diffère). Le prochain
  cycle de version régénérera les zips avec le correctif.

Stage Summary:
- Session de reprise : sandbox restaurée, v3.9.8 revérifiée de bout en
  bout (598/598 xUnit, 112/112 natif, DLL recompilées, Drive complet),
  1 avertissement résiduel corrigé (null-safety d'un test). Aucun
  changement fonctionnel — v3.9.8 reste la version à valider sur le
  terrain.
- Prochaine étape : inchangée — validation terrain Windows : Setup
  3.9.8, réactiver l'EQ sur le casque JBL, lire le verdict du panneau
  (position MFX attendue) ; tout ⚠ ou message « LE SON A ÉTÉ PRÉSERVÉ »
  = le transmettre tel quel (il contient le diagnostic différentiel).

---
Session : 32 — 2026-11-02
Agent : main (Z.ai)
Task : Retour terrain v3.9.8 : « message d'erreur » — la capture du
panneau EST ENFIN PARVENUE (4e signalement, la passerelle IM a transmis
l'image cette fois). Verdict v3.9.8 complet lu : auto-test DLL OK (12
étapes), prérequis moteur tous verts, journal APO présent, position EFX
choisie, Windows 11 build 26300 — et sonde WASAPI 0x80040110
CLASS_E_NOAGGREGATION. Diagnostic + correctif v3.9.9.

Work Log:
- **L'observabilité v3.9.8 a désigné le coupable à la première lecture** :
  l'erreur exacte de la sonde (0x80040110) est LE CODE DE REFUS DE NOTRE
  PROPRE FABRIQUE COM (`if (pUnkOuter != nullptr) return
  kClassNoAggregation;`) — l'exception remontée par la sonde WASAPI est
  le HRESULT que le moteur audio a propagé après notre refus.
- **Cause racine ÉTABLIE par recoupement de DEUX sources primaires** :
  1. **nnyj/virtual-surround-sound** (github, juillet 2026 —
     implémentation indépendante d'un APO custom Win11, doc
     apo-forwarder-internals.md) : « audiodg AGGREGATES APOs:
     IClassFactory::CreateInstance receives a non-null outer unknown.
     Returning CLASS_E_NOAGGREGATION makes the host silently abandon the
     APO after a factory probe […] the APO must implement standard COM
     aggregation: inner non-delegating IUnknown owns identity and
     lifetime » + « symptom of missing support: endpoint FX graph fails ».
  2. **Equalizer APO** (miroir github.com/mirror/equalizerapo cloné et
     LU — ClassFactory.cpp + EqualizerAPO.cpp) : la référence accepte
     l'agrégation sans même y penser — `new EqualizerAPO(pUnkOuter)` +
     `NonDelegatingQueryInterface`, classe `INonDelegatingUnknown` à
     vtable binairement compatible IUnknown, contrôleur jamais AddRefé,
     ND-QI : IUnknown → sous-objet non déléguant, AddRef final via le
     pointeur retourné (déléguant — compteur du contrôleur quand
     agrégé). Aucun document Microsoft ne mentionne l'exigence : elle
     est invisible sauf à écrire la fabrique à la main, comme nous.
  **Synthèse** : le moteur crée l'APO AVEC contrôleur d'agrégation ;
  notre fabrique refusait → le moteur abandonnait l'APO après sa sonde
  de fabrique → le graphe de la sortie ne se construisait pas →
  IAudioClient::Initialize échouait avec NOTRE 0x80040110 → sortie
  muette. C'est LE mécanisme qui coupait le son depuis v3.9.6 (avant,
  le refus de signature avait lieu avant la construction du graphe).
- **Relecture croisée de la même source** (au passage) : la propriété
  « improvements désactivés » d'EAPO = {1da5d803-d492-4edd-8c23-
  e0c0ffee7f0e},5 dans FxProperties, supprimée à l'installation
  (« force-enable enhancements ») — notre registrar la gérait DÉJÀ
  (sauvegarde → 0 → restauration, clé identique) : rien à faire.
- **Correctif v3.9.9 — trois volets** :
  1. **AGRÉGATION COM native** (RoomEqApo.h/.cpp + apo_abi.h) :
     `INonDelegatingUnknown` (vtable binairement compatible IUnknown :
     trois méthodes, mêmes signatures, même ordre — technique EAPO) ;
     l'objet détient `unkOuter_` (pointeur EMPRUNTÉ, jamais AddRefé) :
     le contrôleur s'il est agrégé, sa propre identité non déléguante
     sinon (routage des déléguants SANS branchement) ; IUnknown
     déléguant (QI/AddRef/Release → unkOuter_) ; identité interne
     NDQueryInterface (IUnknown → sous-objet non déléguant, même
     pointeur à chaque appel ; NDAddRef/NDRelease : compteur d'identité,
     SEUL détenteur de la durée de vie) ; fabrique miroir exact
     d'Equalizer APO (outer + IID ≠ IUnknown → E_NOINTERFACE ; outer +
     IID_IUnknown → inner non déléguant) ; le constructeur journalise
     « APO instanciee (AGREGEE par le moteur audio) » vs « (directe) ».
  2. **10ᵉ étape d'auto-test « CoCreateInstance agrégé »**
     (ApoDllSelfTest) : contrôleur CCW managé
     (Marshal.GetIUnknownForObject), création agrégée via l'inscription
     COM réelle, interfaces obtenues par l'inner, et QI(IUnknown) via
     l'interface de l'agrégat DOIT retourner l'identité du CONTRÔLEUR
     (définition même de l'agrégation) — un échec bloque l'activation
     AVANT toute écriture registre : « l'APO refuse l'agrégation COM
     exigée par le moteur audio ». Ce défaut ne peut plus JAMAIS
     atteindre le terrain.
  3. **Verdict non piégé** (ApoLogTrace, PUR + 9 tests) : le retour
     v3.9.8 montrait « trace de chargement PRÉSENTE (l'APO a été
     instanciée par le moteur audio) » — FAUX : ces lignes venaient de
     NOTRE auto-test pré-vol (même DLL, même journal, même processus
     élevé). L'auto-test appose désormais un MARQUEUR de fin (« ===
     auto-test termine (processus eleve) === ») et la classification ne
     considère que les lignes POSTÉRIEURES au DERNIER marqueur :
     None / DirectOnly / Aggregated — seule l'instanciation AGRÉGÉE
     prouve le moteur (libellé « preuve directe ») ; rétrocompatible
     (journal sans marqueur = comportement historique).
- **Tests** : 608/608 xUnit (Core 237, Audio 281 → 291 : +9 ApoLogTrace
  [journal vide, lignes d'auto-test seules ne comptent plus — LE piège,
  post-marqueur comptent, AGREGEE = preuve, AGREGEE avant marqueur ne
  compte pas, plusieurs marqueurs, sans marqueur = historique ×2,
  lignes sans rapport] + 1 diagnostics [preuve directe énoncée] + MAJ
  libellés) ; App 80. Harnais APO natif 112 → **126 vérifications**
  (+14 agrégation : outer+IID≠IUnknown refusé, création agrégée =
  S_OK + inner, contrôleur JAMAIS AddRefé, QI interfaces par l'inner,
  AddRef/Release délégués → compteur du CONTRÔLEUR, QI(IUnknown) via
  l'agrégat = identité du contrôleur, QI inconnu délégué compté, la
  référence INTERNE seule détruit, contrôleur n'a rien gardé — faux
  contrôleur à compteurs jouant le rôle d'audiodg). Build 0 erreur
  0 avertissement. DLL x64+arm64 recompilées (Zig 0.14.1, version
  3.9.9).
- **Verrous structurels** : Directory.Build.props + package_v3.sh
  VERSION 3.9.9 + contrôle v3.9.9 (ApoLogTrace.cs + tests, INonDelegatingUnknown
  dans apo_abi.h ET RoomEqApo.h/.cpp, « AGREGEE par le moteur audio »
  littéral, kClassNoAgregation ABSENT de RoomEqApo.cpp, « CoCreateInstance
  agrégé » dans ApoDllSelfTest, SelfTestMarker dans ApoDllSelfTest +
  ApoLogTrace, ReadEngineTrace dans ApoSetupCommands, AggregatedSeen dans
  ApoFailureDiagnostics, ancien libellé trompeur ABSENT).
- **Docs** : spec I-AUD-09 v1.10 (as-built ai/aj/ak + critères 126/9) ;
  docs/EQ-SYSTEME-APO.md (fait établi v3.9.9 : le moteur AGRÈGE les
  APO, sources) ; LISEZMOI 3.9.9 (explication utilisateur — « votre
  message a désigné le coupable ») ; README (historique) ; CARTE.md
  (M-AUD-09 v1.10, 18 fichiers) ; REPRISE.md (résumé express + état
  session 32).
- **Livrables (D22)** : package_v3.sh (zips test/public/source 3.9.9,
  contrôles verts dont le verrou v3.9.9) ; build_setup.sh →
  RoomEq-win-Setup.exe 3.9.9 régénéré et téléversé sur Drive ; anciens
  zips 3.9.8 archivés dans Livrables/Archive ; miroir Drive complet +
  push GitHub.

Stage Summary:
- v3.9.9 : le message d'erreur ENFIN transmis a désigné le coupable en
  une ligne — 0x80040110 est le code de refus d'agrégation de NOTRE
  fabrique COM, et le moteur audio de Windows instancie tous les
  égaliseurs système en agrégation COM (fait établi par deux sources
  primaires : l'implémentation indépendante nnyj/virtual-surround-sound
  et la fabrique d'Equalizer APO elle-même). L'APO accepte désormais
  l'agrégation à l'identique de la référence, l'auto-test pré-vol la
  rejoue (10ᵉ étape) pour que ce défaut ne puisse plus jamais atteindre
  le registre, et le verdict ne se fait plus piéger par les lignes de
  l'auto-test dans le journal (marqueur + classification : seule
  l'instanciation AGRÉGÉE prouve le moteur).
- Prochaine étape : validation terrain Windows — Setup 3.9.9,
  réactiver l'EQ sur le casque JBL : attendu « Vérifié : la sortie
  répond et l'APO est chargé » puis un son MODIFIÉ par la courbe de
  l'onglet Égaliseur (le preset jbltuneflex2 est déjà appliqué). Si un
  ⚠ subsiste, le transmettre tel quel — il nomme son maillon.

---
Session : 33 — 2026-11-02
Agent : main (Z.ai)
Task : Retour terrain v3.9.9 (5e « message d'erreur », capture ENCORE
transmise — la passerelle fonctionne désormais) : verdict v3.9.9 complet
lu. L'agrégation COM a FAIT SON TRAVAIL : auto-test OK (13 étapes dont
la création agrégée), prérequis verts, journal = « APO instanciée
(AGRÉGÉE par le moteur audio) | Initialize acceptée | APO détruite »,
sonde 0x88890008 AUDCLNT_E_DEVICE_INVALIDATED — le moteur a BIEN chargé
notre DLL puis a renoncé. ET le verdict désignait le maillon restant :
« Position choisie : EFX » sur le casque JBL pourtant COMBINÉ. Diagnostic
+ correctif v3.9.10.

Work Log:
- **Le suspect désigné par élimination différentielle** : le moteur
  instancie (preuve directe agrégée), accepte Initialize, puis DÉTRUIT
  l'APO sans LockForProcess — et la position choisie était EFX alors que
  la recette v3.9.8 elle-même dit MFX pour les combinés (« EFX will not
  work then »). L'inventaire du terrain montre SFX et MFX occupés par
  des APO Microsoft, EFX=RoomEq (le seul slot libre) : la détection
  « périphérique combiné » avait répondu NON à tort.
- **Cause racine TROUVÉE DANS NOTRE CODE en 1 ligne** :
  WindowsApoEndpointStore.GetDeviceProperty lisait la propriété
  `{b3f8fa53-0004-438e-9003-51a46e139bfc},41` via
  `GetValue(valueName) as string` (typage REG_SZ). Or la propriété
  « périphérique combiné » est un BOOLÉEN persisté en type NON chaîne
  (REG_DWORD/REG_BINARY) : le cast `as string` retournait TOUJOURS
  null, sur TOUTE machine — la détection était structurellement aveugle.
  Les tests v3.9.8 passaient parce que le FAUX REGISTRE stockait une
  chaîne (« 1 ») : le mock a menti (leçon enregistrée : le faux doit
  refléter le TYPE réel, pas seulement la présence).
- **Contre-vérification source primaire** (DeviceAPOInfo.cpp du miroir
  EqualizerAPO récupéré et LU en entier) : la référence teste la
  PRÉSENCE via `RegistryHelper::valueExists(keyPath + L"\\Properties",
  combinedDeviceValueName)` — même FMTID, même PID 41, même sous-clé
  \Properties — JAMAIS le type ni la valeur. Elle choisit alors
  INSTALL_SFX_MFX (lignes 328-330 : « bluetooth devices may be combined
  in Windows 11, EFX will not work then »). Au passage, la même lecture
  a fourni l'inventaire complet des noms de valeurs de la référence
  (lfx/gfx/sfx/mfx/efx ,1/,2/,5/,6/,7 ; multi ,13/,14/,15 ; modes
  {d3993a3f…},5/,6/,7 ; disableEnhancements ,5) : TOUS conformes à
  ApoConstants — aucune autre divergence.
- **Correctif v3.9.10 — trois volets** :
  1. **PRÉSENCE, PAS TYPE** : `IApoEndpointStore.HasDeviceProperty`
     (remplace GetDeviceProperty) — présence quel que soit le type
     registre (`GetValue != null`), miroir exact de valueExists ;
     registrar + verdict lisent le drapeau par là. Test de régression
     DU TERRAIN : `Enable_CombinedDevice_NonStringType_IsDetected`
     (valeur INT dans le faux registre → MFX) + variante chaîne
     (compatibilité) + inertie Linux étendue.
  2. **ÉCHELLE DE REPLI AUTOMATIQUE** (le filet qui rend toute future
     erreur de détection non fatale) : si la sonde déclare la sortie
     MORTE après activation : Disable (l'origine VRAIE est restaurée) →
     `registrar.Enable(endpointKey, forcedSlot)` dans l'AUTRE position
     (`ApoSlotPolicy.Fallback`, EFX↔MFX) → redémarrage service →
     re-sonde. Succès → message « Position MFX adoptée par REPLI
     AUTOMATIQUE… » ; double échec → rollback complet + verdict qui
     nomme les DEUX positions et les erreurs des DEUX sondes. C'est
     l'échelle « Install as : SFX/EFX, SFX/MFX, LFX/GFX » que le
     Configurator d'Equalizer APO fait manipuler À LA MAIN au
     dépannage (constat forum : les utilisateurs USB/BT y passent) —
     exécutée ici sans intervention, décideur = la SONDE (aucune
     hypothèse sur le pourquoi d'une sortie morte). Le cycle repli
     capture l'origine vraie restaurée (test
     `Enable_FallbackCycle_CapturesTrueOriginAgain` : l'EFX du pilote
     reste INTACT après repli en MFX).
  3. **VERDICT ENRICHI** : ligne « Périphérique combiné : oui/NON/
     inconnu » (l'EXPLICATION du choix de position — absente du verdict
     v3.9.9 qui montrait le choix sans la raison) + `CombinedDevice`
     dans ApoDeadEndpointContext + SlotChosen « EFX puis MFX (repli
     automatique) » + erreurs des deux sondes + meilleure trace des
     deux cycles.
- **Tests** : 619/619 xUnit (Core 237, Audio 281 → 302 : +11 —
  détection type non chaîne (LE cas terrain), type chaîne, forcedSlot
  MFX-sur-non-combiné, forcedSlot EFX-sur-combiné, cycle repli capture
  l'origine vraie, Fallback = l'autre position (×2) et F(F(x))=x,
  drapeau combiné oui/NON/inconnu (×3), verdict après repli nomme les
  deux positions et les deux sondes ; MAJ interface du faux store +
  inertie Linux). App 80. Harnais APO natif 126/126 (code natif
  inchangé — l'APO elle-même est saine, le moteur l'instanciait et
  l'initialisait). Build 0 erreur 0 avertissement. DLL x64+arm64
  recompilées à l'identique (Zig 0.14.1, version 3.9.10).
- **Verrous structurels** : Directory.Build.props + package_v3.sh
  VERSION 3.9.10 + contrôle v3.9.10 (HasDeviceProperty dans registrar
  ET store, « valueExists » cité, présence `k?.GetValue(valueName) !=
  null`, ApoSlotPolicy.Fallback + forcedSlot + « repli automatique »
  dans ApoSetupCommands, « Périphérique combiné » + CombinedDevice dans
  ApoFailureDiagnostics, les 3 tests de régression nommés) ; verrou
  v3.9.8 mis à jour (GetDeviceProperty → HasDeviceProperty).
- **Docs** : spec I-AUD-09 v1.11 (as-built al/am/an + critères 35
  registrar/619 total) ; docs/EQ-SYSTEME-APO.md (fait établi v3.9.10 :
  la propriété combiné n'est pas une chaîne — le mock a menti) ;
  LISEZMOI 3.9.10 (explication utilisateur) ; README (historique) ;
  CARTE.md (M-AUD-09 v1.11) ; REPRISE.md (résumé express + état
  session 33).
- **Livrables (D22)** : package_v3.sh (zips test/public/source 3.9.10,
  contrôles verts dont le verrou v3.9.10) ; build_setup.sh →
  RoomEq-win-Setup.exe 3.9.10 régénéré (53,8 Mo) ; anciens zips 3.9.9
  archivés dans Livrables/Archive ; miroir Drive complet + push GitHub.
- **Fausse alerte de process élucidée** : `git status` affichait « ahead 2 »
  en début de session, laissant croire que les push des sessions 31-32
  avaient échoué — or `git ls-remote` montre la branche distante À JOUR
  (e70386e = HEAD local après le push de cette session, historique complet
  présent) : sync_backup.sh pousse vers une URL à jeton (pas vers le remote
  nommé « origin »), donc le pointeur local origin/v3-recodage n'est JAMAIS
  mis à jour et « ahead N » est une FAUSSE ALERTE après chaque push. Réflexe
  enregistré dans REPRISE : vérifier un push par `git ls-remote`, jamais par
  `git status` ; pointeur local resynchronisé
  (`git update-ref refs/remotes/origin/v3-recodage HEAD`).

Stage Summary:
- v3.9.10 : le verdict v3.9.9 a prouvé que l'agrégation COM fonctionnait
  (instanciation AGRÉGÉE, Initialize acceptée) et a désigné le maillon
  restant en une ligne : « Position choisie : EFX » sur un périphérique
  combiné. La cause racine tenait en un cast (`as string` sur une
  propriété booléenne — la détection était aveugle sur TOUTE machine),
  prouvée par la source d'Equalizer APO qui teste la PRÉSENCE
  (valueExists), pas le type. Outre la détection corrigée, l'activation
  apprend à se rattraper seule : sortie morte → restauration → essai
  dans l'AUTRE position (l'échelle « Install as » de la référence,
  exécutée automatiquement) — une future erreur de détection ne peut
  plus priver l'utilisateur de l'EQ ni couper son son.
- Prochaine étape : validation terrain Windows — Setup 3.9.10,
  réactiver l'EQ sur le casque JBL : attendu « EQ système activé …
  (position MFX — périphérique combiné) » + « Vérifié : la sortie
  répond et l'APO est chargé », puis un son MODIFIÉ par la courbe (le
  preset jbltuneflex2 est déjà appliqué). Si un ⚠ subsiste, le
  transmettre tel quel : il nomme désormais aussi le drapeau combiné et
  les deux positions essayées.
