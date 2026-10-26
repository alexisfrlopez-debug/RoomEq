# I-APP-03 — Onglet Mixage & routage

> Module **M-APP-03** · couche I (intention) · projet RoomEq.App
> Version 1 — spec écrite AVANT le code (vague 6).

## Rôle

La table de mixage complète : une tranche par source (entrées/sorties
Windows, boucles, lecteur interne, générateur, piste DAW ASIO, périphérique
virtuel macOS), chacune avec fader, pan, mute, solo, VU et égaliseur
propre ; un bus master synchronisé sur l'EQ principal ; le routage du son
Windows via la carte virtuelle RoomEq ou la boucle WASAPI ; l'installation
du pilote ; le volume Windows de la carte physique.

## Décomposition (chaque fichier < 300 lignes)

| Fichier | Contenu |
|---|---|
| `MixerViewModel.cs` | État, périphériques, sorties, sources, tranches, minuterie VU |
| `MixerViewModel.Routing.cs` | Chaîne de routage, bascule/restauration des défauts Windows, démarrage auto |
| `MixerViewModel.Volume.cs` | Volume Windows de la carte physique, synchro des curseurs, OSD |
| `MixerViewModel.Driver.cs` | Carte virtuelle : sondage, installation/désinstallation, panneau son |
| `MixerViewModel.Persist.cs` | Sauvegarde/restauration `mixer.json` |
| `ChannelViewModel.cs` | Une tranche de console |
| `TrackEqViewModel.cs` | Éditeur d'EQ de la piste sélectionnée |

## Contrat observable (sélection)

- **Routage** : `IsRunning`, `RoutingStatusText`, `ToggleRoutingCommand`,
  `AutoStart`, `AutoRouteOnInstall`, `SwitchDefaultCapture`, `AutoOutputPreset`.
- **Carte virtuelle** : `VirtualProviders` + `SelectedProvider`
  (interface `IVirtualAudioProvider` du module M-AUD-03), `VirtualCard`
  (sondage), `VirtualStatusText`, `IsInstalling`, `InstallPhaseText`,
  `InstallPercent`, `InstallIndeterminate`, commandes
  `InstallVirtualCard`, `UninstallDriver`, `SetVirtualDefault`,
  `OpenSoundPanel`, `RefreshDevices`.
- **Sortie master** : `Outputs`, `SelectedOutput`, `OutputInfoText`,
  événement `MasterOutputChanged`.
- **Preset de la sortie** : `OutputPresetOptions`,
  `SelectedOutputPresetOption`, `OutputPresetText`,
  `SetOutputPresetOptions(…)` (remplissage programmatique sans déclencher
  l'événement utilisateur), événement `OutputPresetOptionPicked`.
- **Tranches** : `Channels`, `AvailableSources`, `SelectedChannel`,
  `TrackEq`, `SelectedChannel` → `TrackEq.Attach`, commandes `AddTrack`,
  `RemoveTrack`, `SelectTrack`.
- **Master** : `MasterFader`, `MasterFaderText`, `MasterMute`,
  `MasterDispL/R`, `MasterHoldL/R`.
- **Volume physique** : `RoutedVolume` (−1 avant le premier état réel),
  `RoutedMute`, `RoutedVolumeText`, `HasRoutedVolume`,
  `RoutedVolumeTarget`, commandes `RoutedVolumeUp/Down`,
  `ToggleRoutedMute`, méthode `ApplyTopBarVolume`, événement
  `RoutedVolumeFeedback` (OSD).
- `AsioStatusText` : état du pont (DAW Windows / périphérique macOS).

## Comportements

1. **Routage** : démarrage = synchro de l'EQ master puis bascule des
   défauts Windows vers la carte virtuelle (mémorisés une fois, aussi sur
   disque — survit à un plantage) puis `Mixer.Start()` ; arrêt =
   `Mixer.Stop()` + restauration des défauts. macOS : aucune API publique
   de bascule — l'utilisateur choisit la sortie dans Réglages Système.
2. **Piste câble garantie** : après installation/démarrage, la piste qui
   capte la sortie de la carte virtuelle existe toujours.
3. **Tranches par défaut** (première session) : son Windows (câble virtuel
   ou boucle de la sortie par défaut), lecteur interne, premier micro
   physique non virtuel, générateur.
4. **VU** : minuterie 80 ms — crêtes avec décroissance douce, marqueurs
   tenus, sondage du volume externe (réglages faits hors de l'application),
   état du pont, détection des captures interrompues.
5. **Synchro EQ principal → bus master** : repoussée de 80 ms
   (anti-rebond) pendant les glissements.
6. **Volume carte physique** : 0–100 % règle la carte ; écriture
   programmatique vs utilisateur discriminées ; le premier peuplement du
   binding (−1) n'écrit JAMAIS dans la carte.
7. **Éditeur d'EQ de piste** : bande éditable (ajout/suppression par
   INDEX, aplatir, aucun EQ, presets rapides coupe-bas/présence/aigus
   adoucis), courbe recalculée et poussée dans la `EqChain` de la piste
   sans interruption audio.
8. **Persistance** : `StoredMixer` (sortie master, faders, options,
   défauts sauvegardés, tranches complètes avec EQ) ; un périphérique
   disparu est ignoré silencieusement ; la piste DAW est restaurée telle
   quelle. **Amélioration v3** : le fichier chargé dans la tranche Lecteur
   (`StoredChannel.FilePath`) est restauré au démarrage.

## Invariants

1. La persistance ne lève jamais (l'audio prime).
2. La bascule/restauration des défauts Windows ne lève jamais et
   n'empêche jamais le démarrage/arrêt du routage.
3. Les événements des moteurs arrivent de threads audio : tout passage à
   l'UI passe par le dispatcher.
4. `Shutdown()` arrête les ponts (ASIO + périphérique) et libère le
   contrôleur de volume — sans lever.
5. Suppression de bande par INDEX (l'égalité d'objet ne fonctionne pas,
   les vues créent des instances neuves).

## Tests

- `ChannelViewModel` : fader/pan/mute/solo poussés dans le canal,
   libellés dB, badge selon le type de source, générateur (signal →
   `SetGeneratorSignal`), VU décroissant, LED EQ.
- `TrackEqViewModel` : ajout/suppression par index, aplatir, aucun EQ,
   presets rapides, courbe recalculée, poussée dans `EqChain` de la piste.
- `MixerViewModel` (parties testables sans périphérique) : persistance
  aller-retour via `StoredMixer` (options, tranches, FilePath v3),
  sélection de sortie, synthèse EQ master depuis l'EQ principal.
