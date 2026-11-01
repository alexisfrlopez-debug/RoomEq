# I-AUD-08 — Greffon VST 2 32 bits « RoomEq Correction » (.dll Windows)

- **Module** : M-AUD-08 · **Version de spec** : 3 (v3.5.2) · **Code** :
  `natif/roomeq-vst/` (composant natif N-VST-01, décision D21) — ABI VST 2
  autodéclarée `vst/vst2_abi.h`, greffon `vst/`, hôte de test `host/`, tests
  natifs `tests/` ; **réutilise tel quel le cœur C++ du greffon AU**
  (`natif/roomeq-au/core/` — spec I-AUD-07, parité bit à bit vérifiée).

## Intention

Appliquer la correction acoustique RoomEq **dans un DAW 32 bits** sous Windows
(Ableton Live 9 et antérieurs, FL Studio 11, Cubase 5, REAPER 32…), là où ni
l'application ni le greffon AU macOS (M-AUD-07) ne peuvent fonctionner :
l'utilisateur mesure et règle sa correction dans l'application RoomEq, exporte
son preset, puis charge ce preset dans le greffon VST inséré sur une piste ou
le master du DAW. Le greffon **applique** la correction (EQ paramétrique
identique à l'application) ; la mesure reste dans l'application.

Cible explicite de la demande : « un VST 32 bits ». Les hôtes 32 bits
(Live ≤ 9.7) ne supportent que VST 2 ; VST 3 n'existe quasiment pas en 32
bits — le format retenu est donc **VST 2 (plugins .dll, SDK 2.4)**, livré en
**PE32 i386**.

## Périmètre et non-buts

- **Formats d'entrée** : presets RoomEq `.json` (format exact de
  l'application) et `.ffp` FabFilter Pro-Q 3 (module M-IO-06) — détection par
  magie (« FQ3p » → binaire, « { » → JSON), identique au greffon AU.
- **Chargement** : éditeur Win32 intégré (bouton « Charger… » + glisser-
  déposer un fichier) et chunks VST (l'état du projet DAW restaure le preset).
- **Non-buts v1** : édition des bandes dans le greffon (lecture seule +
  préampli automatisable, comme l'AU), mesure intégrée, bandes dynamiques
  Pro-Q 3, pentes de coupe raides (12 dB/oct des biquads), format VST 3
  (décision D21), versions 64 bits et macOS (le greffon AU couvre macOS ; le
  64 bits = un drapeau du script de build, non livré).

## ABI VST 2 — approche juridique et technique

Le SDK VST 2 n'est plus distribué ni concédé par Steinberg. Ce module ne
copie **aucun en-tête Steinberg** : `vst/vst2_abi.h` déclare l'interface
minimale requise (structure `AEffect`, opcode, drapeaux) **réécrite
intégralement**, dont chaque constante a été **vérifiée valeur à valeur**
contre le SDK 2.4 authentique (lecture de contrôle uniquement, aucune copie)
ET recoupée avec l'en-tête publique vestige (LMMS/falkTX) — même méthode que
le triple recoupement du format `.ffp` (M-IO-06). Aucune dépendance tierce
au runtime : le greffon ne lie que les bibliothèques système Windows
(user32, gdi32, comdlg32, shell32).

## Contrat public (extraits)

```cpp
// vst2_abi.h — ABI VST 2.4 minimale (magie 'VstP' 0x56737450)
struct AEffect {            // 144 octets en 32 bits — ordre EXACT
    int32_t magic, numPrograms, numParams, numInputs, numOutputs, flags;
    intptr_t resvd1, resvd2;
    int32_t initialDelay;
    // ── v3.5.2 : ces CINQ rangs comblent l'espace initialDelay→uniqueID —
    // dépréciés VST 1.x et pointeurs libres, INUTILISÉS mais comptent dans
    // l'alignement binaire (retour réel v3.4.0→v3.5.1 : leur omission
    // décalait processReplacing de 20 octets → l'hôte lisait NULL dans
    // future[] et n'appelait JAMAIS le traitement — son intact, VU morts,
    // « close : 0 blocs traités »)
    int32_t realQualities, offQualities;  // dépréciés — 0
    float ioRatio;                        // déprécié — 0
    void *object, *user;                  // libres — nullptr
    int32_t uniqueID, version;
    ProcessProc processReplacing;          // offset 80 en 32 bits
    ProcessDoubleProc processDoubleReplacing;
    char future[56];
};
// VERROU D'ABI (v3.5.2) : static_asserts d'offsets (32 bits : flags=36,
// initialDelay=48, uniqueID=72, processReplacing=80… ; 64 bits de test :
// idem au facteur de largeur près) dans vst2_abi.h + test « vue hôte
// externe » (lectures PAR OFFSETS EN DUR, puis APPEL du pointeur lu) dans
// tests/test_main.cpp + contrôle équivalent dans host/mini_host.cpp — tout
// écart casse la compilation ou les tests AVANT d'atteindre un DAW.
//
// Opcodes greffon vérifiés : effOpen=0, effClose=1, effSetProgram=2,
// effGetProgram=3, effSetProgramName=4, effGetProgramName=5,
// effGetParamLabel=6, effGetParamDisplay=7, effGetParamName=8,
// effSetSampleRate=10, effSetBlockSize=11, effMainsChanged=12,
// effEditGetRect=13, effEditOpen=14, effEditClose=15, effEditIdle=19,
// effGetChunk=23, effSetChunk=24, effSetBypass=44, effGetEffectName=45,
// effGetVendorString=47, effGetProductString=48, effGetVendorVersion=49,
// effCanDo=51, effGetTailSize=52, effGetVstVersion=58.
// Opcodes hôte utilisés : audioMasterAutomate=0, audioMasterVersion=1,
// audioMasterUpdateDisplay=42.

// vst/RoomEqVst.h — le greffon (cœur réutilisé du module AU)
class RoomEqVst : public AEffect {          // uniqueID 'Rmeq', version 3502
    // numParams = 1 : « Preamp » (dB, [−36 ; +36], kVstMaxParamStrLen = 8
    //               oblige le libellé ASCII « Preamp », label « dB »).
    // numPrograms = 1 ; numInputs = numOutputs = 2 (stéréo) ;
    // flags = CanReplacing | ProgramChunks [| HasEditor sous Windows].
    // Chunks : octets BRUTS du preset .json/.ffp (sniff à la restauration).
    // Bypass : effSetBypass (opcode officiel, comme le bypass AU).
    // v3.4.1 — auto-chargement à effOpen : preset le plus récent du magasin
    //          de l'application (%APPDATA%\RoomEq\presets, ou
    //          ROOMEQ_PRESETS_DIR), essais du plus récent au plus ancien
    //          jusqu'à acceptation ; jamais si un preset est déjà actif ;
    //          l'état du projet (effSetChunk, appliqué après effOpen)
    //          reste maître (chunk vide = transparent).
    // v3.4.1 — bannière « Aucun preset chargé » dans l'éditeur (état
    //          transparent explicite) + journal du premier bloc audio.
    // Éditeur (Windows) : Win32 pur — « Charger… » (boîte GetOpenFileNameW),
    // glisser-déposer, courbe de réponse 97 points, nom/préampli/bandes.
    // Entrées usine exportées : VSTPluginMain + main (hôtes antérieurs).
};
```

## Invariants

1. **Parité numérique** : le cœur C++ est celui du greffon AU (I-AUD-07,
   invariant 1) — mêmes formules RBJ, mêmes bornes, même ordre préampli puis
   cascade, TDFII double précision. Le build 32 bits force **SSE2**
   (`-msse2 -mfpmath=sse -mstackrealign -mcpu=pentium4`) : l'arithmétique
   double est identique à celle du build 64 bits (pas de x87 80 bits).
   Écart résiduel possible vs l'application sous Windows : derniers ulp des
   `sin`/`pow` de la CRT (≤ ~1e-13 relatif, inaudible — même limite
   documentée pour l'AU sur arm64).
2. **Robustesse** : tout preset malformé est refusé avec un message (jamais
   de plantage) ; l'analyseur JSON impose profondeur ≤ 32 et limites de
   taille ; valeurs hors bornes ramenées aux bornes.
3. **Bypass** : `effSetBypass` laisse le signal inchangé bit à bit, états
   des filtres préservés.
4. **Temps réel** : `processReplacing` n'alloue ni ne bloque pas ; le
   chargement d'un preset publie un `shared_ptr` + compteur de version
   atomique, chaque canal reconstruit sa chaîne au bloc suivant (action
   utilisateur rare, documentée).
5. **Threads** : les paramètres sont `std::atomic<float>` ; le blob de chunk
   et le preset sont protégés par mutex côté interface ; le thread audio ne
   prend le mutex que si la version a changé.
6. **État de session** : `effGetChunk`/`effSetChunk` (programme) transportent
   les octets bruts du preset ; le préampli est restauré par l'hôte via
   `setParameter` (le chunk ne touche JAMAIS le paramètre — même règle que
   RestoreState côté AU, l'hôte fait foi).
7. **Auto-chargement (v3.4.1)** : à `effOpen`, si aucun preset n'est actif,
   le greffon charge le preset **le plus récent** du magasin de
   l'application (`%APPDATA%\RoomEq\presets` — le `PresetStore` de
   RoomEq.Core/Io/Stores.cs ; `ROOMEQ_PRESETS_DIR` a priorité) en essayant
   les candidats du plus récent au plus ancien jusqu'à acceptation (un
   fichier corrompu n'en bloque pas d'autres). **Précédence** : l'état du
   projet DAW (chunk restauré après `effOpen`) écrase toujours
   l'auto-chargement — un projet sauvegardé sans preset reste transparent,
   un projet avec preset le retrouve ; le préampli du preset auto-chargé
   recale le paramètre (comme un chargement interactif). Échec silencieux :
   magasin absent/vide/invalide → transparent + bannière de l'éditeur,
   jamais d'échec d'ouverture. Le journal consigne chaque essai et le
   résultat, ainsi qu'une ligne au **premier bloc audio** (taux, preset,
   bandes actives, préampli, bypass) — diagnostic terrain sans aller-retour.
8. **Dépendances** : aucune bibliothèque tierce ; ABI autodéclarée (voir
   ci-dessus) ; ne lie que user32/gdi32/comdlg32/shell32 (+kernel32 pour
   le scan du magasin).
9. **Vie du dépôt** : compilé et validé par la CI (`plugin-vst`, job Linux
   qui cross-compile avec Zig 0.14.1 — MinGW-w64 embarqué) ; `RoomEq.dll`
   PE32 i386 attaché aux releases dans `RoomEq-VST-<v>-win32.zip`.

## Critères de test

- natif Linux (`tests/test_main.cpp`, lié directement au greffon) :
  - parité vs références C# du module AU (`natif/roomeq-au/tests/data`) :
    impulsion+sweep balayé 48 kHz stéréo, 44,1 kHz mono (même signal sur
    les deux canaux), preset de bordes, preset transparent — tolérance
    2e-6 en float32 ;
  - chunks : aller-retour complet (octets préservés, réapplication sans
    toucher au paramètre) ; `.ffp` chargé et appliqué ;
  - paramètres : plage/bornes du préampli, libellés ≤ 8 caractères, nom du
    programme = nom du preset ;
  - bypass : sortie = entrée bit à bit ; rejets : preset vide/tronqué/inconnu ;
  - **auto-chargement (v3.4.1)** avec magasin factice piloté par
    `ROOMEQ_PRESETS_DIR` : le plus récent gagne (horodatages imposés) ;
    repli sur le valide suivant quand le plus récent est corrompu ;
    `.ffp` seul ; répertoire absent/vide → transparent sans échec
    d'ouverture ; précédence du chunk (restauré par-dessus
    l'auto-chargement, chunk vide → transparent) ; **parité sonore de la
    référence C# après auto-chargement** (le scénario exact du retour
    utilisateur « aucune différence de son »).
- hôte de test (`host/mini_host.cpp`, `dlopen` du `.so` Linux) : cycle de vie
  complet par l'ABI C — `VSTPluginMain`, champs `AEffect`, `effOpen`,
  `effSetSampleRate`, `effSetBlockSize`, `effMainsChanged`, `effSetChunk`,
  `processReplacing` par blocs de 512, comparaison à la référence C#,
  `effGetChunk`, `effSetBypass`, `effClose`, `dlclose`.
- build Windows 32 bits (`build-vst.sh`) : magie PE32 i386 + sous-système
  DLL vérifiés, exports `VSTPluginMain` et `main` vérifiés (objdump),
  instructions x87 absentes du chemin DSP (SSE2 forcé).
