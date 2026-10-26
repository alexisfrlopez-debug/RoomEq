# I-APP-05 — Onglets Micro, Analyseur et ASIO

> Module **M-APP-05** · couche I (intention) · projet RoomEq.App
> Version 1 — spec écrite AVANT le code (vague 6).

## Rôle

Trois onglets secondaires : gestion des profils de correction micro
(import, bibliothèque, édition graphique), analyseur de fréquences temps
réel (sortie post-EQ ou micro, compensation de profil), gestion des
pilotes ASIO (énumération, capacités, panneau de contrôle).

## Décomposition (chaque fichier < 300 lignes)

| Fichier | Contenu |
|---|---|
| `MicViewModel.cs` | Profils : liste, import, bibliothèque, création, renommage, suppression |
| `MicViewModel.Curve.cs` | Édition de courbe : points, lissage, aplatir, annuler, enregistrer |
| `AnalyzerViewModel.cs` | Analyseur temps réel |
| `AsioViewModel.cs` | Gestion des pilotes ASIO |
| `RollingBuffer.cs` | Tampon circulaire thread-safe (partagé analyseur/calibration) |

## Contrat MicViewModel

- **Propriétés** : `Profiles` + `SelectedProfile`, `ImportInfo`,
  `LibraryProfiles` + `SelectedLibraryProfile`, `LibrarySearch`,
  `NewProfileName`, `IsDirty`, `EditStatus`, `SelectedPointIndex`,
  `SelectedPointFreq/Gain`, `HasSelection`, `HasSelectedPoint`.
- **Commandes** : `ImportLibraryProfile`, `CreateNewProfile`,
  `AddPoint`, `RemovePoint`, `ApplySelectedPoint`, `SmoothCurve`,
  `FlattenCurve`, `SaveEdits`, `RevertEdits`, `RenameProfile`,
  `DeleteSelected`.
- **Événements** : `SelectionChanged`, `ProfileListChanged`,
  `CurveEdited` (la vue resynchronise l'éditeur graphique).
- **Méthodes appelées par la vue** : `ImportFromFile(path)` (sélecteur),
  `SelectedCurve()`, `AddPointAt(freq, gain)`, `RemovePointAt(index)`,
  `MovePoint(index, freq, gain)`, `SelectPoint(index)`.
- Comportements : import .cal/CSV/TXT (profil invalide → message),
  bibliothèque filtrée par recherche, création d'un profil plat (10 points
  géométriquement répartis 20 Hz→20 kHz), noms uniques (suffixes
  « (2) », « (3) »…), renommage via magasin (refus si cible existante),
  édition : ajout à position triée, suppression (min 5 points), déplacement
  borné par les voisins, lissage 0,25/0,5/0,25, champs numériques
  synchronisés, annulation = dernière version enregistrée.

## Contrat AnalyzerViewModel

- **Propriétés** : `Mode` (`Sortie`/`Micro`), `Modes`, `MicDevices` +
  `SelectedMic`, `MicRunning`, `ApplyMicCompensation`, `FftSize` +
  `FftSizes` (2048–16384), `Info`, `PeakDb`, `MicModeActive`.
- **Commandes** : `ToggleMic` ; `MicGainAt` (fonction gain(f), fixée par
  le shell depuis le profil micro sélectionné).
- Comportements : source Sortie = flux tap post-EQ du lecteur ; source
  Micro = capture continue (redémarrée au changement de périphérique,
  arrêtée en quittant le mode) ; spectre Welch à la taille FFT, bandes
  1/6 d'octave, compensation de profil micro soustraite ; pic (dB +
  fréquence) affiché ; source inactive → aucun calcul (spectre en l'état,
  message d'attente) ; erreur de capture → arrêt + message, jamais de
  crash depuis la minuterie.

## Contrat AsioViewModel

- **Propriétés** : `Drivers` + `SelectedDriver`, `Info`,
  `SupportedRates`, `NoDriver`.
- **Commandes** : `Refresh`, `OpenControlPanel`.
- Comportements : capacités du pilote (canaux in/out, frames par buffer,
  latence, taux supportés) ; la sélection STOPT la lecture (libère le
  pilote) ; panneau de contrôle du pilote ; messages explicites sans
  pilote (« installez ASIO4ALL… »).

## Contrat RollingBuffer

Tampon circulaire thread-safe (verrou) : `Add(span)` en deux segments
(sans modulo par échantillon — appelé depuis le callback de capture),
`Snapshot(maxSamples)` (N derniers échantillons, ou moins),
`Capacity`, `SampleRate`, `Clear()`.

## Invariants

1. Les profils modifiés ne sont écrits sur disque QUE par `SaveEdits`
   (l'édition graphique reste en mémoire, `IsDirty` la signale).
2. Un profil conserve au moins 5 points (validité).
3. Les fréquences d'un profil restent triées (déplacement borné par les
   voisins).
4. L'analyseur ne lève jamais depuis sa minuterie ; la capture micro est
   toujours arrêtable.
5. Les listes de périphériques se rafraîchissent sans perdre la sélection
   courante quand elle existe encore.

## Tests

- `RollingBuffer` : capacity, snapshot partiel/complet, écriture en deux
  segments (boucle), Clear, accès concurrent simple.
- Mic : création (10 points plats), ajout/suppression/déplacement (bornes
  voisins, min 5), lissage, aplatir, annulation, noms uniques, renommage,
  import bibliothèque, suppression — via magasin en répertoire de test.
- Analyseur : construction des séries (bandes, compensation de profil
  appliquée), pic détecté, source inactive → liste vide + info d'attente.
- ASIO : sans pilote (Linux) → `NoDriver` vrai, message d'aide.
