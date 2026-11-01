# I-APP-03 — Onglet Mixage (EQ système)

> Module **M-APP-03** · couche I (intention) · projet RoomEq.App
> Version 3 — as-built **v3.9.0** (2 novembre 2026, décision D26).
> Version 2 : v3.8.0 (D25, tranches « entrée/sortie » retirées).
> Version 1 : vague 6 (spécifiée avant le code).

## Rôle

L'onglet Mixage est le **poste de pilotage de l'EQ système** — depuis la
décision D24, LA voie du son Windows : l'égaliseur s'installe dans le moteur
audio (technologie APO), tout ce qui sort par une sortie activée le traverse,
sans pilote ni carte virtuelle. **La console interne a été retirée en v3.9.0
(D26)** : ses sources internes doublaient des fonctions déjà couvertes
(lecteur et signaux de test de la barre du haut, greffons VST/AU pour les
DAW), son bus master doublait la correction sur les sorties couvertes par
l'APO, et sa présence entretenait la confusion avec l'EQ système.

L'onglet contient donc :

- **Panneau « EQ système (sans pilote) »** (spec I-AUD-09) : une ligne par
  sortie Windows active (nom, état ●/○, sortie par défaut), activation et
  désactivation par sortie (une invite administrateur, réversible),
  réparation après mise à jour de pilote, retrait total.
- **Verdict de la dernière action (D26)** : succès comme échec (« élévation
  refusée », « n'expose pas de slot d'effets », « périphérique débranché »…),
  affiché EN ROUGE ou EN VERT dans le panneau, **jamais effacé par le
  rafraîchissement de la liste** — c'est lui qui explique un bouton qui
  « ne s'active pas ». Un second rafraîchissement différé (1,5 s) rattrape
  l'énumération des sorties juste après le redémarrage du service audio.
- **Panneau « Écoute »** : rafraîchissement, réglages son, volume WINDOWS de
  la carte physique d'écoute (−/+/M, OSD, synchronisé avec le curseur de la
  barre du haut et les raccourcis Ctrl+Alt+↑/↓/M), preset adapté à la
  sortie (auto/manuel, calibration > usine reconnue).
- **Pilote hérité** (D25, conditionnel) : détection d'une carte virtuelle
  d'installation v3.6/v3.7 et bouton de suppression.

**Sortie d'écoute (D26)** : la barre du haut est la source de vérité unique
(plus de sélecteur MASTER). `MixerViewModel` suit la sélection via
`FollowTopBarOutput` et en tire le volume physique, le preset adapté et la
mémorisation de session (`mixer.json`) ; au démarrage,
`PreferredInitialOutput` propose la sortie mémorisée à la barre du haut.

**Anti double-correction (D26, côté shell)** : quand l'EQ système couvre la
sortie du lecteur interne (`WindowsApoSystem.CoversEndpoint`), l'EQ LOCAL du
lecteur se contourne — la courbe y est déjà appliquée par le moteur Windows ;
l'APO reçoit toujours la courbe de l'onglet Égaliseur avec le bypass
UTILISATEUR. Pendant une mesure, le moteur force « pièce seule » et
restaure lui-même l'état (`IsMeasuring` protège contre les écritures
concurrentes).

