# Pont ASIO RoomEq — `asio/`

Fait sortir un **DAW** directement dans l'égaliseur RoomEq :

```
DAW ──▶ « RoomEq ASIO » (DLL chargée dans le DAW)
            │  anneau en mémoire partagée (Local\RoomEq.Asio.Bridge.v1)
            ▼
        RoomEq (onglet Mixage, piste « DAW (ASIO) »)
            │  égaliseur principal + EQ de piste
            ▼
        vraie carte son (WASAPI ou ASIO, tranche MASTER)
```

Le DAW voit un pilote ASIO 2 standard (2 entrées — silence en v1 —,
2 sorties, 48 kHz, buffers 128–2048 recommandé 256). Le pont fonctionne même
si RoomEq est fermé (le DAW joue, l'audio est simplement ignoré jusqu'au
retour de l'application).

## Contenu

| Dossier | Rôle |
|---|---|
| `bridge_core/` | protocole partagé + anneau SPSC + PLL d'horloge — **C portable**, testé par `make test` (g++ sous Linux comme MSVC) |
| `asiodll/` | `RoomEqAsio64.dll` : implémentation COM IASIO (déclarations d'interface dans `asio_decl.h`, protocole public ASIO 2), thread de cadençage haute priorité collé à l'horloge de l'application |

Le protocole (structure de l'en-tête, noms de section, constantes) est le
**contrat** entre la DLL (C++) et l'application (C# :
`src/RoomEq.Audio/Asio/AsioBridgeInterop.cs`) — des tests des deux côtés
verrouillent la disposition binaire (256 octets d'en-tête, anneau à 256
octets, décalages).

## Compiler et tester

```bash
# noyau portable (Linux/CI)
make -C bridge_core test          # 4 groupes de tests, tout doit être vert
```

```powershell
# DLL Windows (MSVC seul, pas de WDK)
msbuild asiodll\RoomEqAsio.vcxproj /p:Configuration=Release /p:Platform=x64
```

La DLL est installée et enregistrée par le **MSI** (clé
`HKLM\SOFTWARE\ASIO\RoomEq ASIO` + CLSID) ; en portable, `regsvr32
RoomEqAsio64.dll` (administrateur) fait l'équivalent.

## Fonctionnement

- **Côté DLL** (dans le processus du DAW) : un thread priorité temps réel
  déclenche les `bufferSwitch` à cadence stable (QueryPerformanceCounter) ;
  la lecture du DAW est entrelacée puis écrite dans l'anneau (politique
  « drop-oldest » : jamais de blocage du DAW). L'écart de cadence avec
  l'application est corrigé par une **PLL lente** (0,5 % de l'erreur par
  passage, plafonnée à 100 ppm) : les horloges des deux processus restent
  collées sans se poursuivre.
- **Côté application** : `AsioBridgeHost` (C#) pompe l'anneau vers un tampon
  SPSC interne ; le bus master tire à son rythme (horloge de la vraie carte) ;
  la consommation est publiée comme « horloge hôte » qui ancre la PLL côté
  DLL. Un changement de session (redémarrage du DAW) réaligne la lecture.
- **Latence** : ~1 buffer DAW + l'attente dans l'anneau (surveillez
  « DAW connecté — N ms en attente » dans le Mixage) + la sortie master.
