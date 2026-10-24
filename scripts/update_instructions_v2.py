#!/usr/bin/env python3
# -*- coding: utf-8 -*-
"""Met à jour INSTRUCTIONS-REPRISE.md : état v2.0.0, historique, résumé express."""
import io

P = '/home/z/my-project/INSTRUCTIONS-REPRISE.md'
s = io.open(P, encoding='utf-8').read()

# ---------- 0. résumé express ----------
old = '| **Version actuelle** | 1.9.0 — **menu déroulant des signaux de test** (bruits blanc/rose/brun/violet, sinus 50 Hz→15 kHz, balayages, fichiers téléchargés) + **« Télécharger d\'autres… »** (catalogue officiel hébergé sur le dépôt GitHub, SHA-256 vérifié) ; **le curseur Volume de la barre du haut pilote aussi le volume Windows de la carte physique routée (0–100 %)** — audible en usage routé ; base 1.8.1 : défilement horizontal ; base 1.8.0 : deux versions d\'exe test télémétré + public silencieux ; base 1.7 : aide « ? », canal micro mono, volume carte physique (Ctrl+Alt+↑/↓/M), audit |'
new = '| **Version actuelle** | **2.0.0 (2026-10-24) — autonomie totale + multiplateforme** : **pilote Windows intégré** `roomeqvad` (carte virtuelle « RoomEq Input/Output »), **pilote ASIO intégré** « RoomEq ASIO » (DAW → EQ → vraie carte), **MSI WiX** ; **greffon macOS RoomEqHAL** (périphérique virtuel « RoomEq Virtual », AudioServerPlugIn + pont /tmp), **application macOS** (CoreAudio AUHAL lecture/capture, .pkg arm64+x64) ; **bibliothèque intégrée de 255 corrections casques/écouteurs** (39 marques, données AutoEq MIT, cible Harman) ; **zéro application tierce** (garde automatisée). Détails : §2. |'
assert old in s, 'ligne version introuvable'
s = s.replace(old, new)

old = '| **Projet** | RoomEq — égaliseur acoustique temps réel pour Windows (correction acoustique de pièce) |'
new = '| **Projet** | RoomEq — égaliseur temps réel **Windows & macOS** (correction acoustique de pièce + corrections casques intégrées) |'
assert old in s, 'ligne projet introuvable'
s = s.replace(old, new)

old = '| **Stack** | .NET 8 · NAudio (WASAPI/ASIO) · Avalonia 11 (MVVM CommunityToolkit) · xUnit |'
new = '| **Stack** | .NET 8 · NAudio (WASAPI/ASIO, Windows) · CoreAudio P/Invoke (macOS) · Avalonia 11 (MVVM CommunityToolkit) · xUnit · WDM/Porseur Windows (SYSVAD MIT) · AudioServerPlugIn macOS · WiX 6 |'
assert old in s, 'ligne stack introuvable'
s = s.replace(old, new)

old = '| **Tests** | 228/228 verts (150 Core + 78 Audio) |'
new = '| **Tests** | 239/239 verts (160 Core + 79 Audio) — matrice CI ubuntu + windows + **macos** ; noyau C du pont testé par g++ (Linux) ; greffon macOS compilé sur runner macOS |'
assert old in s, 'ligne tests introuvable'
s = s.replace(old, new)

old = '| **GitHub** | https://github.com/alexisfrlopez-debug/RoomEq — dépôt PUBLIC sanitisé (v1.8.1, tag `v1.8.1`) :'
new = '| **GitHub** | https://github.com/alexisfrlopez-debug/RoomEq — dépôt PUBLIC sanitisé (dernier push : **v2.0.0**, tags `v1.9.1`, `v2.0.0`) incluant `driver/`, `asio/`, `installer/`, `macos/`, `.github/` :'
assert old in s, 'ligne github introuvable'
s = s.replace(old, new)

# ---------- 2. nouveau chapitre d'état (v2.0.0) inséré avant l'ancien §2 ----------
anchor = '## 2. État du projet au 2026-10-10 (v1.9.0)'
assert anchor in s, 'ancre §2 introuvable'

