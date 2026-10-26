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
