# I-APP-02 — Onglet Calibration pièce

> Module **M-APP-02** · couche I (intention) · projet RoomEq.App
> Version 1 — spec écrite AVANT le code (vague 6).

## Rôle

L'assistant de calibration acoustique : choisir le micro et son canal,
régler le niveau, sélectionner les signaux de mesure, conduire la mesure
(mono ou multi-signaux), afficher les courbes en direct et le résultat,
et transmettre la correction à l'égaliseur.

## Décomposition (chaque fichier < 300 lignes)

| Fichier | Contenu |
|---|---|
| `CalibrationViewModel.cs` | Conduite de la mesure, options, résultat, télémétrie |
| `CalibrationOptions.cs` | Classes d'options : `SignalOption`, `MicDeviceOption`, `ChannelOption`, `TargetOption` |
| `LiveMicMonitor.cs` | Tampon circulaire live + spectre Welch/mini-octaves centré |

## Contrat observable

- **Mesure** : `Duration`, `SweepSeconds`, `MultiSignal`, `TestLevel`,
  `MaxBoost`, `MaxCut`, `UseMicProfile`, `SignalOptions` (cochables),
  `PlanSummary` (estimation du plan recalculée), `MicDevices` +
  `SelectedMicDevice` (cartes virtuelles signalées, micro physique
  préféré), `CaptureChannels` + `SelectedCaptureChannel` (mixage stéréo
  ou canal unique), `HasChannelChoice`, `TargetOptions` +
  `SelectedTarget` (libellés français), `IsMeasuring`, `Progress`,
  `Phase`, `ResultSummary`, `ErrorStatus`, `HasResult`.
- **Commandes** : `Start`, `Cancel`, `ResetCurve`, `SavePreset`,
  `RefreshMics`, `SendLogsNow`.
- **Événements** : `SeriesChanged` (courbes live/résultat),
  `CorrectionReady` (résultat complet), `CorrectionToEq` (preset →
  égaliseur), `CorrectionSavePreset`, `CurveReset`.
- `Result` (dernier résultat), `BuildSeries(result)` (séries du graphe).
- **Télémétrie** : `TelemetryEnabled`, `TelemetryText`,
  `LogPathDisplay` (désactivée en version publique).

## Comportements

1. **Mesure** mono (`MeasureAsync`) ou multi-signaux
   (`MeasureMultiSignalAsync`) : options de correction (cible, boost/
   coupe max, 40 Hz–16 kHz), profil micro si actif et valide, nom de
   preset horodaté ; progression, phases et flux live remontés du moteur
   vers l'UI par le dispatcher ; annulation propre
   (`OperationCanceledException` → « Mesure annulée », graphe live vidé).
2. **Suivi live** : spectre Welch 4096 en sixièmes d'octave, courbe
   CENTRÉE sur sa moyenne (le niveau absolu dépend du gain micro — seule
   la forme compte en direct).
3. **Micros** : rafraîchissement conservant la sélection, préférant un
   micro PHYSIQUE (une carte virtuelle par défaut conduit à une capture
   muette) ; canaux reconstruits par micro (premier canal proposé pour
   les multi-canaux).
4. **Séries du résultat** : courbes individuelles alignées (multi-
   signaux, traits grisés), mesure croisée (cyan, remplie), cible
   (orange, tirets), prédiction corrigée (vert).
5. **Réinitialisation** : annule la mesure en cours, vide graphe et
   résultat, notifie `CurveReset` (l'import EQ devient indisponible).
6. À la fin : `CorrectionReady` + `CorrectionToEq` — la correction est
   appliquée à l'égaliseur automatiquement.
7. **Résumé** : nombre de signaux croisés, filtres, préampli, résidu
   RMS/max, avertissements éventuels.

## Invariants

1. La mesure est exclusive (`IsMeasuring` bloque tout redémarrage).
2. Le `CancellationTokenSource` est toujours libéré dans le `finally`.
3. Une erreur de mesure ne laisse pas l'onglet incohérent : phase
   explicite, `ErrorStatus` renseigné, dernier résultat valide restauré
   dans le graphe (sinon vidé).
4. L'analyse live ne bloque jamais le callback de capture (tampon
   circulaire, calcul côté minuterie).

## Tests

- Options : libellés cibles français, `MicDeviceOption` signale les
  cartes virtuelles, `ChannelOption` (mixage vs canaux).
- `PlanSummary` recalculé quand durée/balayage/sélection changent.
- Séries du résultat : quatre séries attendues (croisée, cible,
  prédiction ; + individuelles en multi-signaux).
- Le moniteur live : tampon circulaire thread-safe (écriture/lecture
  concurrentes), courbe centrée sur sa moyenne.
