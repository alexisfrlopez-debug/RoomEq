# RoomEq VST — greffon VST 2 32 bits « RoomEq Correction » (.dll Windows)

Greffon VST 2 **32 bits** pour appliquer la correction acoustique RoomEq dans
un DAW Windows 32 bits (Ableton Live 9 et antérieurs, FL Studio 11, Cubase 5,
REAPER 32…) : l'utilisateur mesure et règle sa correction dans l'application
RoomEq, exporte son preset, puis charge ce preset dans le greffon inséré sur
une piste ou le master. Spécification : `I/audio/I-greffon-vst.md`
(M-AUD-08 / N-VST-01, décision D21).

Le cœur DSP est **celui du greffon AU** (`natif/roomeq-au/core` — parité bit
à bit avec `RoomEq.Core` vérifiée par ses 118 tests natifs et re-vérifiée ici
par 302 vérifications à travers le greffon VST (tests + mini-hôte)).

## Ce que fait le greffon

- **Applique un preset** RoomEq : `.json` (format exact de l'application) ou
  `.ffp` FabFilter Pro-Q 3 (module M-IO-06) — détection automatique.
- **Auto-chargement (v3.4.1)** : à l'ouverture, le preset le plus récent du
  magasin de l'application (`%APPDATA%\RoomEq\presets`) est chargé
  automatiquement — le greffon corrige **dès l'insertion**, sans manipulation
  (un fichier refusé — corrompu, tronqué — n'empêche pas les autres d'être
  essayés, du plus récent au plus ancien). L'état du projet DAW reste maître :
  la restauration d'une session (chunk) écrase l'auto-chargement, et un
  projet sauvegardé sans preset reste transparent. La variable
  `ROOMEQ_PRESETS_DIR` permet de pointer un autre magasin.
- **Chargement manuel** : bouton « Charger… » ou glisser-déposer un fichier
  sur la fenêtre du greffon ; l'état du projet DAW (chunk VST) restaure le
  preset.
- **Bannière « Aucun preset chargé »** : sans preset actif, l'éditeur
  l'affiche en orange au centre — le greffon est TRANSPARENT dans cet état
  (retour utilisateur v3.4.0 : « aucune différence de son », cause racine).
- **Paramètre unique** « Preamp » (dB, −36…+36, automatisable) ; le préampli
  du preset chargé le recalc au chargement.
- **Bypass** par l'opcode VST officiel (`effSetBypass`) — sortie = entrée bit
  à bit.
- Stéréo 2 entrées / 2 sorties, tout taux d'échantillonnage, zéro latence.

Ce que le greffon ne fait PAS (v1, comme l'AU) : édition des bandes (lecture
seule — la mesure et l'édition restent dans l'application), mesure intégrée,
bandes dynamiques Pro-Q 3, pentes de coupe raides (12 dB/oct des biquads).

## Installation (utilisateur)

1. Récupérez `RoomEq-VST-<version>-win32.zip` (release GitHub ou Drive).
2. Copiez `RoomEq.dll` dans le dossier VST **32 bits** de votre DAW :
   - Ableton Live 9 : `C:\Program Files (x86)\VstPlugins` (ou le dossier
     configuré dans Préférences → File Folder → VST Plug-In Custom Folder),
     puis « Rescan » ;
   - autre hôte : son dossier de greffons 32 bits.
3. Dans le DAW, insérez « RoomEq Correction » sur une piste ou le master.
4. **Rien à faire de plus si l'application RoomEq est installée** : le
   preset le plus récent du magasin (`%APPDATA%\RoomEq\presets`) est
   chargé automatiquement — le nom du preset actif s'affiche en haut du
   greffon. Sinon, ouvrez le greffon et cliquez « Charger… » (ou déposez)
   un preset `.json`/`.ffp` exporté depuis l'application RoomEq.

Le greffon exige un processeur SSE2 (Pentium 4 / 2001 ou postérieur). Le
journal de diagnostic (peu bavard) est écrit dans
`%LOCALAPPDATA%\RoomEq\logs\roomeq-vst.log` — il consigne l'ouverture,
l'auto-chargement, les chargements/refus de presets et **une ligne au
premier bloc audio** (taux, preset actif, bandes, préampli, bypass) : c'est
la première chose à consulter si le son semble inchangé.

## Construction

```bash
# 1) cœur + greffon Linux + tests natifs + mini-hôte (tout OS)
cmake -S natif/roomeq-vst -B build && cmake --build build
./build/roomeq_vst_tests                     # 264 vérifications
./build/roomeq_vst_host build/RoomEqVst.so \
    natif/roomeq-au/tests/data               # 38 vérifications (ABI par dlopen)

# 2) DLL Windows 32 bits (cross-compilation, Zig 0.14.1 — aucun sudo requis)
natif/roomeq-vst/build-vst.sh                # → dist/RoomEq.dll + zip
```

`build-vst.sh` télécharge/require Zig (MinGW-w64 + libc++ embarqués), force
**SSE2** (`-mcpu=pentium4 -msse2 -mfpmath=sse -mstackrealign`) pour une
arithmétique double identique au build 64 bits, exporte `VSTPluginMain` et
`main` (alias `.def`), puis vérifie le binaire (PE32 i386, exports).

## Validation

| étage | quoi | résultat |
| --- | --- | --- |
| tests natifs (Linux x86-64, liés) | parité vs références C# (48 k stéréo, 44,1 k mono, bordes, transparent), chunks, préampli, bypass, accumulation, chaînes ABI, rejets, **auto-chargement (magasin factice : plus récent, repli sur valide, précédence du chunk, .ffp, répertoire absent)** | **264 vérifications, 0 échec** — parité **0.00e+00** (bit à bit), y compris après auto-chargement |
| mini-hôte (Linux x86-64, dlopen) | cycle de vie complet par l'ABI C : `VSTPluginMain`, champs `AEffect`, open/setSampleRate/setBlockSize/mainsChanged/setChunk/processReplacing 512/getChunk/setBypass/setParameter/close, entrée historique `main` | **38 vérifications, 0 échec** — parité **0.00e+00** |
| exécution 32 bits (qemu-i386, sémantique d'instructions exacte) | suite complète des 228 vérifications v3.4.0 compilée `x86-linux-gnu` + SSE2 | **228 vérifications, 0 échec** — parité **0.00e+00** (v3.4.1 : les 36 nouvelles vérifications d'auto-chargement n'ont pas été rejouées sous qemu, voir limites) |
| cycle 32 bits par dlopen (qemu-i386) | open → processReplacing → chunk → bypass → close → `main` → close, étape par étape | OK jusqu'à `dlclose` (voir limite ci-dessous) |
| binaire livré | magie PE32 i386 + DLL, exports `VSTPluginMain` + `main`, DSP en SSE2 pur (x87 confinée à l'ABI de retour libm) | ✓ (`build-vst.sh` refuse le contraire) |

### Limites connues et documentées

- **Écart ulp possible sous Windows** : les `sin`/`pow` de la CRT Windows
  (ucrt/minGW) peuvent différer de ceux de la référence C# (glibc) de ≤ ~1e-13
  relatif — inaudible (−260 dB), même limite documentée pour le greffon AU sur
  arm64.
- **Sous qemu-i386 uniquement** : une bibliothèque partagée C++ 32 bits
  construite par Zig 0.14.1 segfaulte APRÈS `dlclose` (désenregistrement des
  destructeurs `__cxa_atexit` dans le crt 32 bits de Zig — reproductible avec
  5 lignes et une seule `std::string` statique locale, sans rapport avec ce
  greffon). Sans effet sur la validation (la suite 32 bits s'exécute en lien
  statique) ni sur la DLL Windows (CRT MinGW : destructeurs exécutés au
  `FreeLibrary`, mécanisme différent). Documenté pour la traçabilité.
- **v3.4.1** : la série de tests est passée de 228 à 264 vérifications
  (auto-chargement) ; l'environnement de la session de correctif ne
  disposait plus de qemu-i386 — les 36 nouvelles vérifications ont été
  exécutées en x86-64 uniquement. Le code d'auto-chargement est du C++
  standard sans intrinsèque et le chemin DSP est inchangé (SSE2 forcé) ;
  la ligne « premier bloc » du journal permet de vérifier le comportement
  en situation réelle.
- **Premier retour utilisateur (Live 9, v3.4.0)** : « aucune différence de
  son quand le greffon est activé » — cause : aucun preset chargé (état
  transparent par défaut). Corrigé en v3.4.1 par l'auto-chargement, la
  bannière de l'éditeur et la ligne « premier bloc » du journal.
- Mono : le greffon est stéréo 2/2 (un signal mono est dupliqué sur les deux
  canaux par l'hôte, comme pour tout greffon stéréo).

## ABI VST 2 — note juridique et technique

Le SDK VST 2 n'est plus distribué ni concédé par Steinberg. Ce module ne
copie aucun en-tête Steinberg : `vst/vst2_abi.h` déclare l'interface minimale
(structure `AEffect`, opcodes, drapeaux) **réécrite intégralement**, chaque
constante ayant été vérifiée valeur à valeur contre le SDK 2.4 authentique
(lecture de contrôle uniquement) et recoupée avec l'en-tête publique vestige
(LMMS/falkTX). Le greffon ne lie que user32/gdi32/comdlg32/shell32.

## Structure

```
natif/roomeq-vst/
├── CMakeLists.txt        # cœur (réutilisé de roomeq-au) + .so + tests + hôte
├── build-vst.sh          # cross-compilation Zig → dist/RoomEq.dll (win32)
├── vst/
│   ├── vst2_abi.h        # ABI VST 2.4 minimale autodéclarée (vérifiée)
│   ├── RoomEqVst.h/.cpp  # le greffon (params, chunk, bypass, rendu)
│   ├── EditorWin32.cpp   # éditeur Win32 : Charger…, glisser-déposer, courbe
│   └── roomeq_vst.def    # exports : VSTPluginMain + main (alias)
├── host/mini_host.cpp    # mini-hôte de test (ABI par dlopen, Linux)
└── tests/                # 228 vérifications + utilitaires partagés
```
