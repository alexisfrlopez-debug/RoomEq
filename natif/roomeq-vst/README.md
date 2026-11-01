# RoomEq VST — greffon VST 2 32 bits « RoomEq Correction » (.dll Windows)

Greffon VST 2 **32 bits** pour appliquer la correction acoustique RoomEq dans
un DAW Windows 32 bits (Ableton Live 9 et antérieurs, FL Studio 11, Cubase 5,
REAPER 32…) : l'utilisateur mesure et règle sa correction dans l'application
RoomEq, exporte son preset, puis charge ce preset dans le greffon inséré sur
une piste ou le master. Spécification : `I/audio/I-greffon-vst.md`
(M-AUD-08 / N-VST-01, décision D21).

Le cœur DSP est **celui du greffon AU** (`natif/roomeq-au/core` — parité bit
à bit avec `RoomEq.Core` vérifiée par ses 118 tests natifs et re-vérifiée ici
par 441 vérifications à travers le greffon VST (tests + mini-hôte)).

## Ce que fait le greffon

- **ÉGALISEUR MODIFIABLE DIRECTEMENT (v3.5.0)** : l'onglet Égaliseur de
  l'application, dans le greffon — glissez un point de bande pour régler sa
  fréquence (axe X) et son gain (axe Y), molette sur un point pour le facteur
  de qualité Q, double-clic sur la courbe pour créer une bande, double-clic
  sur un point pour activer/désactiver, clic droit pour le menu (type de
  filtre : cloche/shelf/notch/passe-bas/passe-haut, suppression). Les bornes
  sont celles de l'application (20 Hz–20 kHz, ±24 dB, Q 0,1–20) ; chaque
  édition re-sérialise le chunk (JSON au format exact de l'application) — la
  session DAW sauvegarde/restaure l'état édité, et un preset édité est
  relisible par l'application.
- **BANQUE CASQUES/ÉCOUTEURS INTÉGRÉE (v3.5.0)** : les 321 modèles de la
  bibliothèque de l'application (données AutoEq — MIT) sont EMBARQUÉS dans
  la DLL : deux listes déroulantes (marque, modèle) et un bouton « Appliquer »
  — aucune dépendance à l'application installée, aucun fichier à chercher
  (réponse au besoin « obligé d'aller chercher un fichier de preset
  enregistré via l'appli .exe »).
- **VU-MÈTRE ENTRÉE/SORTIE + VERSION AFFICHÉE (v3.5.0)** : diagnostic du
  « aucune différence de son » — si les barres In/Out (coin haut droit)
  bougent, l'audio traverse réellement le greffon ; la version affichée en
  bas à droite vérifie d'un coup d'œil que la bonne DLL est installée.
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
- **Presets plats ignorés (v3.4.2)** : un preset dont toutes les bandes
  actives sont à 0 dB (sans préampli) ne corrige rien — c'est le cas de
  « Plat », l'état par défaut de l'onglet Égaliseur de l'application (10
  bandes à 0 dB) sauvegardé tel quel. L'auto-chargement le SAUTE et prend
  le preset avec effet le plus récent. Les bandes Notch/LowPass/HighPass,
  elles, filtrent même à 0 dB : un preset qui en contient reste audible.
- **Chargement manuel** : bouton « Charger… » ou glisser-déposer un fichier
  sur la fenêtre du greffon ; l'état du projet DAW (chunk VST) restaure le
  preset.
- **Bannière « Aucun preset chargé »** : sans preset actif, l'éditeur
  l'affiche en orange au centre — le greffon est TRANSPARENT dans cet état.
- **Bannière « Preset plat » (v3.4.2)** : un preset chargé (manuellement ou
  par chunk de session) dont toutes les bandes sont à 0 dB est signalé en
  orange — « Preset plat — le son n'est pas corrigé ».
- **Points de bandes sur la courbe (v3.4.2)** : chaque bande active est
  marquée d'un point à sa fréquence centrale — les bandes sont visibles,
  même à 0 dB. L'entête affiche le gain maximal des bandes.
- **Paramètre unique** « Preamp » (dB, −36…+36, automatisable) ; le préampli
  du preset chargé le recalc au chargement (les presets casque ont un
  préampli négatif typique de −4 à −8 dB : c'est audible dès l'application).
- **Bypass** par l'opcode VST officiel (`effSetBypass`) — sortie = entrée bit
  à bit.
- Stéréo 2 entrées / 2 sorties, tout taux d'échantillonnage, zéro latence.

Ce que le greffon ne fait PAS : mesure intégrée, bandes dynamiques
Pro-Q 3, pentes de coupe raides (12 dB/oct des biquads), édition graphique
au point près de la courbe complète (l'édition se fait bande par bande, par
les points — comme l'application).

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
   greffon. Sinon : choisissez votre casque dans les listes déroulantes
   (banque intégrée), cliquez « Charger… » (ou déposez) un preset
   `.json`/`.ffp`, ou double-cliquez sur la courbe et réglez les bandes
   directement.
5. **Si le son semble inchangé** : vérifiez la version affichée en bas à
   droite du greffon (une ancienne DLL peut rester installée), regardez le
   VU-mètre In/Out (barres figées = l'audio ne passe pas par le greffon —
   routage du DAW à vérifier), puis le journal (ci-dessous).

Le greffon exige un processeur SSE2 (Pentium 4 / 2001 ou postérieur). Le
journal de diagnostic (peu bavard) est écrit dans
`%LOCALAPPDATA%\RoomEq\logs\roomeq-vst.log` — il consigne l'ouverture,
l'auto-chargement, les chargements/refus de presets, **une ligne au
premier bloc audio** (taux, preset actif, bandes, préampli, bypass) et,
**à la fermeture, le nombre de blocs audio traités** (v3.5.0 — 0 bloc =
l'audio n'est jamais passé par le greffon) : c'est la première chose à
consulter si le son semble inchangé.

## Construction

```bash
# 0) régénérer la banque casques embarquée (si la bibliothèque change)
python3 scripts/gen_headphone_bank.py

# 1) cœur + greffon Linux + tests natifs + mini-hôte (tout OS)
cmake -S natif/roomeq-vst -B build && cmake --build build
./build/roomeq_vst_tests                     # 403 vérifications
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
| tests natifs (Linux x86-64, liés) | parité vs références C# (48 k stéréo, 44,1 k mono, bordes, transparent), chunks, préampli, bypass, accumulation, chaînes ABI, rejets, **auto-chargement (magasin factice : plus récent, repli sur valide, précédence du chunk, .ffp, répertoire absent)**, **presets plats ignorés (v3.4.2)**, **routage out-of-place (v3.5.0 : buffers entrée/sortie distincts + entrée intacte)**, **suspension/reprise (v3.5.0)**, **édition directe (v3.5.0 : création « Manuel », clamps, type/activation/suppression, chunk round-trip JSON, parité du preset édité)**, **banque casques (v3.5.0 : 321 modèles décodés, clamps, préampli recalcé, parité du preset casque)**, **VU-mètre/compteur de blocs (v3.5.0)**, **géométrie éditeur (v3.5.1 : rect déclaré = fenêtre réelle)** | **409 vérifications, 0 échec** — parité **0.00e+00** (bit à bit) |
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
  en situation réelle. v3.4.2 : 297 vérifications (33 de plus, presets
  plats), mêmes conditions — le code ajouté est de la seule logique de
  choix de preset, aucune arithmétique DSP. v3.5.0 : 403 vérifications
  (106 de plus : routage out-of-place, suspension/reprise, édition,
  banque casques, VU) — mêmes conditions ; l'éditeur Win32 élargi
  (560×320) n'est compilé que pour la DLL Windows (exclu du build Linux),
  l'édition/banque est du C++ standard sans arithmétique DSP nouvelle
  (les clamps et la conversion sont ceux de l'application C#).
  v3.5.1 : 409 vérifications (6 de plus, géométrie de l'éditeur) — le
  correctif est purement déclaratif (rect `kEffEditGetRect`), aucun
  changement DSP ni de rendu.
- **Premier retour utilisateur (Live 9, v3.4.0)** : « aucune différence de
  son quand le greffon est activé » — cause : aucun preset chargé (état
  transparent par défaut). Corrigé en v3.4.1 par l'auto-chargement, la
  bannière de l'éditeur et la ligne « premier bloc » du journal.
- **Second retour utilisateur (Live 9, v3.4.1)** : « le VST affiche 10
  bandes de fréquence mais sur la courbe je ne vois pas les 10 bandes » —
  cause : le fichier le plus récent du magasin était un preset PLAT (10
  bandes à 0 dB — l'état par défaut de l'onglet Égaliseur de
  l'application, sauvegardé tel quel) : « 10 bandes » affichées, courbe
  plate, son inchangé — le DSP était sain (parité bit à bit). Corrigé en
  v3.4.2 : l'auto-chargement saute les presets sans effet audible, l'éditeur
  marque chaque bande d'un point sur la courbe, signale les presets plats
  et affiche le gain maximal.
- **Troisième retour utilisateur (Live 9, v3.4.2)** : « toujours aucune
  modification du son perçue — vérifie s'il n'y a pas un problème de
  routage ». Audit du câblage : `ProcessChannels` gère correctement le
  in-place ET l'out-of-place (copie entrée→sortie avant traitement), le
  mode accumulation et la suspension/reprise — tout est désormais COUVERT
  PAR LES TESTS (parité 0.00e+00 en out-of-place, entrée intacte, après
  resume). v3.5.0 ajoute les outils qui tranchent côté utilisateur :
  VU-mètre In/Out (l'audio passe-t-il par le greffon ?), version affichée
  (la bonne DLL est-elle installée ?), compteur de blocs au journal de
  fermeture, et surtout l'édition directe + banque casques intégrée (un
  preset casque à préampli −6 dB est immédiatement audible ; glisser une
  bande à +15 dB aussi).
- **Quatrième retour utilisateur (v3.5.0)** : « l'interface n'est pas
  visible entièrement, certaines parties ne sont pas accessibles » —
  cause : le rect déclaré à l'hôte (`kEffEditGetRect`) était resté à
  520×240 (taille d'avant la rangée casques) alors que la fenêtre réelle
  fait 560×320 : le DAW dimensionne sa fenêtre sur le rect déclaré, donc
  les 80 derniers pixels de hauteur (bouton « Charger… », listes
  déroulantes casque, « Appliquer ») et les 40 derniers de large
  (VU-mètre, « Appliquer ») étaient coupés. Corrigé en v3.5.1 :
  `vst/editor_geometry.h` est la source de vérité unique (fenêtre créée
  par l'éditeur ET rect déclaré dérivent des mêmes constantes),
  `static_assert` de cadrage des contrôles dans EditorWin32.cpp et test
  dédié dans la suite native — une dérive future casse la compilation ou
  les tests au lieu de couper l'interface.
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
│   ├── editor_geometry.h # géométrie éditeur : source unique rect/fenêtre (v3.5.1)
│   ├── RoomEqVst.h/.cpp  # le greffon (params, chunk, bypass, rendu, édition)
│   ├── EditorWin32.cpp   # éditeur Win32 : drag/molette/double-clic, combos
│   │                     # casques, VU-mètre, version, Charger…, glisser-déposer
│   ├── preset_write.h/.cpp     # sérialisation JSON (format application)
│   ├── headphone_bank.h/.cpp   # banque casques : décodage + conversion
│   ├── headphone_bank_data.h   # GÉNÉRÉ (scripts/gen_headphone_bank.py)
│   └── roomeq_vst.def    # exports : VSTPluginMain + main (alias)
├── host/mini_host.cpp    # mini-hôte de test (ABI par dlopen, Linux)
└── tests/                # 409 vérifications + utilitaires partagés
```
