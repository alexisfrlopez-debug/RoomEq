# I-APP-01 — Shell applicatif et MVVM

> Module **M-APP-01** · couche I (intention) · projet RoomEq.App
> Version 1 — spec écrite AVANT le code (vague 6).

## Rôle

Le shell Avalonia de RoomEq : démarrage, fenêtre principale (barre
supérieure + onglets + barre d'état), composition des ViewModels et
politique transverse « preset adapté à la sortie ». Il assemble les
moteurs (RoomEq.Core, RoomEq.Audio) et les ViewModels d'onglets sans
contenir de logique métier propre.

## Structure

- `Program.cs` — point d'entrée : `BuildAvaloniaApp()` (détection de
  plateforme, trace) + `StartWithClassicDesktopLifetime`.
- `App.axaml(.cs)` — thème sombre (ressources + styles globaux),
  initialisation : journalisation selon la saveur (version publique :
  AUCUN journal écrit, AUCUN envoi ; version test : journalisation
  complète), exceptions globales capturées (processus + UI, jamais
  silencieuses), création du `MainViewModel` et de la `MainWindow`,
  proposition de bascule version publique si disponible.
- `Views/MainWindow.axaml(.cs)` — barre supérieure (sortie de lecture,
  transport, volume, bypass, signaux de test), `TabControl` à six onglets
  (Égaliseur, Mixage & routage, Analyseur, Calibration pièce, Micro,
  ASIO), barre d'état, OSD volume éphémère, raccourcis
  `Ctrl+Alt+↑/↓/M`, sélecteur de fichiers audio.
- ViewModels : `MainViewModel` (shell), `TestSignalMenu` (menu des
  signaux de test), `OutputPresetCoordinator` (politique preset↔sortie).

## Contrat MainViewModel

- **Moteurs exposés** : `Engine` (PlaybackEngine), `AnalyzerCapture`
  (CaptureEngine), `Measurement` (RoomMeasurementEngine).
- **Sous-ViewModels** : `Eq`, `Analyzer`, `Calibration`, `Mic`, `Asio`,
  `Mixer` — construits par le shell, les initialisations croisées sont
  câblées ici et uniquement ici (calibration → EQ, courbe mesurée
  importable, profil micro appliqué à l'analyseur, liaisons preset↔sortie).
- **Propriétés observables** : `Outputs` + `SelectedOutput` (énumération
  des sorties, sélection → `Engine.SetOutput`), `Volume` (0–1,5),
  `Bypass`, `StatusText`, `SourceName`, `IsPlaying`, `SignalChoices` +
  `SelectedSignal`.
- **Commandes** : `RefreshOutputs`, `PlayPause`, `Stop`,
  `PlaySelectedSignal`, `OpenSignalCatalog`.
- **Événements** : `SignalCatalogRequested` (la vue ouvre la fenêtre).
- **Volume double cible** : le curseur pilote le gain du lecteur interne
  (après EQ) ET le volume Windows de la carte physique routée ; les
  changements venus d'ailleurs (Mixage, raccourcis, mixeur Windows)
  recadrent le curseur sans réécrire la carte (drapeau anti-boucle,
  décisions de `VolumeSyncPolicy`).
- **`OnAppClosing()`** : arrêt du routage, libération des périphériques,
  puis DERNIER envoi des journaux (synchrone, 10 s max) — ne lève jamais.

## Contrat TestSignalMenu

- Menu déroulant des signaux : `TestSignalChoice` = signal généré
  (`GeneratorSignalItem`) OU fichier téléchargé (`FilePath`).
- `RefreshSignalChoices()` reconstruit le menu (intégrés + magasin
  `SignalStore`), conserve la sélection précédente si possible, sinon le
  signal par défaut (bruit rose).
- `PlaySelected` : signal généré → `Engine.PlayTestSignal` ; fichier →
  lecture en boucle (`LoopingSampleProvider { Loop = true }`), erreur
  remontée en texte d'état sans lever.

## Contrat OutputPresetCoordinator

Politique du preset adapté à la sortie master (consommée par le shell,
affichée par l'onglet Mixage) :

1. liaison manuelle mémorisée pour cette sortie (prioritaire) ;
2. preset de calibration mesuré sur cette sortie ;
3. preset usine reconnu depuis le nom du périphérique
   (`HeadphonePresetLibrary.MatchDeviceName`) ;
4. rien — l'égaliseur reste tel quel.

- Le sélecteur « Preset de la sortie » expose les options : `auto`
  (recommandé), `none`, presets utilisateur (`user:<nom>`), suggestion
  usine (`factory:<id>`).
- `Evaluate(device)` applique la politique ; `Pick(option)` mémorise le
  choix ; `LinkFactory(model)` / `LinkNamed(name)` lient un preset à la
  sortie master ; `BindCalibration(preset)` lie le preset fraîchement
  mesuré au périphérique de mesure.
- Un preset lié introuvable (supprimé) : la liaison est retirée et la
  politique repasse en auto, avec message explicite.
- Toutes les actions passent par un délégué « poster sur l'UI »
  injectable (testable sans boucle Avalonia).

## Invariants

1. **Un seul point de composition** : les ViewModels d'onglets ne se
   référencent pas entre eux ; seuls le shell et le coordinateur
   connaissent l'ensemble.
2. **Aucune logique métier dans les vues** : le code-behind se limite à
   l'attachement (événements du ViewModel ↔ contrôles) et aux sélecteurs
   de fichiers.
3. **Le volume a une seule source de vérité** (la carte physique) ; tous
   les affichages suivent, sans boucle (politique `VolumeSyncPolicy`
   du module M-AUD-03).
4. **La fermeture ne lève jamais** et restaure l'état système (défauts
   Windows, arrêt des ponts).
5. La télémétrie n'est active qu'en version test (timer 5 min + envoi
   final) ; jamais en version publique.
6. La version en cours (test/public) est visible dans le titre de la
   fenêtre.

## Tests

- `OutputPresetCoordinator` : priorité des sources (manuel > calibration
  > usine > rien), mémorisation auto/none/user/factory, preset lié
  introuvable → retour auto, liaison calibration au périphérique de mesure.
- `TestSignalMenu` : construction du menu (intégrés + fichiers),
  conservation de la sélection, défaut bruit rose.
- Le shell lui-même (fenêtres, timers) n'est pas testé unitairement —
  dépendances plateforme ; sa logique décisionnelle est déléguée aux deux
  classes ci-dessus, testées.
