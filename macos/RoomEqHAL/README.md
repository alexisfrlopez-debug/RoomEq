# RoomEqHAL — greffon AudioServerPlugIn macOS

Périphérique virtuel **« RoomEq Virtual »** pour macOS : l'équivalent du pilote
noyau Windows `roomeqvad.sys`, écrit sous forme de **greffon d'espace
utilisateur** chargé par `coreaudiod` (l'API publique d'Apple pour les cartes
virtuelles, déclarée dans `<CoreAudio/AudioServerPlugIn.h>`).

## Ce qu'il fait

```
        macOS (toutes les apps)                    RoomEq.app
   ┌────────────────────────────┐          ┌────────────────────────────┐
   │  sort dans « RoomEq Virtual »         │ lit l'anneau du pont       │
   └──────────────┬─────────────┘          └──────────────▲─────────────┘
                  ▼                                        │  /tmp/roomeq.device.bridge.v1.mmf
        coreaudiod : mixage des clients                    │  (même protocole que le pont ASIO :
        → WriteMix ──► greffon RoomEqHAL ──► anneau SPSC ───┘   en-tête 256 o + 32768 trames float)
                                             ▲
        flux d'entrée « boucle » ◄────────────┘ (curseur privé : OBS/QuickTime
                                                 peuvent enregistrer le son du système)
```

- **Un périphérique, deux flux** : sortie (ce que macOS joue) + entrée
  (boucle du son joué, curseur privé synchronisé sur le cycle IO de l'hôte) ;
- **48 kHz / stéréo / float32 entrelacé**, configuration figée — même
  philosophie de format unique que le reste des ponts RoomEq (Windows compris) ;
- **Publication dans le pont intégré** : chaque bloc mixé par l'hôte est écrit
  dans l'anneau SPSC (drop-oldest, compteur de trames écartées) — l'application
  RoomEq le lit, applique l'égaliseur, et rend le son sur la vraie sortie
  CoreAudio ;
- **Aucune dépendance tierce** : les maths d'anneau viennent du noyau C partagé
  du projet (`asio/bridge_core/roomeq_asio_bridge.c`, compilé tel quel) ;
- **Temps réel propre** : `DoIOOperation` ne fait ni allocation, ni verrou, ni
  journalisation ; le greffon respecte le mode sandbox de `coreaudiod` (un seul
  fichier : le pont, dans le répertoire temporaire système).

## Fichiers

| Fichier | Rôle |
|---|---|
| `roomeq_hal.cpp` | Le greffon complet (vtable COM CFPlugIn, propriétés HAL, IO, pont) |
| `RoomEqHAL.driver/Contents/Info.plist` | Enregistrement CFPlugIn statique (type AudioServerPlugIn d'Apple → usine `RoomEqHALCreate`) |
| `build_plugin.sh` | Compilation universelle (arm64 + x86_64) + signature ad-hoc |

## Compilation & installation (développement)

```bash
bash build_plugin.sh                 # universel + signature ad-hoc
sudo cp -R RoomEqHAL.driver /Library/Audio/Plug-Ins/HAL/
sudo killall coreaudiod              # rechargement immédiat
```

« RoomEq Virtual » apparaît dans Réglages Système › Son › Sortie. L'UID stable
du périphérique est `RoomEqVirtual` (c'est lui que l'application résout pour
suivre l'appareil entre les branchements et redémarrages).

Pour la **distribution**, signez le `.driver` avec un **Developer ID** et
notarisez le paquet (voir `INSTALLATION-MACOS.md`) — l'équivalent macOS de la
signature de pilote Windows.

## Vérification continue

- La CI compile le greffon sur runner macOS (`job macos` du workflow) ;
- En environnement Linux, le fichier est vérifié syntaxiquement **contre les
  en-têtes Apple réels** (`AudioServerPlugIn.h`, `AudioHardwareBase.h`) — toute
  utilisation d'une API inexistante est ainsi détectée avant même le runner.

## Choix techniques assumés

- **AudioServerPlugIn** (et non DriverKit/AudioDriverKit) : API publique, mature,
  adoptée par tous les périphériques virtuels ; le greffon vit dans `coreaudiod`
  (espace utilisateur, pas de noyau). La migration vers DriverKit reste possible
  plus tard sans toucher au pont (le protocole est indépendant du transport).
- **Pas de contrôle de volume dans le greffon** : le volume système reste réglé
  par l'hôte (mixage logiciel) ; le vrai niveau d'écoute se règle sur la sortie
  physique choisie dans RoomEq.
- **Session** : à chaque (re)chargement du greffon, l'identifiant de session du
  pont s'incrémente — l'application réaligne son curseur (comportement partagé
  avec le pont ASIO Windows, testé par `DeviceBridgeTests`).