v2_chapter = """## 2. État du projet au 2026-10-24 (v2.0.0) — autonomie totale, presets casques, macOS

### 2.0 Ce que couvre la v2.0.0 (demandes utilisateurs)

1. **« Sa propre carte son virtuelle et son propre driver, application
   installable, zéro référence tierce »** (Windows) — livré.
2. **« Des presets de correction pour une liste assez fournie de casques et
   écouteurs de toutes gammes »** (la capture n'étant pas applicable) — livré :
   255 modèles / 39 marques.
3. **« En plus de la version Windows, une version macOS (adapter tous les
   drivers) »** — livré : app + greffon système + installateur .pkg.

### 2.1 Architecture v2.0.0 (vue d'ensemble)

| Brique | Windows | macOS |
|---|---|---|
| Carte virtuelle | pilote noyau `roomeqvad.sys` (WDM/PortCls WaveRT, dérivé SYSVAD MIT ; paire « RoomEq Input / RoomEq Output ») | greffon AudioServerPlugIn `RoomEqHAL` (CoreAudio, périphérique unique « RoomEq Virtual » avec flux sortie + flux boucle) |
| Pont vers l'app | copie noyau render→capture (`driver/roomeqvad/bridge.cpp`) | anneau SPSC `/tmp/roomeq.device.bridge.v1.mmf` (même protocole que le pont ASIO) |
| DAW → EQ | DLL ASIO intégrée `RoomEqAsio64.dll` (IASIO auto-déclaré, CLSID `{8F5E3A91-…}`, PLL de cadence) | natif CoreAudio : le DAW sort sur « RoomEq Virtual » |
| Sortie/capture app | NAudio WASAPI + ASIO | **CoreAudio P/Invoke** (`src/RoomEq.Audio/Mac/` : AUHAL `CoreAudioOutput`/`CoreAudioCapture`, catalogue `CoreAudioCatalog`, pont `MacAudioBridge`) |
| Installateur | WiX 6 MSI + CA DTF (pnputil différé) | `pkgbuild` .pkg (arm64 + x64) + postinstall (relance coreaudiod) |
| CI | jobs tests(3 OS) + driver(WDK NuGet, signé-test) + msi | job **macos** (build+tests+greffon+pkg) |

- **Le protocole du pont** (en-tête 256 o + anneau 32768 trames 48 kHz/2c/float,
  drop-oldest, sessions) est partagé par les trois usages (ASIO, périphérique
  macOS, tests Linux) : `asio/bridge_core/` (C pur, testé g++) ↔
  `AsioBridgeInterop.cs` ↔ `roomeq_hal.cpp`. Chemin POSIX déterministe :
  `/tmp/roomeq.<nom>.mmf` (règle dans `AsioBridgeTransport.FallbackPath`,
  miroir dans `roomeq_hal.cpp` — test `DeviceBridgeTests`).
- **macOS n'autorise pas** le changement programmatique de sortie par défaut :
  l'UI le dit (bouton « Définir par défaut » masqué, guide Réglages Système).

### 2.2 Presets casques (v2.0.0)

- `src/RoomEq.Core/Headphones/headphone_presets.json` (ressource intégrée,
  262 Ko) : 255 modèles — Sennheiser (27), Sony (16), AKG (18), Beyerdynamic
  (14), HiFiMAN (13), Audio-Technica (11), Apple (6), Bose (6), Grado (6),
  SteelSeries (6), Moondrop (9), Shure (10), HyperX (4), Razer (3),
  Logitech G (4), Corsair (2), Beats (4), Samsung (3), Jabra (4), JBL (5),
  Anker (3), Audeze (7), Focal (5), Etymotic (6), Truthear (5), 7Hz (3),
  ThieAudio (5), Tin HiFi (3), Simgot (3), 64 Audio, Campfire, Westone,
  Symphonium, Xenns, Yanyin, Kiwi Ears, CCA, KZ, Blon, Tanchjim, V-Moda,
  Meze, Philips, Yamaha, Koss, Superlux, 1MORE, FiiO, TGXear, Smabat,
  EPOS Sennheiser.
- Chaque preset : préampli + 10 filtres (peaking/shelves), source de mesure
  conservée (oratory1990 > Super Review > crinacle > Rtings > …).
- Génération : `scripts/fetch_headphone_presets.py` (arbre GitHub AutoEq →
  téléchargement parallèle → JSON validé) ; **données figées au dépôt**, zéro
  dépendance d'exécution. Attribution : `THIRD_PARTY_NOTICES.md` (AutoEq MIT,
  mesures oratory1990/crinacle/etc.).
- App : `HeadphonePresetLibrary` (Core) + sélecteur marque/modèle **et**
  recherche instantanée dans l'onglet Égaliseur (`EqViewModel`, section
  « Casques & écouteurs ») ; application en un clic = preset complet.

### 2.3 Livrables v2.0.0

- `download/RoomEq-2.0.0-win-x64-test.zip`, `-win-x64-public.zip`,
  `-source.zip` (scripts `scripts/package_2.0.0.sh`) ;
- CI (tag `v2.0.0`) : `RoomEq-2.0.0-win-x64.msi` (job msi) +
  `RoomEq-2.0.0-macos-arm64.pkg` / `-macos-x64.pkg` (job macos) — artefacts
  GitHub + brouillon de Release.
- **Signature** : pilote Windows signé-test (CI, `INSTALLATION-PILOTE.md` pour
  l'attestation) ; greffon macOS ad-hoc (Developer ID + notarisation :
  `INSTALLATION-MACOS.md`).

### 2.4 Points d'attention / dettes assumées

- Le greffon macOS compile (CI + vérif syntaxique locale contre les en-têtes
  Apple réels) mais n'a **pas encore été testé sur du vrai matériel** —
  première session avec un Mac : suivre `INSTALLATION-MACOS.md` §7.
- Pas de contrôle de volume matériel dans le greffon macOS (v1 volontaire).
- La bibliothèque casques se régénère manuellement (script) ; pas de mise à
  jour automatique.

"""

