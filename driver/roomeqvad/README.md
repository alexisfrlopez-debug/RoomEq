# Carte son virtuelle RoomEq — `roomeqvad`

Pilote Windows (WDM/PortCls WaveRT) qui expose la paire d'endpoints
virtuels utilisée par l'application :

- **« RoomEq Input (RoomEq Virtual Audio) »** — périphérique de *lecture* :
  Windows y joue tout le son système ;
- **« RoomEq Output (RoomEq Virtual Audio) »** — périphérique
  d'*enregistrement* : l'application RoomEq y lit le son à égaliser.

Les deux côtés sont **pontés en interne** par `bridge.cpp` : ce que le moteur
audio écrit côté rendu est recopié vers la capture au rythme de lecture — le
comportement d'un câble audio virtuel, **sans aucune application tierce**.

```
Windows ──▶ RoomEq Input (rendu) ──bridge──▶ RoomEq Output (capture) ──▶ RoomEq (EQ) ──▶ carte physique
```

## Contenu

| Fichier | Rôle |
|---|---|
| `roomeqvad.sln`, `roomeqvad/roomeqvad.vcxproj`, `EndpointsCommon/` | projets VS/WDK (dépendance : `EndpointsCommon.lib`) |
| `bridge.cpp`, `bridge.h` | pont rendu→capture (publication du tampon/position, copie avec silence si le moteur n'a pas écrit) |
| `roomeqvad/roomeqvad.inx` | INF d'installation (énumération racine `ROOT\ROOMEQVAD`, noms des endpoints) |
| `roomeqvad/minipairs.h` | table des endpoints — **un rendu + une capture** (élagué de SYSVAD multi-endpoints) |
| `roomeqvad/micinwavtable.h`, `EndpointsCommon/speakerwavtable.h` | formats : 48 kHz / stéréo / float 32 unique (garantit la cohérence du pont) |
| `packages.config` + `Directory.Build.props` | WDK 10 via NuGet (voie officielle, aucune extension VS) |
| `build/build_driver.ps1` | build complet : NuGet → msbuild → inf2cat → collecte |
| `build/sign_driver_test.ps1` | signature de test (certificat local) + procédure d'installation |

Dérivé de l'exemple **SYSVAD** du WDK (MIT, (c) Microsoft) — voir le fichier
`THIRD_PARTY_NOTICES.md` à la racine du dépôt. Les autres périphériques de
l'exemple (Bluetooth, USB sideband, HDMI, SPDIF, matrices de micros) ont été
retirés ; la ligne de base reste reconnaissable et les en-têtes d'origine
sont conservés.

## Compiler

Prérequis : Windows + Visual Studio 2022 (charge « Développement C++ »).
Le WDK arrive par NuGet au premier build (aucune extension à installer) :

```powershell
powershell -ExecutionPolicy Bypass -File build\build_driver.ps1            # build + inf2cat
powershell -ExecutionPolicy Bypass -File build\build_driver.ps1 -TestSign  # + signature de test
```

Sorties dans `build\x64\` : `roomeqvad.sys`, `roomeqvad.inf`, `roomeqvad.cat`,
`RoomEq-TestSigning.cer`.

## Installer / désinstaller

Voir `INSTALLATION-PILOTE.md` à la racine du dépôt (signature, mode test,
attestation Microsoft). En bref :

```bat
pnputil /add-driver roomeqvad.inf /install /scan     :: installation
pnputil /delete-driver oemNN.inf /uninstall /force   :: désinstallation
```

## Architecture

- **WaveRT** : le miniport (`EndpointsCommon/minwavert*.cpp`) alloue le tampon
  DMA partagé avec le moteur audio ; l'horloge est simulée par `UpdatePosition`
  (dérivation du temps à partir du compteur de performance).
- **Pont** (`bridge.cpp`) : le flux de rendu publie son tampon et la position
  d'écriture du moteur (`SetCurrentWritePosition`) ; le flux de capture copie
  les octets correspondants à sa propre position linéaire, en comblant par du
  silence ce qui n'a pas encore été écrit. Un verrou tournant protège le tout
  (durée de copie : quelques millisecondes d'audio).
- **Formats** : 48 kHz / 2 canaux / float 32 **unique** sur les deux
  endpoints — le moteur audio rééchantillonne tous les flux applicatifs vers
  ce format, et la copie d'octets du pont reste toujours cohérente.
- **Installation** : périphérique racine `ROOT\ROOMEQVAD` créé par `pnputil
  /add-driver … /install` (INF classique, fichiers dans le magasin de
  pilotes, `PnpLockDown`).
