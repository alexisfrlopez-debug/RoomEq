# Blocs natifs scellés (hors périmètre de recodage — décision D1)

Les composants natifs ci-dessous sont conservés TELS QUELS en v2.2.2 (référence
git `deff25c`, tag `v2.2.2`). Ils ne sont PAS recodés : validés par CI (9 rounds
de correction, jobs 8/8 verts), testés chez l'utilisateur final, et leur
réécriture exigerait des environnements de build Windows/WDK et macOS non
disponibles dans la sandbox. L'application v3 les consomme via les mêmes points
d'entrée (P/Invoke, shared memory, fichiers).

| ID | Composant | Chemin legacy (`roomeq-git-ref/`) | Points d'entrée consommés par v3 |
|----|-----------|-----------------------------------|----------------------------------|
| N-DRV-01 | Pilote audio virtuel Windows `roomeqvad.sys` + INF + CAT (WDM/PortCls, SYSVAD) | `driver/roomeqvad/` | périphériques « RoomEq Input/Output » (WASAPI standard) ; installation via RoomEqDriverSetup |
| N-ASI-01 | DLL ASIO `RoomEqAsio64.dll` + `bridge_core` (protocole shared memory) | `asio/` | `RoomEq.Audio/Asio/AsioBridgeInterop.cs` + `roomeq_asio_protocol.h` (versionner le protocole des deux côtés) |
| N-MAC-01 | Greffon HAL CoreAudio macOS `RoomEqHAL.driver` + scripts pkg | `macos/RoomEqHAL/` | périphérique virtuel vu par CoreAudio ; pont C# dans `RoomEq.Audio/Mac/` |
| N-INS-01 | Installateurs : MSI WiX + custom actions, DriverSetup GUI (WinForms net472), AppSetup | `installer/` | aucun à l'exécution ; packaging de la v3 à réadapter en vague 7 |

## Règles de cohérence

1. **Toute modification de ces composants** sort du périmètre v3 : la justifier
   par une décision dédiée dans `docs/DECISIONS.md` et la faire dans un dépôt/
   environnement adapté (WDK, Xcode), jamais ici.
2. Le **protocole du pont ASIO** (`roomeq_asio_protocol.h` ↔ `AsioBridgeInterop.cs`)
   est un contrat croisé natif/managé : si l'un change, l'autre change dans le
   même commit, et la Carte porte la mention.
3. Les versions scellées vivent dans le dépôt GitHub `alexisfrlopez-debug/RoomEq`
   (branche main, tags v2.x) et dans les assets des releases — jamais recopiées
   dans ce dépôt (le miroir `roomeq-git-ref` suffit en local).