s = s.replace(anchor, v2_chapter + '## 2bis. État antérieur au 2026-10-10 (v1.9.0) — historique', 1)

# ---------- 8. historique ----------
hist_anchor = '| Version | Date | Contenu | Livrables |\n|---|---|---|---|'
assert hist_anchor in s, 'tableau historique introuvable'
rows_20 = (
    '| **2.0.0** | 2026-10-24 | **Autonomie totale + macOS + presets casques.** Windows : pilote de carte virtuelle intégré (`roomeqvad`, SYSVAD MIT adapté, INF `ROOT\\ROOMEQVAD`), pilote ASIO intégré (`RoomEqAsio64.dll` + pont SPSC partagé), suppression de TOUTE référence tierce (garde automatisée `NoThirdPartyReferencesTests`), installateur MSI WiX 6 (app + pilote + ASIO, CA pnputil), CI GitHub Actions (tests 3 OS, driver WDK, msi). macOS : application CoreAudio (AUHAL lecture + capture, P/Invoke `src/RoomEq.Audio/Mac/`), greffon système RoomEqHAL (AudioServerPlugIn, périphérique « RoomEq Virtual » + flux boucle, publication dans `/tmp/roomeq.device.bridge.v1.mmf`), installateur .pkg arm64+x64 (job CI macos), icône .icns. Presets casques : 255 modèles intégrés (données AutoEq MIT, cible Harman, 39 marques toutes gammes) + sélecteur/recherche dans l\'onglet Égaliseur. 239/239 tests verts. | `RoomEq-2.0.0-win-x64-test.zip` (42 Mo), `RoomEq-2.0.0-win-x64-public.zip` (42 Mo), `RoomEq-2.0.0-source.zip` ; CI : `RoomEq-2.0.0-win-x64.msi`, `RoomEq-2.0.0-macos-arm64.pkg`, `RoomEq-2.0.0-macos-x64.pkg` |\n'
    '| 1.9.1 | 2026-10-10 | Synchro permanente des deux curseurs de volume (barre du haut + Vol. carte physique) quel que soit l\'ordre de manipulation, y compris réglages externes Windows (`VolumeSyncPolicy` testé) ; packaging + dépôt public v1.9.1. 245/245 tests. | `RoomEq-1.9.1-win-x64-{test,public,source}.zip` |\n'
)
s = s.replace(hist_anchor, hist_anchor + '\n' + rows_20, 1)

io.open(P, 'w', encoding='utf-8').write(s)
print('INSTRUCTIONS-REPRISE.md mis à jour (v2.0.0)')
