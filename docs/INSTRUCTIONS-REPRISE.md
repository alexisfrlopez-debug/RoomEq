# INSTRUCTIONS DE REPRISE — Projet RoomEq (à destination de l'agent IA)

> **À lire en tout premier au début de chaque session.**
> Dernière mise à jour : 2026-10-11 — État : RoomEq **v1.9.1** livré (**les deux curseurs de volume — barre du haut et Mixage — restent synchronisés** quel que soit l'ordre de manipulation), base 1.9.0 (menu des signaux + catalogue téléchargeable + volume audible sur la sortie), 245/245 tests verts.
> Ce fichier est le point d'entrée unique : il explique l'état du projet, comment
> restaurer l'environnement depuis ce Drive, comment travailler, et comment exécuter
> la **sauvegarde incrémentielle obligatoire** en fin de session.

---

## 0. Résumé express (30 secondes)

| | |
|---|---|
| **Projet** | RoomEq — égaliseur acoustique temps réel pour Windows (correction acoustique de pièce) |
| **Stack** | .NET 8 · NAudio (WASAPI/ASIO) · Avalonia 11 (MVVM CommunityToolkit) · xUnit |
| **Version actuelle** | 1.9.1 — **les deux curseurs de volume (barre du haut + Vol. carte physique du Mixage) synchronisés en permanence** (une seule source de vérité : le volume Windows de la carte) ; base 1.9.0 : **menu déroulant des signaux de test** (bruits blanc/rose/brun/violet, sinus 50 Hz→15 kHz, balayages, fichiers téléchargés) + **« Télécharger d'autres… »** (catalogue officiel hébergé sur le dépôt GitHub, SHA-256 vérifié) ; **le curseur Volume de la barre du haut pilote aussi le volume Windows de la carte physique routée (0–100 %)** — audible en usage routé ; base 1.8.1 : défilement horizontal ; base 1.8.0 : deux versions d'exe test télémétré + public silencieux ; base 1.7 : aide « ? », canal micro mono, volume carte physique (Ctrl+Alt+↑/↓/M), audit |
| **Source locale** | `/home/z/my-project/workspace/roomq-source/RoomEq` |
| **SDK .NET** | `/home/z/.dotnet` → `export PATH="$HOME/.dotnet:$PATH"` (8.0.425) |
| **Tests** | 245/245 verts (150 Core + 95 Audio) |
| **GitHub** | https://github.com/alexisfrlopez-debug/RoomEq — dépôt PUBLIC sanitisé (v1.9.1, tag `v1.9.1`) : docs internes et scripts avec placeholders `__ID_*__`, aucune donnée personnelle. Re-sanitiser via `scripts/assemble_github_repo.py (outil interne, non publié)` avant tout nouveau push. **Relève à chaque session** : issues/PR/commentaires (§7bis, `scripts/github_releve.py`). Licence MIT. |
| **Drive** | Racine : https://drive.google.com/drive/folders/__ID_RACINE_DRIVE__ |
| **Règle d'or** | Fin de session = append au `worklog.md` **puis** `python3 scripts/drive_backup.py` |

---

## 1. Démarrage immédiat — checklist dans l'ordre

1. **Lire le journal** `worklog.md` (local : `/home/z/my-project/worklog.md`, sinon
   `data/worklog.md` sur ce Drive). Il contient tout l'historique des travaux, au
   format normalisé (voir §7). Continuer là où il s'arrête.
2. **Si l'environnement local est vide ou absent** (nouvelle sandbox) → faire le
   **bootstrap** (§4) : restaurer les sources depuis ce Drive, récupérer l'outil de
   sauvegarde et les identifiants OAuth.
3. **Vérifier l'outillage** :
   ```bash
   export PATH="$HOME/.dotnet:$PATH"
   dotnet --version        # 8.0.425 attendu ; sinon : (commande d'installation ci-dessous)
   # curl -sSL https://dot.net/v1/dotnet-install.sh | bash -s -- --channel 8.0 --install-dir "$HOME/.dotnet"
   ```
4. **Vérifier la compilation et les tests** :
   ```bash
   cd /home/z/my-project/workspace/roomq-source/RoomEq
   dotnet build RoomEq.sln -c Debug
   dotnet test tests/RoomEq.Core.Tests     # 108 tests
   dotnet test tests/RoomEq.Audio.Tests    # 65 tests
   ```
5. **Relève GitHub (§7bis)** — issues / pull requests / commentaires reçus
   depuis la dernière session : `python3 scripts/github_releve.py` puis
   traiter selon le protocole §7bis.4 (aussi refaire en fin de session).
6. **Clarifier la demande du jour avec l'utilisateur** si elle est ambiguë, sinon
   exécuter la tâche demandée.
7. **En fin de session (obligatoire)** : append au worklog + sauvegarde incrémentielle (§5).

---

## 2. État du projet au 2026-10-11 (v1.9.1)

### 2.0 Fonctionnalités livrées (v1.9.1 — les deux curseurs de volume toujours synchronisés)

- **Retour utilisateur** : « les 2 curseurs de volume ont des problèmes de
  synchro selon dans quel ordre ils sont modifiés ».
- **Diagnostic** (la 1.9.0 avait unidirectionnalisé la synchro) : le curseur
  « Volume » de la barre du haut poussait bien vers le curseur « Vol. carte
  physique » du Mixage, mais le binding TwoWay de ce dernier écrivait
  `RoutedVolume` **sans aucun handler** : le déplacer ne changeait NI la carte
  NI la barre du haut — puis le moindre mouvement de la barre du haut écrasait
  la valeur affichée. Mêmes trous pour Ctrl+Alt+↑/↓, pour le changement de
  sortie master et pour un réglage fait hors de l'application (mixeur
  Windows). D'où une synchro dépendante de l'ordre.
- **Correctif — une seule source de vérité (le volume Windows de la carte),
  tous les affichages relisent la carte après chaque écriture** :
  - `RoomEq.Audio/Routing/VolumeSyncPolicy.cs` (NOUVEAU, pur et testé) :
    sentinelle −1 (« indisponible ») jamais interprétée comme un volume,
    epsilon de changement (0,05 %) vs epsilon d'écriture anti-spam (0,35 %),
    décision de recadrage de la barre du haut, bornage 0..1 ;
  - `MixerViewModel.OnRoutedVolumeChanged` (NOUVEAU) : le curseur du Mixage
    écrit la carte PUIS recale la barre du haut ; garde `_syncingRoutedVolume`
    contre la boucle du binding TwoWay et garde `_routedVolumeReady` contre
    le piège du premier peuplement (RoutedVolume démarre à −1, le Slider le
    contraint à 0 dès l'affichage — ne PAS écrire 0 dans la carte au
    démarrage !) ;
  - `MixerViewModel` : nudges clavier et changement de sortie master recalent
    aussi la barre du haut ; `PollExternalEndpointVolume` dans `Tick()` (80 ms)
    suit les réglages faits hors de l'application ;
  - `MainViewModel.SyncVolumeFromRouted` (NOUVEAU, câblé via le constructeur
    du Mixer — le callback doit vivre dès le chargement de la config au
    démarrage) : recadre `Volume` sous drapeau `_syncingVolumeFromMixer` pour
    que `OnVolumeChanged` ne réécrive pas la carte (elle y est déjà) ;
  - `ApplyTopBarVolume` refactoré sur `VolumeSyncPolicy` (comportement inchangé) ;
  - XAML : `Mode=TwoWay` explicite sur les deux curseurs ; aides « ? » mises à
    jour (synchro documentée dans la barre du haut et la tranche MASTER).
- **Zone d'asymétrie documentée (inchangée)** : au-delà de 100 %, la carte est
  au maximum (le curseur du Mixage reste à 100 %) et seul le gain du lecteur
  interne monte — ce point est écrit dans les deux aides et le LISEZMOI.
- **Tests** : +17 Audio (`VolumeSyncPolicy` : sentinelles, epsilons, bornage,
  décision de recadrage) — 245 total (150 Core + 95 Audio).