**macOS** : pas d'EQ système — l'onglet affiche l'état inerte ; le routage
par périphérique virtuel (l'ancienne voie macOS) a été retiré avec la
console ; la voie DAW sur macOS est le greffon AU.

## Décomposition (chaque fichier < 300 lignes)

| Fichier | Contenu |
|---|---|
| `MixerViewModel.cs` | État, sortie d'écoute (suit la barre du haut), périphériques, rafraîchissement |
| `MixerViewModel.Volume.cs` | Volume Windows de la carte physique, synchro des curseurs, OSD |
| `MixerViewModel.Driver.cs` | Volet hérité : migration des défauts D25, suppression du pilote, panneau son |
| `MixerViewModel.Persist.cs` | Sauvegarde/restauration `mixer.json` (réduite, tolérante aux fichiers anciens) |
| `MixerViewModel.SystemApo.cs` | Panneau « EQ système » + verdict (I-AUD-09, lot 3) |

(`ChannelViewModel`, `TrackEqViewModel`, `MixerTrackPolicy` et le code-behind
de la vue ont été retirés avec la console ; la couche audio M-AUD-03 —
`ConsoleMixer`, `ConsoleChannel`, etc. — reste intacte, défense en
profondeur, et continue d'être couverte par `ConsoleRoutingTests`.)

## Contrat observable (sélection)

- **EQ système** : `SystemApo`, `ApoEndpoints`, `SystemApoStatusText`,
  `SystemApoVerdictText`, `SystemApoVerdictIsError`, `HasSystemApoVerdict`,
  `IsSystemApoBusy`, `SystemApoSupported`, `AnySystemApoEnabled`, commandes
  `SystemApoEnable/Disable/Repair/Uninstall/Refresh` (détail en I-AUD-09).
- **Sortie d'écoute** : `SelectedOutput`, `FollowTopBarOutput(…)`,
  `PreferredInitialOutput(…)`, événement `MasterOutputChanged`.
- **Preset de la sortie** : `OutputPresetOptions`,
  `SelectedOutputPresetOption`, `OutputPresetText`,
  `SetOutputPresetOptions(…)`, événement `OutputPresetOptionPicked`.
- **Volume physique** : `RoutedVolume` (−1 avant le premier état réel),
  `RoutedMute`, `RoutedVolumeText`, `HasRoutedVolume`,
  `RoutedVolumeTarget`, commandes `RoutedVolumeUp/Down`,
  `ToggleRoutedMute`, méthode `ApplyTopBarVolume`, événement
  `RoutedVolumeFeedback` (OSD).
- **Périphériques** : `RefreshDevicesAsyncCommand`, `IsRefreshingDevices`,
  `RefreshDevices()` (branchement à chaud, post-désinstallation).
- **Pilote hérité** : `ShowLegacyDriverCleanup`, `UninstallDriverCommand`,
  `OpenSoundPanelCommand`, `IsInstalling`, `InstallPhaseText`.

## Comportements

1. **Verdict (D26)** : toute action APO (activer, désactiver, réparer,
   retirer) pose `SystemApoVerdictText` préfixé « ✓ » ou « ✗ » ; le
   rafraîchissement de la liste (fin d'action, branchement à chaud) ne
   l'efface pas — seule l'action suivante le remplace.
2. **Refresh différé** : après chaque action, un second rafraîchissement à
   +1,5 s complète l'énumération des sorties (le service audio vient d'être
   redémarré par le processus élevé).
3. **Sortie d'écoute** : `FollowTopBarOutput` ignore null et les valeurs
   identiques ; chaque changement recible le volume physique, mémorise la
   sortie (`mixer.json`) et notifie `MasterOutputChanged` (réévaluation du
   preset).
4. **Persistance réduite** : `mixer.json` ne mémorise plus que la sortie
   d'écoute, `AutoOutputPreset` et les défauts Windows sauvegardés par
   l'ancien routage (migration D25). Les configurations des versions
   antérieures (tranches, protocoles, faders) se chargent sans exception et
   sont ignorées.
5. **Migration pilote hérité** (Windows, une fois par session) : si l'entrée
   de la carte virtuelle est encore la sortie par défaut de Windows (état
   v3.6/v3.7), la sortie d'origine mémorisée est restaurée (sinon message
   actionnable) ; le bouton « Supprimer le pilote » reste proposé tant
   qu'une carte héritée est détectée.
6. **Sondage du volume externe** : minuterie 80 ms — les réglages faits hors
   de l'application (mixeur Windows, touches du casque) suivent seuls.

## Invariants

1. La persistance ne lève jamais (l'audio prime).
2. Les événements des moteurs arrivent de threads audio : tout passage à
   l'UI passe par le dispatcher.
3. `Shutdown()` libère le contrôleur de volume — sans lever.
4. La migration du pilote hérité ne s'exécute qu'une fois et ne doit
   jamais interrompre le démarrage.
5. Le verdict d'échec n'est JAMAIS effacé par un rafraîchissement de la
   liste (l'utilisateur doit toujours voir POURQUOI une activation a échoué).

## Tests

- `MixerSystemTabTests` (D26) : suivi de la sortie d'écoute (événement,
  idempotence), restauration de la sortie mémorisée (une fois), persistance
  réduite sans tranche résiduelle, chargement d'une configuration héritée
  sans exception, verdict qui survit au rafraîchissement, inertie de
  `WindowsApoSystem` hors Windows (`CoversEndpoint` toujours faux), garde
  d'instance unique (acquisition/refus/libération).
