# Audit de parité v2.2.2 → v3.0.0-dev.1 (vague 7)

> Vérification point par point de la parité fonctionnelle entre la base de
> référence **v2.2.2** (GitHub `alexisfrlopez-debug/RoomEq` @ `deff25c`,
> miroir local `roomeq-git-ref/`) et le recodage **v3** (ce dépôt), contre la
> liste des fonctionnalités du README v2.2.2. Critère de non-régression : D6.
> Méthode : comparaison de code par échantillonnage ciblé + tests + empreintes
> de données embarquées.

## Synthèse

| Volet | Verdict |
|---|---|
| Fonctionnalités applicatives | ✅ parité intégrale (14/14 familles du README) |
| Données embarquées | ✅ après comblement D9 (casques 255 → 321) |
| Jeux de tests | ✅ 437 verts (legacy : 228) — portage intégral + extensions |
| Natifs (pilote, ASIO, installateurs) | 🔒 scellés v2.2.2, consommés tels quels (D1) |
| Écarts volontaires | 3, tous documentés (voir § Écarts) |

## 1. Fonctionnalités du README v2.2.2, point par point

| # | Fonctionnalité (README v2.2.2) | Modules v3 | Vérification | État |
|---|---|---|---|---|
| 1 | Égaliseur paramétrique : bandes peaking/shelf/pass/notch 20 Hz–20 kHz, ±24 dB, Q 0,1–20, courbe composite temps réel, poignées souris + molette Q, préampli, presets JSON **renommables**, import calibration 4 précisions | M-EQ-01..04, M-APP-04 | EqBand/EqChain/EqPreset + EqControl (poignées, molette Q) ; `RenamePreset` dans EqViewModel ; CorrectionPrecision (douce/standard/fine/très fine) | ✅ |
| 2 | Analyseur : RTA 1/6 octave, source Sortie (tap post-EQ) ou Micro, FFT 2048→16384, compensation profil micro | M-DSP-06, M-APP-05 | `fftSizes = {2048, 4096, 8192, 16384}` (défaut 8192, idem v2) ; compensation ligne 195 AnalyzerViewModel | ✅ |
| 3 | Calibration multi-signaux : 5 signaux successifs (blanc, rose, brun, violet, balayage), croisement médian + dispersion, placement auto anti-doublons, « Réinitialiser la courbe », journal diagnostic, **sélecteur de canal mono**, capture avant lecture, format adaptatif, échec isolé | M-CAL-01..08, M-AUD-06, M-APP-02 | MultiSignalAnalysis (médiane + dispersion), AutoBandPlanner (zones d'exclusion), `ResetCurve`/`CurveReset`, `ChannelOption` (« Canal N seul (mono) — gauche/droite »), CaptureEngine inertie + repli format | ✅ |
| 4 | Correction micro : import `.cal` REW/CSV (`;`/`,`, décimales `.`/`,`), bibliothèque 36 micros, éditeur graphique (ajout/déplacement/suppression, lissage), profils spécialisés, renommage | M-CAL-07, M-APP-05 | MicLibrary.json **empreinte identique** à v2.2.2 (36 micros, md5 `f126f6f5…`) ; MicCurveEditor ; MicViewModel (imports, noms uniques — testés) | ✅ |
| 5 | Corrections casques : bibliothèque AutoEq, sélection marque/modèle ou recherche, application un clic (préampli + bandes), source par modèle | M-HPH-01, M-APP-04 | **321 modèles / 58 marques** (comblement D9, empreinte JSON = v2.2.2) ; HeadphonePickerViewModel + recherche instantanée | ✅ (D9) |
| 6 | Preset automatique par sortie : preset de calibration si mesure, sinon correction usine si casque reconnu dans le nom Windows ; choix manuel mémorisé (`devicepresets.json`) | M-EQ-04, M-APP-01 | DevicePresetStore + OutputPresetCoordinator (priorités/mémorisation/replis — testés, interfaces étroites) | ✅ |
| 7 | Périphériques à chaud : surveillance WASAPI, anti-rebond, listes mises à jour | M-AUD-03 | AudioDeviceWatcher (anti-rebond 1 s, porté et testé) | ✅ |
| 8 | ASIO : pilote intégré (piste « DAW (ASIO) »), énumération des autres pilotes, lecture exclusive négociée, infos de capacité, panneau de contrôle | M-AUD-04, M-APP-05 | AsioManager + AsioBridgeHost (protocole verrouillé par 8 tests constants/décalages/tailles) ; AsioViewModel (+ correctif v3 : message sans pilote) | ✅ |
| 9 | Mixage & routage : pistes = toutes entrées/sorties Windows + DAW + internes, EQ par piste, fader/pan/mute/solo, VU crêtes, tranche MASTER (sortie, fader, vol. carte physique), carte virtuelle intégrée ou boucle WASAPI, installation pilote un clic (pnputil), bascule automatique des défauts + restauration, volume synchronisé (barre + MASTER + raccourcis), tranches défilables | M-AUD-03, M-APP-03 | ConsoleMixer/ConsoleChannel/RingBuffer SPSC ; VirtualAudioProviders (RoomEqVirtualProvider + LoopbackProvider, zéro tiers) ; DriverSetupManager (pnputil local) ; DefaultDeviceSetter (PolicyConfig 3 IIDs) ; VolumeSyncPolicy (tests) ; MixerTab ScrollViewer | ✅ |
| 10 | Lecteur intégré : wav/mp3/flac/aiff/wma/m4a, menu signaux (bruits blanc/rose/brun/violet, sinus ISO 10 fréquences, 2 balayages, téléchargés SHA-256), boucle, volume, bypass | M-AUD-02, M-IO-04, M-APP-01 | FilePickerFileType patterns `*.wav;*.mp3;*.flac;*.aiff;*.aif;*.wma;*.m4a` ; DefaultSineSteps ISO 266 (31,5 Hz→16 kHz — **identique v2.2.2**, le README arrondissait) ; SignalCatalogDialog + SignalStore (SHA-256) | ✅ |
| 11 | Aide contextuelle « ? » : infobulle + dialogue par fonctionnalité, 6 onglets + barre du haut | M-APP-01/05 | HelpButton/HelpDialog présents dans toutes les vues | ✅ |
| 12 | Défilement d'interface : barre supérieure, barre Analyseur, barre routage Mixage (horizontal) ; tranches (vertical + horizontal) | M-APP-01..05 | ScrollViewer horizontal barre supérieure (l.28 MainWindow.axaml) ; idem onglets | ✅ |
| 13 | Versions test/public : même binaire, deux noms (`RoomEq.exe` public silencieux / `RoomEq-test.exe` journalisé + télémétrie), version dans le titre, FlavorDialog au lancement test, `ROOMEQ_TEST=1` | M-IO-01, M-APP-01 | AppFlavor (règle de nommage + `ROOMEQ_TEST=1` — testés) ; `Title = $"RoomEq {version} — version {AppFlavor.DisplayName}…"` (identique v2) ; FlavorDialog | ✅ |
| 14 | Télémétrie de test : envoi Drive (fermeture / 5 min / bouton), statut dans l'onglet Calibration, pilotée par `telemetrie.json`, ignorée du public | M-IO-03 | TelemetryConfig + DriveLogUploader (adoptés v1.9-v2.0, sécurisés — spec I-TELE-01) | ✅ |

## 2. Points d'interface et comportements clés

| Point | Vérification v3 | État |
|---|---|---|
| Version dans la barre de titre | code identique au legacy (`Assembly.GetName().Version.ToString(3)` + AppFlavor.DisplayName) | ✅ |
| Raccourcis Ctrl+Alt+↑/↓ (±5 %) / M + OSD volume | MainWindow.axaml.cs (KeyGesture) + OSD dans MainWindow.axaml | ✅ |
| Pistes par défaut au premier lancement (son Windows via carte virtuelle sinon boucle, lecteur, micro, générateur) | MixerViewModel.Routing.cs — **code identique** au legacy (vérifié ligne à ligne) | ✅ |
| Presets rapides EQ de piste (« Coupe-bas 80 Hz / Présence 3 kHz / Aigus adoucis / Aucun EQ ») | TrackEqViewModel — **mêmes bandes exactes** que le legacy ChannelViewModel | ✅ |
| Anti-larsen : refus de démarrer un montage boucle | ConsoleMixer (validation avant ouverture — amélioré v3 : vérifiable sous Linux) | ✅ |
| Fichiers `%APPDATA%\RoomEq` : `presets/`, `micprofiles/`, `mixer.json`, `devicepresets.json`, `signaux/` | Stores.cs + MixerStore + DevicePresetStore (chemins et formats portés ; `settings.json` « réservé » dans les deux versions) | ✅ |
| Journal `logs/roomeq-AAAA-MM-JJ.log` à côté de l'exe, repli `%LOCALAPPDATA%`, jamais bloquant | DiagnosticsLog (testé, + OverrideDir v3) | ✅ |

## 3. Données embarquées (empreintes)

| Donnée | v2.2.2 | v3 après vague 7 | État |
|---|---|---|---|
| `MicLibrary.json` | 36 micros, md5 `f126f6f5…` | **fichier identique** (md5 égal) | ✅ |
| `headphone_presets.json` | 321 modèles / 58 marques | 255 → **321** (D9 : fichier v2.2.2 repris tel quel, md5 `ffff00a5…` égal) | ✅ |
| Signaux de test (catalogue intégré) | bruits ×4, sinus ×10, balayages ×2 | DefaultSineSteps + TestSignalCatalog identiques (testés) | ✅ |
| Assets (icônes) | roomeq.ico + PNG 16→256 | repris tels quels | ✅ |

## 4. Jeux de tests

| Suite | v2.2.2 (legacy) | v3 |
|---|---|---|
| RoomEq.Core.Tests | 150 | **220** (portage + extensions, dont parité casques) |
| RoomEq.Audio.Tests | 78 | **142** (portage + extensions v3) |
| RoomEq.App.Tests | — | **75** (nouveaux : ViewModels décomposés) |
| **Total** | 228 | **437** |

## 5. Écarts volontaires vis-à-vis de la v2.2.2 (tous documentés)

1. **D8 — seuil de détection du balayage 8× → 1000×** : la v2.2.2 rendait des
   bandes absurdes sur une capture de bruit seul ; la v3 affiche le message
   d'aide (4 décades de marge de chaque côté, mesuré en sandbox).
2. **Améliorations v3** (consignées vagues 2-6) : préampli appliqué dans
   `EqChain.Process`, `SanitizeFileName` multi-plateformes, diagnostics
   injectables, `StoredChannel.FilePath` persisté (le fichier de la tranche
   Lecteur survit au redémarrage — la v2 ne le remplissait pas), message ASIO
   sans pilote (bogue v2 : PropertyChanged jamais levé), garde-fou « Lier ce
   preset » étendu au preset plat, kind « mac » persisté dans `mixer.json`.
3. **Structure** : ViewModels décomposés (< 300 lignes/fichier),
   ConsoleWasapiOutput fusionné dans WasapiOutput(latenceMs), interfaces
   étroites pour la politique preset↔sortie — sans changement de comportement
   observable (couverts par tests).

## 6. Ce qui reste volontairement hors parité (D1)

Pilote WDM `roomeqvad.sys`, DLL `RoomEqAsio64.dll`, greffon macOS `RoomEqHAL`,
installateurs (MSI WiX, DriverSetup GUI, AppSetup, MSIX) : **scellés v2.2.2**
et consommés via les mêmes points d'entrée (`natif/REFERENCE-NATIF.md`).
L'application v3 les utilise à l'identique — l'installation du pilote passe
par `RoomEqDriverSetup-2.2.2.exe` (artefact v2.2.2).

## Conclusion

La parité fonctionnelle v2.2.2 est **établie** : les 14 familles de
fonctionnalités du README sont couvertes, les données embarquées sont
identiques en volume (après comblement D9), la suite de tests dépasse la
legacy (437 vs 228) et les seuls écarts sont des correctifs documentés.
L'état est cristallisé aux tags **`v3.0.0`** (ouverture finale D12, sur
ordre du développeur le 2026-10-26), **`v3.0.1`** (correctif tests-only
de la CI Windows, D13) puis **`v3.0.2`** (portabilité CI du packaging,
D14 — binaire identique, specs I inchangées). La validation utilisateur
sur machine Windows réelle se poursuit sur la version publiée : tout
écart constaté sera traité en **patch 3.0.x à contrat stable** (R4).