### 2.1 Fonctionnalités livrées (v1.9.0 — signaux de test en menu + volume audible sur la sortie)

- **Menu déroulant des signaux de test** (demande : « pour la sélection de
  bruits/types de sinus, je voudrais plutôt un menu déroulant avec possibilité
  d'en télécharger d'autres. Enrichie cette liste ») :
  - `RoomEq.Audio/Playback/TestSignalCatalog.cs` (NOUVEAU) : 16 signaux
    intégrés — bruits blanc/rose/brun/violet (amplitudes calées pour un niveau
    perçu comparable), sinus à 10 fréquences utiles (50 Hz → 15 kHz), 2
    balayages logarithmiques (20 Hz → 20 kHz 12 s ; médium 200 Hz → 2 kHz 8 s) ;
    `DefaultItemId = "bruit-rose"` (réglage de niveau d'écoute) ;
  - `MainViewModel` : `SignalChoices` (intégrés + fichiers téléchargés,
    section « Téléchargés »), `SelectedSignal`, commandes `PlaySelectedSignal`
    (générateurs → `PlayTestSignal` ; fichiers WAV → `AudioFileReader` +
    `LoopingSampleProvider` en boucle via `PlaySource`) et `OpenSignalCatalog`
    (événement `SignalCatalogRequested` → fenêtre) ; anciennes commandes
    TestWhiteNoise/TestPinkNoise/TestSine supprimées (code mort) ;
  - `MainWindow.axaml` : les 3 boutons deviennent ComboBox (240 px) + « Jouer »
    + « Télécharger d'autres… » ; aides « ? » refaites (Volume et Signaux).
- **Catalogue téléchargeable hébergé sur le dépôt GitHub** (dossier `signaux/`,
  publiés avec les sources — URLs stables en `raw.githubusercontent.com`) :
  - `RoomEq.Core/Io/SignalPack.cs` (NOUVEAU) : `DownloadableSignalItem`,
    `SignalManifest.TryParse` (validation COMPLÈTE avant usage : JSON, id slug,
    fileName sûr anti-traversal + .wav, bytes 1..25 Mo, sha256 hex 64,
    doublons interdits, ≤ 50 signaux) ; `SignalUrls` (BaseDir/ManifestUrl/
    `FileUrl`, `IsAllowedFileUrl` : https + domaine exact raw.githubusercontent.com
    + chemin exact `/alexisfrlopez-debug/RoomEq/main/signaux/`, pas de query ni
    de `..`) ;
  - `RoomEq.Core/Io/SignalStore.cs` (NOUVEAU) : bibliothèque locale
    `%APPDATA%\RoomEq\signaux` (dossier injectable pour tests) ; `Save` refuse
    tout nom non sûr, VÉRIFIE le SHA-256 avant écriture, tient un `index.json`
    des noms conviviaux ; `List` résiste à un index corrompu (repli : nom de
    fichier) ; `Contains` (présence + taille) ;
  - `RoomEq.Core/Io/SignalCatalogClient.cs` (NOUVEAU) : téléchargement borné
    (Content-Length contrôlé + coupure au fil de l'eau si dépassement),
    progression, pas de redirection, erreurs en clair (jamais d'exception vers
    l'UI) ;
  - `RoomEq.App/Controls/SignalCatalogDialog.cs` (NOUVEAU) : fenêtre modale
    thème sombre — liste des signaux publiés (cochés par défaut sauf déjà
    téléchargés), taille + description, « Tout cocher/décocher », barre de
    progression par fichier, statuts par élément (Téléchargé / Échec : raison /
    Déjà téléchargé) ; à la fermeture → `RefreshSignalChoices()` ;
  - **pack publié** (`scripts/gen_signal_pack.py`, numpy) : `impulsion-dirac.wav`
    (5 Dirac −3 dBFS espacés 200 ms), `bruit-gris-10s.wav` (spectre blanc ÷
    pondération A = égale sonie, écart mesuré 0,3 dB entre bandes),
    `multi-tones-10s.wav` (15 sinus SMPTE −24 dBFS chacun),
    `balayage-lent-30s.wav` (Farina-ready 20 Hz → 20 kHz) + `manifest.json`
    (id/nom/description/taille/sha256) ; 4,5 Mo au total ; mono 44,1 kHz 16 bits.
- **Curseur Volume audible sur la sortie** (demande : « le volume n'a aucun
  effet sur la sortie sélectionnée ») — diagnostic : le son ROUTÉ (Windows →
  carte virtuelle → mixeur → carte physique) ne passe PAS par `PlaybackEngine`,
  le curseur ne touchait que le lecteur interne :
  - `EndpointVolumeController.SliderToEndpoint` (statique pur, testé) :
    `clamp(v, 0, 1)` — la plage 0–100 % règle la carte physique, au-delà seule
    le gain du lecteur monte (150 % = +3,5 dB) ;
  - `MixerViewModel.ApplyTopBarVolume` : si le volume Windows de la cible
    routée est accessible, le curseur l'applique (seuil anti-spam 0,35 %) et
    rafraîchit l'affichage RoutedVolume (OSD/MASTER restent en phase) ;
  - `MainViewModel.OnVolumeChanged` : `Engine.Volume = value` (inchangé)
    **+** `Mixer.ApplyTopBarVolume(value)` + statut explicite ;
  - aucune écriture forcée au démarrage (seulement lors du déplacement).
- **Tests** : +42 Core (manifeste 12 cas, URLs 9, bibliothèque 10, magasin
  ordonné/index corrompu…), +13 Audio (catalogue : unicités, amplitudes,
  fréquences audibles, balayages, défaut ; conversion curseur) — 228 total.

### 2.3 Fonctionnalités livrées (v1.8.1 — défilement horizontal de l'interface)

- **Diagnostic** (retour utilisateur : « les boutons de test de bruit n'apparaissent
  pas tous », après le défilement vertical du mixer en 1.8.0) : la barre supérieure
  de la fenêtre (`MainWindow.axaml`) empile TOUS les contrôles en une seule ligne
  horizontale (logo + aide + combo sortie 300 px + rafraîchir + transport + nom de
  source 240 px + volume 150 px + bypass + **Bruit blanc / Bruit rose / Sinus 1k**
  + aide ≈ 1 900 px) dans une fenêtre de 1 300 px : les boutons de droite étaient
  coupés, aucun défilement possible.
- **Corrections 1.8.1** (XAML uniquement, aucune logique modifiée) :
  - `MainWindow.axaml` : la StackPanel de la barre supérieure est enveloppée dans
    un `ScrollViewer` (`HorizontalScrollBarVisibility="Auto"`, vertical Disabled) —
    le padding 14 px passe du Border au StackPanel (`Margin="14,0"`) pour que la
    barre de défilement couvre toute la largeur ; tous les boutons restent
    accessibles par défilement (molette + Maj, pavé tactile, barre) ;
  - barre supérieure allégée : combo sortie 300→**260 px**, nom de source
    240→**170 px**, curseur volume 150→**120 px** — sur écran 1920 px tout tient
    SANS défilement ;
  - `AnalyzerTab.axaml` : la barre de contrôle (source, micro, FFT) enveloppée
    pareillement ;
  - `MixerTab.axaml` : première ligne de la barre de routage enveloppée
    pareillement (le WrapPanel du dessous enveloppe déjà naturellement ; les
    tranches gardent leur défilement vertical + horizontal de 1.8.0).
- ⚠ **Piège évité** : NE PAS activer le défilement horizontal sur les ScrollViewer
  latéraux à texte enveloppé (Calibration/Mic/Eq panneaux 320/340 px) — un
  ScrollViewer horizontal contraint le contenu à une largeur infinie et le
  `TextWrapping="Wrap"` ne se déclenche plus (textes sur une ligne + barre
  horizontale). Les barres concernées ne contiennent que des éléments non
  enveloppants (boutons, combos, sliders).
- **Packaging** (`scripts/package_1.8.1.sh`, dérivé de 1.8.0) : mêmes contrôles
  (2 exécutables, telemetrie.json test seul, version 1.8.1.0 dans les DLL) +
  vérification de la présence du défilement horizontal dans les 3 XAML ;
  LISEZMOI.txt réécrit (section « DÉFILEMENT HORIZONTAL PARTOUT »).
- **Tests** : aucun changement de logique → 173/173 inchangés (re-vérifiés).

### 2.4 Fonctionnalités livrées (v1.8.0 — deux versions d'exe)

- **Deux versions d'exécutable, un seul binaire** (demande : « une version test
  qui log et envoie les logs sur le drive, et une version public qui ne log rien
  et n'envoie rien ») :
  - `RoomEq.Core/Io/AppFlavor.cs` (NOUVEAU) : la version est déduite du NOM du
    fichier exécutable (`Environment.ProcessPath`) — tout nom contenant
    « public » = version PUBLIQUE, tout le reste = version TEST (règle d'or :
    une version publique doit toujours s'annoncer explicitement ; l'exe de
    développement `RoomEq.App.exe` se comporte en test). Fonction pure
    `FromExecutableName` testée ; `PublicExePath` (voisin) + `PublicExeAvailable` ;
  - `DiagnosticsLog.Enabled` (interrupteur global, false = ni fichier ni
    mémoire) : réglé dans `App.OnFrameworkInitializationCompleted` AVANT la
    première trace (`Enabled = !AppFlavor.IsPublic`) ;
  - `DriveLogUploader.Config` : version publique → `TelemetryConfig` forcé à
    `Enabled=false` (même si `telemetrie.json` est présent) — les garde-fous
    existants (timer, fermeture, bouton, UI Calibration) suivent automatiquement ;
  - **dialogue de bascule** `RoomEq.App/Controls/FlavorDialog.cs` (NOUVEAU) :
    au lancement de la version test SI `RoomEq-public.exe` est à côté, fenêtre
    modale après `MainWindow.Opened` — « Lancer la version publique » démarre
    l'exe public (`ProcessStartInfo UseShellExecute`) puis `desktop.Shutdown()` ;
    « Continuer en version test » / croix / Échap = poursuite normale ; choix
    journalisé ;
  - **barre de titre** : `RoomEq 1.8.0 — version test|public · Égaliseur…` (MainWindow) ;
  - UI Calibration adaptée : statut « Version publique : aucun journal écrit,
    aucun envoi. », chemin de journal « journalisation désactivée », bouton
    « Envoyer maintenant » masqué (`TelemetryEnabled` false) ;
  - session journalisée avec sa version (`version X — version public — session…`).
- **Onglet Mixage : défilement vertical** (demande : « voir chaque tranche
  entièrement en faisant défiler la page vers le bas ») : le ScrollViewer des
  tranches était `VerticalScrollBarVisibility="Disabled"` (tranches ~520 px >
  viewport) → **Auto** ; horizontal conservé ; l'éditeur EQ de piste avait déjà
  son ScrollViewer vertical.
- **Empaquetage** (`scripts/package_1.8.0.sh`) : publication win-x64 unique →
  `RoomEq.App.exe` copié en `RoomEq-test.exe` + `RoomEq-public.exe` (même
  binaire : l'apphost charge `RoomEq.App.dll` quel que soit le nom de l'exe) puis
  `RoomEq.App.exe` supprimé ; **zip TEST** (dossier `RoomEq-test/`, les deux
  exécutables + `telemetrie.json`) et **zip PUBLIC** (dossier `RoomEq-public/`,
  exe public seul, SANS `telemetrie.json`) + zip source sans identifiants ;
  contrôles automatiques (présences/absences, folder_id, version d'assemblage
  1.8.0.0 dans les DLL, tests finaux).
- **Tests** : +15 nets (108 Core) — `AppFlavorTests` (noms publics/test/chemins,
  DisplayName cohérent, chemin voisin), `DiagnosticsLog_EnabledFalse_WritesNothing`
  (interrupteur : ni mémoire ni fichier) ; 173 total.

### 2.5 Fonctionnalités livrées (v1.7.0)

- **Télémétrie Drive (le cœur de la 1.7)** : l'EXE de test envoie son
  journal de diagnostic vers le dossier Drive `journaux-app`
  (ID `__ID_DOSSIER_JOURNAUX__`, sous la racine) pour que tout
  problème utilisateur soit diagnostiqué à distance :
  - `RoomEq.Core/Io/TelemetryConfig.cs` : lit `telemetrie.json` à côté de l'exe
    (repli `%LOCALAPPDATA%\RoomEq`) — clés **snake_case** (écrit par les scripts
    python) ; fichier absent/incomplet = télémétrie désactivée, app normale ;
  - `RoomEq.Core/Io/DriveLogUploader.cs` : refresh token → access token
    (oauth2.googleapis.com), puis téléversement « créer sinon PATCH » des
    journaux du jour + de la veille, nommés `<machine>-roomeq-AAAA-MM-JJ.log`
    (sanitisation du nom de machine) ; copie mémoire incluse ; jamais d'exception ;
  - déclencheurs : à la fermeture (`MainViewModel.OnAppClosing`, synchrone 10 s
    max — seul moment garanti), timer 5 min, bouton « Envoyer maintenant »
    (Calibration, avec statut `TelemetryText`) ;
  - **consultation** : `python3 scripts/fetch_app_logs.py
    [--machine X] [--day AAAA-MM-JJ] [--tail N] [--download DIR]` ;
  - configuration embarquée : `scripts/telemetrie.json` (client_id/secret/
    refresh/folder_id) — injectée dans le zip portable par
    `scripts/package_1.7.0.sh`, **jamais** dans le zip source ;
  - `DiagnosticsLog` : repli mémoire (600 dernières lignes, `MemoryDump()`)
    quand aucun fichier disque n'est accessible — part quand même en télémétrie.
- **Aide contextuelle « ? »** : `RoomEq.App/Controls/HelpButton.cs`
  (StyledProperty HelpTitle/HelpText, infobulle = texte, clic = dialogue) +
  `HelpDialog.cs` (fenêtre modale centrée, Échap) + style global `Button.help`
  dans App.axaml ; présents sur les 6 onglets + barre du haut.
- **Canal micro mono** : `CaptureEngine.GetChannelCount(deviceId)` (MixFormat
  sans démarrer de capture) ; `RoomMeasurementEngine.CaptureChannelIndex`
  (−1 = mixage L+R, 0/N = canal unique) ; `ChannelMixer.ToMono` (Core/Dsp,
  mutualisé avec l'analyseur — ex-duplication) ; UI : `ChannelOption` +
  ComboBox « Canal du micro » visible seulement si le micro a >1 canaux.
- **Volume de la carte physique routée** : `EndpointVolumeController`
  (Audio/Routing — `IAudioEndpointVolume` via `MMDevice.AudioEndpointVolume`,
  cible « wasapi|… » modifiable à chaud, ASIO non contrôlable) ;
  `MixerViewModel` : RoutedVolume/Text/Mute, commandes Up/Down/ToggleMute
  (±5 %), événement `RoutedVolumeFeedback` → OSD (bandeau 1,6 s dans
  MainWindow) ; **raccourcis clavier** `Ctrl+Alt+↑ / ↓ / M` (KeyBindings
  MainWindow) ; curseur + boutons dans la tranche MASTER.
- **Audit systématique du code** (passe complète, corrections appliquées) :
  - M1 (bug réel) : `TrackEqViewModel.RemoveBand` ne supprimait JAMAIS la
    bande (Remove par instance recréée) → suppression par index ;
  - M2 : `EqChain.Configure` reconstruit aussi au changement de TAUX
    (+ test `EqChainRateTests` à double chaîne) ;
  - M3 : `PlaybackEngine` libère désormais l'ancienne source (AudioFileReader)
    au remplacement/échec/Dispose ; M4 : `AudioClient` COM libéré
    (`using`) dans WasapiOutput + CaptureEngine ;
  - M6 : `ConsoleMixer.RemoveChannel` — disposition DIFFÉRÉE (cimetière +
    1 s) quand le bus tourne ; `StopLocked`/`Dispose` purgent le cimetière ;
  - m1 : fin du lecteur interne détectée (read==0 → PlayerFinished) ;
  - m2/m3 : garde IndexOutOfRange (RevertEdits), handlers MicTab désabonnés
    avant ré-abonnement ; m4 : erreurs de capture de l'analyseur affichées ;
  - m5 : repli mémoire du journal (voir télémétrie) ;
  - m8/I1 : nom de fichier déduit de l'URL RÉELLEMENT téléchargée ;
    User-Agent avec version d'assembly réelle ;
  - code mort supprimé : RouteWindowsToEq, TargetDisplay (cibles affichées
    en FR via `TargetOption`), RequestFilePick/PickFile, VisualChanged ×2,
    OnFftSizeChanged, RefreshMicsCommand (attribut), OutputId/Channels
    (ConsoleMixer), IsCapturing/SourceRate/SourceChannels/DroppedSamples,
    Clear (TapBuffer conservé), AppPaths.SettingsFile, LogSweepSpec.
    SecondsPerOctave + LogSweepGenerator.Progress, CorrectionResult.RawCurve/
    AppliedMicProfile/MeasuredBandDb/TargetBandDb, EqBand.Label, 
    UpdateLocalPoint ; StopReason désormais journalisé ;
  - duplication unifiée : `JsonNamedStore<T>` (Presets + MicProfiles),
    downmix `ChannelMixer`, `EnsureDefaultOutput`→Locked, `SpectrumTools.
    SixthOctave`, BuildSeries réutilisée par CalibrationTab ;
  - performances : `ResponseDb` sans allocation (filtre scratch),
    `RollingBuffer` par segments (plus de modulo par échantillon), bus master
    sans 2e Array.Clear ni WaveFormat allouée par accès, PeakDb calculé une
    fois, VU master early-exit si inchangé, synchro EQ → bus master
    anti-rebond 80 ms ;
  - usings implicites nettoyés sur ~26 fichiers (liste de l'audit).
- **Tests** : +19 nets (93 Core) — `TelemetryTests` (nommage Drive, collecte
  jour+veille, déduplication, repli mémoire, config snake_case), 
  `ChannelMixerTests` (6 cas), `EqChainRateTests` (reconstruction au taux +
  continuité d'état) ; 158 total.

### 2.6 Fonctionnalités livrées (v1.6.1)

- **Fiabilité de la calibration (v1.6.1)** — retour utilisateur « la courbe de
  calibration ne s'affiche pas », cause : la mesure pouvait échouer silencieusement
  (petite erreur grise + graphe vide). Corrections :
  - `RoomMeasurementEngine.CaptureSegmentAsync` transmet désormais la **spec du
    balayage** au lecteur (`PlayTestSignal(..., sweepSpec)`) — auparavant le lecteur
    jouait toujours `LogSweepSpec.Default` (12 s) et toute autre valeur du curseur
    corrompait la déconvolution (balayage non détecté) ;
  - **capture démarrée AVANT la lecture** (pré-roulement 0,7 s capturé → le rognage
    d'attaque de 0,5 s tombe dans le silence, le début du balayage n'est plus rogné)
    + **marge de fin +0,8 s** pour le balayage (les aigus sont toujours joués/capturés) ;
  - `CaptureEngine` : **repli de format** — si 48 kHz mono float est refusé (micro
    verrouillé à 44,1 kHz…), retente avec le format natif du périphérique
    (`dev.AudioClient.MixFormat`), le callback reçoit le taux réel ;
  - **détection de capture muette** (`SpectrumTools.PeakDb` < −66 dBFS → erreur
    actionnable nommant « CABLE Output » et le niveau Windows) ;
  - **un signal en échec ne bloque plus la mesure** : segments ignorés avec
    avertissement visible (`MultiSignalResult.Warnings`) ; échec complet seulement
    si AUCUN signal exploitable ;
  - `DiagnosticsLog` (Core/Io) : journal par jour dans `logs/` à côté de l'exe
    (repli `%LOCALAPPDATA%\RoomEq\logs`), sessions, micro choisi, formats, niveaux
    de chaque segment, analyse du balayage, erreurs avec pile — chemin affiché en
    bas de l'onglet Calibration ; erreurs **en rouge** (`ErrorStatus`) ; exceptions
    globales interceptées (App + Dispatcher) et journalisées ;
  - **courbe live centrée** sur sa moyenne (200 Hz – 8 kHz) — visible quel que soit
    le gain micro ; micros virtuels signalés (`MicDeviceOption`, préselection du
    premier micro physique) ; +8 tests (74 Core, 139 total).

- **5 onglets historiques (v1.0)** : Égaliseur · Analyseur · Calibration · Micro · ASIO
  (DSP temps réel : biquads RBJ, FFT/Welch, bruit rose/blanc, designer de correction,
  courbes cibles, profils micro `.cal`/CSV, mesure de pièce).
- **6e onglet « Mixage & routage » (v1.1)** :
  - Table de mixage style console : une tranche par périphérique d'entrée/sortie Windows ;
  - **EQ indépendant par piste** + EQ principal synchronisé en temps réel sur le bus master
    (événement `EqChain.Changed`) ;
  - Fader (loi puissance constante), pan, mute/solo, VU-mètres stéréo à crêtes ;
  - Définition de la carte virtuelle en périphérique par défaut (COM `IPolicyConfig`,
    repli `mmsys.cpl`) ;
  - Protections anti-larsen : loopback du périphérique master refusé ; master ≠ carte virtuelle ;
  - Persistance JSON : `%APPDATA%\RoomEq\mixer.json`.
- **Installation automatique + multi-protocoles (v1.2)** :
  - Sélecteur « Carte virtuelle » : `VBCableProvider`, `VoiceMeeterProvider`, `VacProvider`,
    `LoopbackProvider` (abstraction `IVirtualAudioProvider`, motifs de détection par protocole) ;
  - **« Installer automatiquement… »** : téléchargement depuis `https://download.vb-audio.com`
    uniquement (validation HTTPS + domaine verrouillé `InstallerCore.ValidateUrl`, taille minimale,
    anti-ZipSlip `ValidateEntryName`, signature PE `ValidateExecutable`, recherche
    d'installateur X64 `FindSetupExecutable`), extraction, lancement élevé UAC, **détection
    d'arrivée des périphériques par sondage** (300 s), progression en direct + journal
    `%APPDATA%\RoomEq\logs` ;
  - Bouton « Page du produit… » (VAC : payant, installation manuelle) ;
  - Guides dynamiques par protocole dans l'UI ; protocole persisté (`VirtualProviderId`) ;
  - Détection/larsen : `AudioDeviceCatalog.IsVirtualRender/IsVirtualCapture` = union des
    motifs de tous les fournisseurs (via `VirtualAudioCatalog`); `ProbeVirtualCard(provider)`.
  - Fichiers clés : `src/RoomEq.Audio/Routing/VirtualAudioProviders.cs`,
    `VirtualAudioInstaller.cs` ; tests `tests/RoomEq.Audio.Tests/VirtualAudioTests.cs`.
- **Chaîne automatique complète (v1.3)** :
  - **Corrections des 2 bugs utilisateur** : (a) Voicemeeter ne se téléchargeait pas —
    les URL `.exe` directes sont mortes (404), l'officiel est un **ZIP versionné**
    (`VoicemeeterSetup_v1130.zip`) : `VoiceMeeterProvider` passé en `Container="zip"` +
    **découverte automatique de version** (`DiscoveryPageUrl`/`DiscoveryPattern`,
    `InstallerCore.ExtractLatestDownloadUrl`) ; (b) l'UAC n'apparaissait jamais — l'archive
    VB-CABLE (1 318 877 octets) était rejetée par le seuil `MinBytes` trop strict (4 Mo)
    AVANT le lancement : corrigé + test de régression ;
  - UAC fiabilisé : détection d'élévation (`WindowsPrincipal`), refus UAC (1223) → message
    explicite, 740 → relance `runas`, hint post-lancement par protocole ;
  - **Bascule automatique des périphériques par défaut** : `DefaultDeviceSetter` — 3 IIDs
    `IPolicyConfig` en cascade, **sortie ET entrée**, confirmation par relecture
    `MMDeviceEnumerator` ; mémorisation des défauts d'origine dans `mixer.json` et
    **restauration automatique à l'arrêt** (`TryRestoreDefaults`) ;
  - Après installation (option « Router dès l'installation ») : piste câble ajoutée,
    chaîne démarrée automatiquement (Windows → carte virtuelle → EQ → sortie physique).
- **Calibration multi-signaux + bibliothèque micros + renommage (v1.4)** :
  - Signaux : blanc, rose, **brun** (intégrateur fuyant, -6 dB/oct), **violet**
    (différenciateur, +6 dB/oct) + 10 tons ISO → remplacés en 1.5 par le balayage ;
  - **Croisement** : retrait de la pente théorique de chaque signal, alignement de niveau,
    **médiane robuste inter-signaux** par bande 1/6 oct, **dispersion** = poids de
    confiance qui dose prudemment les corrections aux fréquences peu fiables ;
  - **AutoBandPlanner** : placement glouton (plus grand |résidu| → peaking, Q déduit de la
    largeur à mi-hauteur), simulation avec la VRAIE réponse des biquads, gains bornés,
    arrêt RMS + anomalie étroite — nombre de bandes / fréquences / Q 100 % automatiques ;
  - **Bibliothèque 36 micros** embarquée (`MicLibrary.json`) : mesure, studio, scène,
    rubans passifs — recherche, import en un clic, copie indépendante ;
  - **Éditeur graphique de courbe micro** (glisser/double-clic/clic droit, champs
    numériques, lissage, profils spécialisés vierges) ; **renommage** des presets EQ
    et des profils micro (`PresetStore.Rename`/`MicProfileStore.Rename`).
- **Balayage progressif + zéro doublon (v1.5)** :
  - **LogSweepGenerator** : ESS logarithmique 20 Hz → 20 kHz, phase en forme fermée
    φ(n) = k·(r^(n/fsT) − 1) (intégrale exacte), fondus 10 ms, flux déterministe
    (la référence d'analyse est régénérée à l'identique — condition de la déconvolution) ;
  - **SweepAnalyzer** : déconvolution de **Farina** (FFT linéaire sans repliement,
    inverse régularisé hors bande), pic IR cherché dans [0 ; 1,2 s] et zone repliée,
    fenêtrage son direct + réverbération courte (harmoniques ESS écartées), niveaux 1/6 oct ;
  - **Anti-doublons** (`AutoBandPlanner`) : zone d'exclusion par demi-largeur de bande
    (plancher `MinSpacingOctaves`), refus de placement conflictuel
    (`ConflictsWithExisting`), passe finale `RemoveDuplicates`, filtres triés par
    fréquence croissante — les anomalies dépassant le boost max sont RÉPARTIES au lieu
    d'être empilées à la même fréquence ;
  - Pièges traités dans les tests : décalage LINÉAIRE (pas circulaire) de la capture
    synthétique, post-roll 1 s (fin du balayage = aigus), comparaison énergétique
    bande à bande, pièce filtrée par de VRAIS biquads causaux.
- **Import de la courbe de calibration + précision au choix (v1.6)** :
  - **Bouton « Réinitialiser la courbe »** (Calibration) : vide le graphe, efface
    résultat et résumé, retire la courbe importable dans l'Égaliseur ; annule une
    mesure en cours ; une mesure annulée/échouée ne laisse plus de courbe « live » figée ;
  - **Panneau « Import calibration »** (Égaliseur) : importe la courbe affichée dans
    Calibration (mesure croisée, profil micro DÉJÀ retiré, alignée sur la cible) et
    régénère toutes les bandes — sans refaire la mesure ;
  - **`CorrectionPrecision`** (Core, `CorrectionPrecision.cs`) : 4 niveaux
    (*Douce* 8 bandes max / *Standard* 14 / *Fine* 20 / *Très fine* 30) qui fixent
    `MaxFilters`, `ThresholdDb`, `ResidualTargetRmsDb`, bornes de Q et espacement
    minimal — les fréquences et gains restent optimisés par `AutoBandPlanner`
    (toujours sans doublon, vérifié par tests avec les options de chaque niveau) ;
  - **`CorrectionDesigner.DesignFromCalibrationCurve(source, precision, name)`** :
    ré-import depuis une `CorrectionResult` (courbe `MeasuredCurve` → planner avec les
    options de précision, options de correction source conservées, micro non réappliqué) ;
  - Câblage : `Calibration.CorrectionReady` → `Eq.SetCalibrationSource(r)` ;
    `Calibration.CurveReset` → `Eq.SetCalibrationSource(null)` (bouton désactivé
    tant qu'il n'y a pas de courbe) ; preset « Import calibration HH:mm », préampli
    de sécurité recalculé ;
  - Nouveau : `CorrectionResult.ResidualRmsDb()` (jumeau de `ResidualMaxDb()`).

### 2.7 Architecture du code

```
RoomEq/
├── RoomEq.sln · Directory.Build.props (version 1.9.1)
├── src/
│   ├── RoomEq.Core/    DSP pur : BiquadFilter, Fft, Windowing, SpectrumTools,
│   │                   NoiseGenerator (blanc/rose/brun/violet), LogSweepGenerator,
│   │                   EqChain (+ Changed), EqBand, EqPreset,
│   │                   Calibration/{CorrectionDesigner (+ DesignFromCalibrationCurve),
│   │                   CorrectionPrecision (4 niveaux), TargetCurve, MicProfile,
│   │                   MicProfileLibrary (36 micros), MultiSignalAnalysis,
│   │                   AutoBandPlanner (anti-doublons), SweepAnalyzer (Farina)},
│   │                   Io/{PresetStore, MicProfileStore (+ Rename),
│   │                   DiagnosticsLog (interrupteur global), AppFlavor (test/public
│   │                   par nom d'exe), TelemetryConfig, DriveLogUploader,
│   │                   SignalPack (manifeste + URLs verrouillées), SignalStore
│   │                   (bibliothèque signaux), SignalCatalogClient (téléchargement borné)}
│   ├── RoomEq.Audio/   NAudio : WASAPI, ASIO (AsioOut), capture micro,
│   │                   RoomMeasurementEngine (multi-signaux segmentés, balayage),
│   │                   Playback/{PlaybackEngine, SampleProviders, TestSignalCatalog
│   │                   (16 signaux intégrés)}, Routing/{RingBuffer (lock-free SPSC),
│   │                   AudioDeviceCatalog, DefaultDeviceSetter (3 IIDs, sortie+entrée),
│   │                   EndpointVolumeController (volume carte physique), ConsoleChannel,
│   │                   ConsoleMixer, MixerStore, VirtualAudioProviders,
│   │                   VirtualAudioInstaller}
│   └── RoomEq.App/     Avalonia 11 : MainWindow (6 onglets), PlotControl,
│                       MicCurveEditor, EqControl (poignées), VuMeter,
│                       ViewModels (Eq : import calibration + précision ;
│                       Calibration : reset + multi-signaux ; Mixer : volume
│                       carte physique + ApplyTopBarVolume ; Mic, Analyzer,
│                       Asio ; Main : SignalChoices + lecture des signaux),
│                       HelpButton/HelpDialog (« ? »), FlavorDialog
│                       (bascule test → public), SignalCatalogDialog
│                       (téléchargement des signaux), thème studio sombre
└── tests/
    ├── RoomEq.Core.Tests/   150 tests xUnit (DSP, précision d'import, balayage,
    │                        croisement, planificateur + ANTI-DOUBLONS, micros, stores,
    │                        AppFlavor, interrupteur du journal, télémétrie, manifeste
    │                        + URLs + bibliothèque des signaux téléchargeables)
    └── RoomEq.Audio.Tests/  78 tests xUnit (RingBuffer, pan law, synchro EQ, MixerStore,
                             protocoles, sécurité installateur, découverte de version,
                             catalogue de signaux, curseur → volume Windows…)
```

### 2.8 Limites connues (à ne pas « corriger » sans discussion)

- `IPolicyConfig` est une API **non documentée** (standard du secteur) : 3 IIDs tentées en
  cascade avec confirmation par relecture ; repli prévu via panneau son Windows.
- Si l'UAC de l'utilisateur est en mode « ne jamais notifier » ou si RoomEq tourne en admin,
  aucune invite n'apparaît pendant l'installation (comportement Windows normal, message
  affiché dans la progression).
- Latence du routage ≈ **70–90 ms** (WASAPI partagé 50 ms + câble virtuel) — acceptable
  pour correction acoustique, pas pour le jeu en temps réel.
- En mode loopback, le périphérique capté doit différer de la sortie master (contrôle intégré).
- Le token OAuth du Drive expire **tous les 7 jours** (voir §6).

---

## 3. Structure du Drive et identifiants techniques

```
📁 Racine — __ID_RACINE_DRIVE__
├── 📄 INSTRUCTIONS-REPRISE.md      ← ce fichier (ID : __ID_FICHIER_INSTRUCTIONS__)
├── 📄 LISEZMOI-SAUVEGARDE.md       (note de sauvegarde v1.0)
├── 📁 data — __ID_DOSSIER_DATA__       [MIROIR incrémentiel]
│   ├── RoomEq/                     (sources complètes, sans bin/obj/publish)
│   ├── worklog.md                  (journal multi-agents, append-only)
│   ├── scripts/drive_backup.py     (outil de sauvegarde — ID : __ID_OUTIL_BACKUP__)
│   ├── scripts/drive_restore.py    (restauration complète : data/RoomEq + worklog + INSTRUCTIONS + livrables)
│   ├── scripts/restore_scripts.py  (restauration ciblée de data/scripts)
│   ├── scripts/…                   (bootstrap_session.sh, package_1.4 → package_1.9.0.sh, fetch_app_logs.py, gen_signal_pack.py…)
│   ├── _manifest_backup.json       (manifeste SHA-256 — ID : __ID_MANIFESTE__)
│   ├── snapshots/                  (instantanés datés --snapshot, 5 max)
│   └── client_secret_*.json · oauth_token_relais.json · projet-gcp-prive-*.json
│       ⚠ identifiants OAuth — NE PAS ré-uploader, NE PAS modifier (exclus du manifeste)
└── 📁 livrables — __ID_DOSSIER_LIVRABLES__  [AJOUT seulement, jamais de suppression]
    ├── README.md
    ├── RoomEq-1.9.0-win-x64-test.zip · RoomEq-1.9.0-win-x64-public.zip · RoomEq-1.9.0-source.zip
    └── Archives/                   (1.0 → 1.8.1, déplacés automatiquement à chaque
                                      nouvelle version — IDs Drive conservés)
```

Téléchargement **public** (sans authentification) d'un fichier par son ID :
`https://drive.usercontent.google.com/download?id=<ID>&export=download`

---

## 4. Bootstrap — reprendre dans un environnement vierge

Exécuter dans l'ordre (copier-coller tel quel) :

```bash
mkdir -p /home/z/my-project/{scripts,secrets,workspace}
cd /home/z/my-project

# 1) Récupérer l'outil de sauvegarde + le manifeste (liens publics, IDs en §3)
curl -sL 'https://drive.usercontent.google.com/download?id=__SCRIPT_ID__&export=download' \
     -o scripts/drive_backup.py
curl -sL 'https://drive.usercontent.google.com/download?id=__MANIFEST_ID__&export=download' \
     -o /tmp/manifest.json

# 2) Restaurer tout l'environnement (sources + worklog + livrables) depuis le Drive
python3 scripts/drive_backup.py --restore /home/z/my-project --manifest-id __MANIFEST_ID__
#   → recrée workspace/roomq-source/RoomEq, worklog.md, download/, data/scripts/

# 3) Récupérer les identifiants OAuth (exclus volontairement de la restauration)
curl -sL 'https://drive.usercontent.google.com/download?id=__ID_FICHIER_TOKEN_OAUTH__&export=download' \
     -o secrets/oauth_token_relais.json

# 3bis) Token GitHub (relève §7bis + push du dépôt public) — dossier Drive « connexions »
curl -sL 'https://drive.usercontent.google.com/download?id=__ID_FICHIER_TOKEN_GIT__&export=download' \
     -o secrets/token_git.txt

# 4) Installer le SDK .NET 8 s'il manque (voir §1.3), puis build + tests (§1.4).
```

Après un `--restore`, relancer une sauvegarde **ne devrait rien envoyer**
(`0 nouveaux, 0 maj`) — c'est le test de bon fonctionnement du bootstrap.

---

## 5. Sauvegarde incrémentielle — protocole systématique

**Obligatoire en fin de session, et après chaque jalon majeur (build vert, nouvelle
version, livraison).**

```bash
python3 /home/z/my-project/scripts/drive_backup.py              # sauvegarde incrémentielle
python3 /home/z/my-project/scripts/drive_backup.py --dry-run    # simulation (rien n'est envoyé)
python3 /home/z/my-project/scripts/drive_backup.py --snapshot   # + instantané daté (5 max conservés)
python3 /home/z/my-project/scripts/drive_backup.py --verify     # re-télécharge tout et vérifie les SHA-256
```

### Fonctionnement
- **Incrémentiel** : chaque fichier est haché (SHA-256) et comparé au manifeste
  `data/_manifest_backup.json` ; seuls les nouveaux/modifiés partent sur le Drive.
- **Miroir** (`data/`) : les fichiers supprimés localement sont mis à la corbeille Drive.
- **Ajout** (`livrables/`) : ajout/mise à jour uniquement — l'historique des versions
  n'est jamais supprimé.
- **Archivage automatique des versions** : à chaque exécution, les zips des versions
  antérieures à la version courante (`Directory.Build.props`) sont DÉPLACÉS dans
  `livrables/Archives/` (Drive) et `download/Archives/` (local), manifeste synchronisé
  (fonction `archive_old_versions`, idempotente, sûre en `--dry-run`).
- **Mise à jour en place** : un fichier déjà présent garde son ID Drive (les liens du §3
  restent valides après chaque sauvegarde).
- **Exclusions** (jamais sauvegardées) : `bin/`, `obj/`, `publish/`, `.git/`, caches
  (`.avalonia-build-tasks`), identifiants/secrets, `access_token.json`.
- **Instantanés** (`--snapshot`) : archive ZIP complète datée dans `data/snapshots/`,
  à créer lors des jalons de version (1.6, 2.0…), pas à chaque session.

### Ce qui est sauvegardé
`workspace/roomq-source/RoomEq` (sources) → `data/RoomEq` · `worklog.md` → `data/` ·
`scripts/` → `data/scripts/` · `download/` (livrables) → `livrables/` ·
`INSTRUCTIONS-REPRISE.md` → racine.

---

## 6. Sécurité — token OAuth et péremption

- Compte : `proprietaire@example.com` — projet Google Cloud `projet-gcp-prive`, scope
  `https://www.googleapis.com/auth/drive` (accès complet).
- **Le refresh token expire tous les 7 jours.** Sauvegardé le 2026-10-07 → valable
  jusqu'au **≈ 2026-10-14** (testé fonctionnel le 2026-10-09).
- Si l'outil échoue avec `invalid_grant` / HTTP 400-401 : **demander à l'utilisateur**
  de régénérer un fichier `oauth_token_relais_*.json` via le même processus d'autorisation
  OAuth qu'à l'origine, puis le déposer dans `data/` (Drive) et le copier en local dans
  `/home/z/my-project/secrets/oauth_token_relais.json`. Le nouveau fichier remplacera
  l'ancien (l'ID Drive du token : `__ID_FICHIER_TOKEN_OAUTH__` peut être mis à jour
  via le Drive, mais **mettre à jour aussi cette section et le §3** si l'ID change).
- ⚠️ Le dossier `data/` est **partagé par lien** : quiconque possède le lien peut lire le
  refresh token. Recommandations à rappeler à l'utilisateur : déplacer les 3 fichiers
  d'identifiants vers un dossier privé, révoquer l'accès sur
  https://myaccount.google.com/permissions en cas de doute.
- **Ne jamais** ré-uploader les identifiants, ne jamais les inclure dans le manifeste,
  ne jamais les copier dans `download/` ni dans les livrables.

---

## 7. Conventions de travail du projet

- **Langue** : échanges et documents utilisateur en **français** ; identifiants de code
  et commentaires en anglais, **interface de l'application en français** (convention
  suivie depuis la v1.4 pour les textes visibles de l'utilisateur).
- **worklog.md** (append-only, jamais de réécriture) — chaque tâche se termine par :
  ```markdown
  ---
  Task ID: <n ou n-a/n-b si parallèle>
  Agent: <nom de l'agent>
  Task: <la demande>

  Work Log:
  - <étapes concrètes>

  Stage Summary:
  - <résultats, décisions, artefacts>
  ```
- **Livrables** : les fichiers finaux vont dans `/home/z/my-project/download/` (la
  sauvegarde les propage vers `livrables/`).
- **Version** : incrémenter dans `Directory.Build.props` à chaque livraison ; bump
  `--snapshot` à ce moment-là ; script d'empaquetage `scripts/package_X.Y.sh` à
  dériver du précédent (version, LISEZMOI).
- **Tests** : tout jalon de code doit finir avec `dotnet test` vert avant livraison.
- **Génération de code** : scripts persistés sous `/home/z/my-project/scripts/`
  (jamais de code long en ligne de commande).
- **Publication GitHub** : le dépôt public `alexisfrlopez-debug/RoomEq` reçoit les
  versions (tag = version de `Directory.Build.props`). Protocole : relancer
  `python3 scripts/assemble_github_repo.py (outil interne, non publié)` (reconstruit `workspace/roomq-github/RoomEq`
  depuis les sources + sanatisation : IDs → `__ID_*__`, email, secrets de
  télémétrie →
  `.example`), vérifier la passe anti-fuite, puis commits + tag + push. JAMAIS
  d'identifiants, d'IDs Drive réels, d'email ou de `telemetrie.json` réel dans le dépôt.
- **Pièges XAML Avalonia** : pas de virgule dans un `StringFormat` (casse le parseur) ;
  `WrapPanel` n'a pas de `Spacing` en Avalonia 11.

---

## 7 bis. Relève et traitement GitHub (dépôt public)

Le dépôt GitHub reçoit du public : **issues**, **pull requests**, commentaires
(et, de façon non actionnable : étoiles, forks, téléchargements de Releases).
Le tout est relevé **à chaque session** et traité selon le protocole ci-dessous.

### 7bis.1 Quand

- **Début de session** (après build/tests verts de l'étape 4, avant de coder) :
  relève complète → `python3 scripts/github_releve.py` (état « dernière relève »
  persisté dans `scripts/github_releve_state.json`, synchronisé par la
  sauvegarde Drive — d'où une continuité entre les sessions).
- **Fin de session** : nouvelle relève rapide pour répondre à ce qui est arrivé
  pendant la session, puis append au worklog.

### 7bis.2 Authentification

- Token GitHub : fichier `token_git.txt` du dossier Drive « connexions »
  (ID `__ID_DOSSIER_CONNEXIONS__`) → copier en local :
  `secrets/token_git.txt` (hors sauvegarde, hors repo, JAMAIS dans un
  message/issue/push). À ajouter au bootstrap §4 dans une nouvelle sandbox.
- API REST v3 en `curl` (pas de `gh` CLI) :
  `curl -s -H "Authorization: Bearer $(cat secrets/token_git.txt)" https://api.github.com/repos/alexisfrlopez-debug/RoomEq/issues?state=open`
- Outil prêt à l'emploi : `scripts/github_releve.py` (`--since` pour forcer une
  date, `--all` pour tout, `--no-save`) — rapport : issues ouvertes, PR
  ouvertes, nouveaux commentaires, issues fermées récemment, étoiles/forks.

### 7bis.3 Ce qui est relevé

| Élément | Source | Action attendue |
|---|---|---|
| Issues ouvertes | `GET /repos/…/issues?state=open` (hors PR) | Classifier puis traiter (7bis.4) |
| Nouveaux commentaires | paramètre `since` de la dernière relève | Répondre |
| Pull requests | `GET /repos/…/pulls?state=open` | Review (7bis.4.5) |
| Issues fermées récemment | `state=closed&since=…` | Vérifier que la réponse est partie |
| Étoiles/forks/Releases | info | Note au worklog si jalon |

### 7bis.4 Protocole de traitement (par élément)

1. **Classifier** : `bug` / `amélioration` / `question` / `spam-hors-sujet`
   (étiquettes créées au besoin via l'API).
2. **Bug** : reproduire (préciser la version), corriger avec test xUnit vert
   (règle §7 « Tests »), commit `fix: … (fixes #N)` → fermeture automatique ;
   livrable + Drive si jalon de version ; PUIS répondre sur l'issue.
3. **Amélioration** : petite et claire → implémenter (même cycle qu'un bug) ;
   sinon l'ajouter au §9 (pistes) avec le lien de l'issue et répondre que
   c'est noté. Périmètre/stratégie ambigu → en discuter avec l'utilisateur
   AVANT de s'engager publiquement.
4. **Question** : répondre en français, courtois, précis ; renvoyer vers le
   README/DISTRIBUTION.md quand la réponse y figure.
5. **Pull request** : remercier systématiquement ; vérifier tests verts +
   aucun secret + style du projet ; fusionner seulement si conforme.
   Sous MIT, une contribution fusionnée est couverte par la licence du projet.
6. **Spam / hors-sujet** : fermer avec une phrase neutre, sans engagement.
7. **Répondre AVANT de fermer** une issue ; jamais de délai ferme annoncé ;
   jamais de secret/ID réel/token dans une réponse publique.
8. **Traçabilité** : append au worklog (Task « relève GitHub ») — items
   relevés, actions, réponses, issues fermées.

### 7bis.5 Hygiène du dépôt

- Le repo public reste la copie SANITISÉE : après TOUT changement des docs
  internes ou scripts, re-passer `scripts/assemble_github_repo.py (outil interne, non publié)` puis
  commit/push — jamais de modification directe dans
  `workspace/roomq-github/RoomEq` sans synchroniser l'arbre source.
- Tag git = version de `Directory.Build.props` ; Releases = zips de
  `download/` (futur).

---

## 8. Historique des versions

| Version | Date | Contenu | Livrables |
|---|---|---|---|
| 1.9.0 | 2026-10-10 | **Menu déroulant des signaux de test** : 16 signaux intégrés (bruits blanc/rose/brun/violet, sinus 50 Hz→15 kHz, 2 balayages log) via `TestSignalCatalog` ; les 3 boutons fixes deviennent ComboBox + « Jouer » + **« Télécharger d'autres… »** — catalogue officiel hébergé dans le dépôt GitHub (`signaux/` : impulsion Dirac, bruit gris d'égale sonie, multi-tones SMPTE, balayage lent 30 s + manifest.json SHA-256) ; chaîne sécurisée : `SignalManifest.TryParse` (anti-traversal, tailles, sha256, doublons) + `SignalUrls` (domaine/chemin verrouillés) + `SignalStore` (%APPDATA%\RoomEq\signaux, index convivial, SHA vérifié avant écriture) + `SignalCatalogClient` (téléchargement borné) + `SignalCatalogDialog` (progression, statuts) ; fichiers joués en boucle. **Le curseur Volume de la barre du haut pilote aussi le volume Windows de la carte physique routée** (`ApplyTopBarVolume` + `SliderToEndpoint` : 0–100 % = carte physique, au-delà = gain du lecteur seul) — corrige « le volume n'a aucun effet sur la sortie sélectionnée » (le son routé ne passe pas par le lecteur interne). +55 tests (228 total). | `RoomEq-1.9.0-win-x64-test.zip` (42 Mo, 2 exécutables + telemetrie.json), `RoomEq-1.9.0-win-x64-public.zip` (42 Mo, sans identifiants), `RoomEq-1.9.0-source.zip` (avec signaux/) |
| 1.8.1 | 2026-10-10 | **Défilement horizontal de l'interface** (retour « les boutons de test de bruit n'apparaissent pas tous ») : barre supérieure de la fenêtre (Bruit blanc / Bruit rose / Sinus 1k), barre de contrôle de l'Analyseur et barre de routage du Mixage défilables sur la largeur ; barre supérieure allégée (combo 260 px, source 170 px, volume 120 px — tout tient sans défilement sur 1920 px). XAML uniquement, logique inchangée. | `RoomEq-1.8.1-win-x64-test.zip` (42 Mo, 2 exécutables + telemetrie.json), `RoomEq-1.8.1-win-x64-public.zip` (42 Mo, sans identifiants), `RoomEq-1.8.1-source.zip` (252 Ko) |
| 1.0.0 | 2026-10-07 | Égaliseur temps réel, analyseur RTA, calibration (courbe cible + designer), profils micro, mesure de pièce, ASIO. 20 tests. | `RoomEq-1.0-win-x64-portable.zip` (42 Mo), `RoomEq-1.0-source.zip` |
| 1.1.0 | 2026-10-09 | Onglet « Mixage & routage » : pistes = tous périphériques Windows, EQ par piste, fader/pan/mute/solo/VU, VB-CABLE + loopback de secours, anti-larsen, persistance mixer.json. +18 tests (38 total). | `RoomEq-1.1-win-x64-portable.zip` (43,4 Mo), `RoomEq-1.1-source.zip` |
| 1.2.0 | 2026-10-09 | Installation automatique du pilote depuis les sites officiels (VB-CABLE, VoiceMeeter) avec validation sécurité + détection d'arrivée ; sélecteur multi-protocoles (VB-CABLE/VoiceMeeter/VAC/boucle) ; guides dynamiques. +29 tests (67 total). | `RoomEq-1.2-win-x64-portable.zip` (43,4 Mo), `RoomEq-1.2-source.zip` |
| 1.3.0 | 2026-10-09 | Corrections URL Voicemeeter (ZIP versionné) et seuil VB-CABLE qui bloquait l'UAC ; chaîne automatique : sortie/entrée par défaut de Windows basculées vers la carte virtuelle, restaurées à l'arrêt ; piste câble + démarrage auto. +18 tests (85 total). | `RoomEq-1.3-win-x64-portable.zip` (43,4 Mo), `RoomEq-1.3-source.zip` |
| 1.4.0 | 2026-10-10 | Calibration multi-signaux (blanc/rose/brun/violet + tons ISO, croisement médian, dispersion) ; AutoBandPlanner (bandes/fréquences/Q automatiques) ; bibliothèque 36 micros + éditeur graphique de courbe + profils spécialisés ; renommage presets/profils. +29 tests (114 total). | `RoomEq-1.4-win-x64-portable.zip` (42 Mo), `RoomEq-1.4-source.zip` |
| 1.5.0 | 2026-10-10 | Test sur sinus = balayage sinusoïdal progressif 20 Hz → 20 kHz (ESS + déconvolution de Farina, tout le spectre en une passe) ; zéro doublon de bandes (zones d'exclusion, refus conflictuels, déduplication finale). +10 tests (124 total). | `RoomEq-1.5-win-x64-portable.zip` (42 Mo), `RoomEq-1.5-source.zip` |
| 1.6.0 | 2026-10-10 | Bouton « Réinitialiser la courbe » (Calibration) ; panneau « Import calibration » (Égaliseur) : régénère les bandes depuis la courbe affichée avec PRÉCISION au choix (Douce 8 / Standard 14 / Fine 20 / Très fine 30 bandes max → espacement + Q) ; `CorrectionPrecision` + `DesignFromCalibrationCurve` + `ResidualRmsDb()` ; courbe live figée effacée après annulation. +7 tests (131 total). | `RoomEq-1.6-win-x64-portable.zip` (42 Mo), `RoomEq-1.6-source.zip` |
| 1.7.0 | 2026-10-10 | **Version de test** : télémétrie Drive (`TelemetryConfig` + `DriveLogUploader` + repli mémoire du journal, envoi fermeture/5 min/bouton, consultation `fetch_app_logs.py`) ; aide contextuelle « ? » sur toutes les sections (`HelpButton`/`HelpDialog`) ; canal micro mono (`ChannelMixer`, sélecteur UI) ; volume de la carte physique routée (`EndpointVolumeController`, Ctrl+Alt+↑/↓/M + OSD + curseur MASTER) ; AUDIT complet : bug EQ de piste réparé (suppression par index), reconstruction des filtres au changement de taux, fuites corrigées (sources, AudioClient, suppression de tranche), code mort supprimé, cibles en français, optimisations temps réel ; usings nettoyés. +19 tests (158 total). | `RoomEq-1.7.0-win-x64-portable.zip` (42 Mo, telemetrie.json embarqué), `RoomEq-1.7.0-source.zip` |
| 1.8.0 | 2026-10-10 | **Deux versions d'exe du même binaire** : `AppFlavor` (version déduite du nom de l'exécutable, règle « public » explicite), `DiagnosticsLog.Enabled` (version publique = ni fichier ni mémoire), télémétrie forcée OFF en public, **dialogue de bascule test → public au lancement** (`FlavorDialog` : lance `RoomEq-public.exe` et referme la version test), version affichée dans la barre de titre ; **onglet Mixage : défilement vertical des tranches** ; packaging en deux zips (test = 2 exécutables + telemetrie.json, public = 1 exécutable sans identifiants). +15 tests (173 total). | `RoomEq-1.8.0-win-x64-test.zip` (42 Mo, 2 exécutables + telemetrie.json), `RoomEq-1.8.0-win-x64-public.zip` (42 Mo, sans identifiants), `RoomEq-1.8.0-source.zip` |
| 1.6.1 | 2026-10-10 | Fiabilité de la calibration (retour « la courbe ne s'affiche pas ») : spec du balayage réellement transmise au lecteur ; capture avant lecture (pré-roulement + marge de fin, balayage jamais rogné) ; repli de format de capture (48k mono → format natif) ; détection de capture muette actionnable ; erreurs en rouge + `DiagnosticsLog` (journal par jour à côté de l'exe) ; un signal en échec n'annule plus la mesure ; courbe live centrée ; micros virtuels signalés + présélection du micro physique. +8 tests (139 total). | `RoomEq-1.6.1-win-x64-portable.zip` (42 Mo), `RoomEq-1.6.1-source.zip` |

---

## 9. Pistes d'évolution (non demandées — à proposer, pas à imposer)

- Réduction de latence : sortie WASAPI exclusive ou mode ASIO pour le bus master.
- Presets de piste partageables / import-export du mixage complet (JSON).
- Télémétrie : enrichir les journaux envoyés (courbes de correction en attaché,
  configurations mixer.json anonymisées) si le diagnostic à distance le nécessite.
- Enregistreur de comparaison A/B (bypass master par raccourci).
- Localisation française de l'interface.
- Extension `InstallerCore.AllowedHosts` si d'autres éditeurs (VAC…) offrent un canal
  de téléchargement stable et vérifiable (hachage publié).
