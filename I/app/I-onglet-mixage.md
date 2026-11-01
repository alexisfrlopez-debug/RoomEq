# I-APP-03 — Onglet Mixage (console interne + EQ système)

> Module **M-APP-03** · couche I (intention) · projet RoomEq.App
> Version 2 — as-built **v3.8.0** (2 novembre 2026, décision D25).
> Version 1 : vague 6 (spécifiée avant le code).

## Rôle

L'onglet Mixage réunit les deux voies de l'égaliseur :

- **EQ système (Windows)** — panneau « EQ système (sans pilote) » (spec
  I-AUD-09, D24) : la voie principale, placée EN TÊTE de l'onglet. Tout le
  son d'une sortie activée traverse l'égaliseur DANS le moteur audio
  Windows, sans pilote ni carte virtuelle.
- **Console interne** — des tranches pour les sources INTERNES de RoomEq
  uniquement (lecteur de fichiers, générateur de test, pont DAW ASIO,
  périphérique virtuel macOS), chacune avec fader, pan, mute, solo, VU et
  égaliseur propre ; un bus master synchronisé sur l'EQ principal ;
  démarrage/arrêt par un bouton ; le volume Windows de la carte physique ;
  le preset automatique par sortie.

**Retiré en v3.8.0 (D25)** : les tranches liées à des périphériques
Windows — « entrée » (captures : micros, lignes, sortie du câble virtuel)
et « sortie » (boucles WASAPI) — ainsi que le routage par carte virtuelle
qui les alimentait (bascule des périphériques par défaut, sélecteur de
protocole, installation du pilote, piste « câble » garantie). L'EQ système
APO traite le son du système sans eux ; ces tranches doublaient la
correction, déclenchaient les protections anti-boucle et désorientaient.
**macOS** (pas d'EQ système) : le routage par périphérique virtuel
« RoomEq Virtual » reste la voie principale, inchangé.

## Décomposition (chaque fichier < 300 lignes)

| Fichier | Contenu |
|---|---|
| `MixerViewModel.cs` | État, périphériques, sorties, sources internes, tranches, minuterie VU, drapeau APO-sur-master |
| `MixerViewModel.Routing.cs` | Démarrage/arrêt de la console, libellés/aide par OS, démarrage auto, tranches par défaut |
| `MixerViewModel.Volume.cs` | Volume Windows de la carte physique, synchro des curseurs, OSD |
| `MixerViewModel.Driver.cs` | Pilote hérité : suppression de maintenance + panneau son |
| `MixerViewModel.Persist.cs` | Sauvegarde/restauration `mixer.json` (purge des tranches périphériques) |
| `MixerViewModel.SystemApo.cs` | Panneau « EQ système » (I-AUD-09, lot 3) |
| `MixerTrackPolicy.cs` | Politique PURE des sources autorisées (D25) |
| `ChannelViewModel.cs` | Une tranche de console |
| `TrackEqViewModel.cs` | Éditeur d'EQ de la piste sélectionnée |

## Contrat observable (sélection)

- **Console/routage** : `IsRunning`, `RoutingStatusText`, `RoutingStartLabel`,
  `RoutingStopLabel`, `RoutingHelpTitle`, `RoutingHelpText`,
  `ToggleRoutingCommand`, `AutoStart`, `AutoOutputPreset`.
- **EQ système** : `SystemApo`, `ApoEndpoints`, `SystemApoStatusText`,
  `IsSystemApoBusy`, `SystemApoSupported`, `AnySystemApoEnabled`,
  commandes `SystemApoEnable/Disable/Repair/Uninstall/Refresh`
  (détail en I-AUD-09).
- **Anti double-correction (D25)** : `ApoOnMaster` (l'EQ système couvre la
  sortie master), `MasterEqSourceText` — quand `ApoOnMaster` est vrai, le
  bus master contourne son propre EQ (la courbe est déjà appliquée par le
  moteur Windows) ; recalculé au changement de sortie et à chaque état APO.
- **Sortie master** : `Outputs`, `SelectedOutput`, `OutputInfoText`,
  événement `MasterOutputChanged`.
- **Preset de la sortie** : `OutputPresetOptions`,
  `SelectedOutputPresetOption`, `OutputPresetText`,
  `SetOutputPresetOptions(…)`, événement `OutputPresetOptionPicked`.
- **Tranches** : `Channels`, `AvailableSources` (internes uniquement),
  `SelectedChannel` → `TrackEq.Attach`, commandes `AddTrack` (refuse les
  sources périphériques), `RemoveTrack`, `SelectTrack`.
- **Master** : `MasterFader`, `MasterFaderText`, `MasterMute`,
  `MasterDispL/R`, `MasterHoldL/R`.
- **Volume physique** : `RoutedVolume` (−1 avant le premier état réel),
  `RoutedMute`, `RoutedVolumeText`, `HasRoutedVolume`,
  `RoutedVolumeTarget`, commandes `RoutedVolumeUp/Down`,
  `ToggleRoutedMute`, méthode `ApplyTopBarVolume`, événement
  `RoutedVolumeFeedback` (OSD).
- **Pilote hérité** : `ShowLegacyDriverCleanup` (détection d'une carte
  virtuelle d'installation antérieure), `UninstallDriverCommand`,
  `OpenSoundPanelCommand`.
- `AsioStatusText` : état du pont (DAW Windows / périphérique macOS).

## Comportements

1. **Console** : démarrage = synchro de l'EQ master puis `Mixer.Start()`
   (captures internes + bus master vers la sortie choisie) — plus AUCUNE
   bascule de périphérique par défaut (D25) ; arrêt = `Mixer.Stop()`.
   macOS : le routage par périphérique virtuel suit le même bouton, avec
   ses libellés propres.
2. **Sources internes seulement** (D25) : `AvailableSources` = DAW (ASIO)
   + Lecteur + Générateur (+ périphérique virtuel macOS si le greffon est
   installé). `AddTrack` REFUSE une source capture/loopback (message
   actionnable — défense en profondeur, `MixerTrackPolicy`).
3. **Tranches par défaut** (première session) : lecteur interne +
   générateur (+ périphérique virtuel macOS si installé). Plus de piste
   câble, de boucle ni de micro.
4. **Persistance purgée** : au chargement de `mixer.json`, les tranches
   `capture`/`loopback` sont ignorées silencieusement ; les champs
   obsolètes (protocole, bascules de défauts) restent lus pour tolérance
   puis cessent d'être écrits. Les identifiants de défauts sauvegardés
   alimentent la migration.
5. **Migration pilote hérité** (Windows, une fois par session) : si
   l'entrée de la carte virtuelle est encore la sortie par défaut de
   Windows (état laissé par v3.6/v3.7), la sortie d'origine mémorisée est
   restaurée (sinon message actionnable) ; le bouton « Supprimer le
   pilote » reste proposé tant qu'une carte héritée est détectée.
6. **VU** : minuterie 80 ms — crêtes avec décroissance douce, marqueurs
   tenus, sondage du volume externe, état du pont, détection des captures
   interrompues.
7. **Synchro EQ principal → bus master** : repoussée de 80 ms
   (anti-rebond) pendant les glissements ; bypass master = bypass
   global OU APO actif sur la sortie master (anti double-correction).
8. **Volume carte physique** : 0–100 % règle la carte ; écriture
   programmatique vs utilisateur discriminées ; le premier peuplement du
   binding (−1) n'écrit JAMAIS dans la carte.
9. **Éditeur d'EQ de piste** : bande éditable (ajout/suppression par
   INDEX, aplatir, aucun EQ, presets rapides), courbe recalculée et
   poussée dans la `EqChain` de la piste sans interruption audio.

## Invariants

1. La persistance ne lève jamais (l'audio prime).
2. `MixerTrackPolicy` : aucune tranche liée à un périphérique Windows ne
   peut être créée (vue, persistance, appel direct).
3. Les événements des moteurs arrivent de threads audio : tout passage à
   l'UI passe par le dispatcher.
4. `Shutdown()` arrête les ponts (ASIO + périphérique) et libère le
   contrôleur de volume — sans lever.
5. Suppression de bande par INDEX (l'égalité d'objet ne fonctionne pas,
   les vues créent des instances neuves).
6. La migration du pilote hérité ne s'exécute qu'une fois et ne doit
   jamais interrompre le démarrage.

## Tests

- `MixerTrackPolicyTests` (D25) : classification exhaustive des six genres
  de source (autorisée vs héritée, XOR complet), kinds persistés
  (`capture`/`loopback` détectés, autres non), badge « INTERNE » des
  sources internes.
- `ChannelViewModel` : fader/pan/mute/solo poussés dans le canal,
   libellés dB, générateur (signal → `SetGeneratorSignal`), VU
   décroissant, LED EQ.
- `TrackEqViewModel` : ajout/suppression par index, aplatir, aucun EQ,
  presets rapides, courbe recalculée, poussée dans `EqChain` de la piste.
- `MixerViewModel` (parties testables sans périphérique) : persistance
  aller-retour via `StoredMixer`, sélection de sortie, synthèse EQ master
  depuis l'EQ principal.
