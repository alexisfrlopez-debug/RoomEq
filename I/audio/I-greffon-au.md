# I-AUD-07 — Greffon AudioUnit « RoomEq Correction » (.component)

- **Module** : M-AUD-07 · **Version de spec** : 2 (v3.6.0) · **Code** :
  `natif/roomeq-au/` (composant natif N-AU-01, décision D19) — cœur `core/`,
  couche d'édition portable `edit/`, greffon `au/`, tests natifs `tests/`,
  SDK vendangé `thirdparty/AudioUnitSDK/`

## Intention

Appliquer la correction acoustique RoomEq **dans un DAW** (Ableton Live en
premier lieu, cible explicite de la demande) sous forme de greffon AudioUnit
AUv2 `.component` : l'utilisateur mesure et règle sa correction dans
l'application RoomEq, exporte son preset, puis charge ce preset dans le greffon
inséré sur une piste ou le master du DAW. Le greffon **applique** la correction
(EQ paramétrique identique à l'application, bit à bit) ; la mesure reste dans
l'application — un greffon ne peut pas piloter le son système ni la boucle de
mesure.

**v2 (3.6.0) — parité fonctionnelle avec le VST 3.5.x** (retours utilisateurs
Windows transposés à macOS) : l'égaliseur est **modifiable directement dans le
greffon** (glisser un point, molette = Q, double-clic = créer/activer, clic
droit = type/suppression), la **banque casques/écouteurs de l'application est
embarquée** (données AutoEq, listes déroulantes marque/modèle), un
**VU-mètre entrée/sortie** diagnostique le flux audio, le preset de
l'application est **auto-chargé** à l'insertion, et un **journal de diagnostic**
trace le démarrage du rendu (même rôle que roomeq-vst.log côté Windows).

## Périmètre et non-buts

- **Formats d'entrée** : presets RoomEq `.json` (format exact de l'application,
  énumérations en chaînes) et `.ffp` FabFilter Pro-Q 3 (module M-IO-06) —
  détection par magie (« FQ3p » → binaire, « { » → JSON).
- **Non-buts** : mesure intégrée, bandes dynamiques Pro-Q 3, pentes de coupe
  raides du Pro-Q 3 (l'import FFP applique la pente 12 dB/oct des biquads ;
  les pentes 24/48 importées sont approchées par 12 dB/oct), format VST3,
  sauvegarde de presets depuis le greffon (l'édition reste en mémoire + état
  de session du projet — l'application reste l'outil de mesure/export).

## Contrat public (extraits)

```cpp
// Cœur C++ portable (testé sous Linux, sans framework) — port EXACT de
// RoomEq.Core (BiquadFilter.cs / EqChain.cs / FabFilterProQ3.cs /
// HeadphonePresetLibrary.cs / EqPreset.cs) :
namespace roomeq {
enum class FilterType { Peaking, LowShelf, HighShelf, LowPass, HighPass, Notch };

struct EqBandDef { FilterType type; double frequency; double gainDb; double q; bool enabled; };
struct PresetDef { std::string name; double preampDb; std::vector<EqBandDef> bands; };

namespace preset {
    bool FromJson(const std::string& text, PresetDef& out, std::string& error);
    bool FromFfp(const uint8_t* data, size_t size, PresetDef& out, std::string& error);
    std::string ToJson(const PresetDef&);   // v2 — écriture, format EXACT de
                                            // l'application (aller-retour FromJson)
}
namespace bank {                            // v2 — banque casques embarquée
    const std::vector<HeadphoneModelDef>& HeadphoneBank();  // blob « RHP1 »
    PresetDef HeadphonePreset(const HeadphoneModelDef&);    // miroir ToEqPreset
}
}

// Couche d'édition portable (v2, edit/preset_editor.h — testée sous Linux ;
// l'intégration AudioUnit est macOS seule) :
namespace roomeq::edit {
class PresetEditor {
    // état : preset shared_ptr + octets + version atomique (comme le noyau AU)
    bool ApplyBytes(const uint8_t*, size_t, double& preampOut, std::string& error);
    bool LoadHeadphone(size_t index, double& preampOut);
    bool AutoLoadFromDir(const std::string& dir, std::string& summary);
    int  EditBand(size_t, const double* freq, const double* gain, const double* q);
    int  AddBand(double frequency, double gainDb);   // → preset « Manuel »
    bool RemoveBand(size_t); bool SetBandEnabled(size_t, bool);
    bool SetBandType(size_t, FilterType);
    // chaque mutation : bornes de l'application, re-sérialisation JSON,
    // version++, notification (l'intégration appelle PropertyChanged)
};
}

// Greffon AUv2 (macOS uniquement, ausdk Apache-2.0 vendangé)
class RoomEqAu : public ausdk::AUEffectBase {   // 'aufx' / 'Rmeq' / 'Rmeq'
    // Paramètre 0 : « Préampli » en dB, plage [−36 ; +36], initialisé au
    // préampli du preset chargé (automatisable dans le DAW).
    // Bypass standard : géré par AUEffectBase (kAudioUnitProperty_BypassEffect).
    // Propriétés custom :
    //   'RPDT' (set) : CFData = octets du fichier .json/.ffp → preset appliqué
    //   'RPST' (get) : CFDictionary { name, bands, preampDb, version }
    //   'RPcv' (get) : CFData = courbe (paires double f/Hz, dB) 97 points
    //   'RPbd' (get) : CFData = instantané binaire « RBP1 » du preset édité
    //                  (préampli, nom, bandes complètes — dessin/édition de la vue)
    //   'RPeb' (set) : CFData { index, masque, freq, gain, Q } → EditBand
    //   'RPab' (set) : CFData { freq, gain } → AddBand
    //   'RPbo' (set) : CFData { index, opération } → RemoveBand/toggle/SetBandType
    //   'RPlh' (set) : CFData { index } → LoadHeadphone (banque embarquée)
    //   'RPlv' (get) : CFData { peak entrée f32, peak sortie f32 } — VU-mètres
    // État (SaveState/RestoreState) : dictionnaire standard + blob preset.
    std::unique_ptr<ausdk::AUKernelBase> NewKernel();  // 1 EqChain par canal
};
```

## Invariants

1. **Parité numérique** : le cœur C++ reproduit EXACTEMENT RoomEq.Core — mêmes
   formules RBJ, mêmes bornes (f0 ∈ [1 ; fs·0,499], Q ∈ [0,05 ; 50]), même
   ordre (préampli en tête puis cascade des bandes actives, gain
   linéaire 10^(dB/20), seuil |préampli| > 1e-9), même implémentation TDFII en
   double précision. Les tests comparent la réponse impulsionnelle et la
   courbe de réponse aux sorties de référence générées par le code C#
   (tolérance 2e-6 en float32, les deux chaînes calculant en double).
2. **Robustesse fichiers** : tout preset malformé est refusé avec un message
   (jamais de plantage) ; l'analyseur JSON impose une profondeur ≤ 32, des
   limites de taille, et ignore les champs inconnus ; les valeurs hors bornes
   sont ramenées aux bornes (jamais rejetées).
3. **Bypass** : le greffon laisse le signal inchangé (bit à bit en in-place),
   état des filtres préservé ; géré par la base ausdk.
4. **Temps réel** : `Process` n'alloue ni ne bloque pas ; le chargement d'un
   preset publie un `shared_ptr` + un compteur de version atomique, chaque
   noyau reconstruit sa chaîne au bloc suivant (rare, action utilisateur —
   documenté). Les VU-mètres sont des atomiques écrits par les noyaux et lus
   par la vue (jamais de verrou au rendu).
5. **Canaux/taux** : 1 ou 2 canaux, tout taux (chaîne configurée au taux réel
   du flux) ; remise à zéro des états à `Reset` et au changement de format.
6. **État de session** : la sauvegarde du projet DAW restaure le preset
   (blob brut — JSON dès qu'une édition a eu lieu) et le préampli ; le
   paramètre « Préampli » reste automatisable.
7. **Édition (v2)** : bornes IDENTIQUES à l'application — fréquence
   [20 ; 20 000] Hz, gain [−24 ; +24] dB, Q [0,1 ; 20] ; chaque mutation
   re-sérialise le preset au JSON exact de l'application (un preset `.ffp`
   édité devient du JSON, relisible partout) et incrémente la version : le
   rendu bascule au bloc suivant, l'état de session emporte l'édition. Un
   payload de propriété malformé est refusé (`kAudioUnitErr_InvalidPropertyValue`),
   jamais interprété.
8. **Banque embarquée (v2)** : les modèles de la bibliothèque intégrée de
   l'application (données AutoEq — MIT) sont compilés dans le greffon (blob
   « RHP1 », décodage défensif : blob corrompu = banque tronquée, jamais de
   hors-zone) ; la conversion en preset est le miroir EXACT de
   `HeadphonePresetLibrary.ToEqPreset` (préampli clamp [−15 ; 0], mêmes clamps
   par bande). Le greffon n'exige PAS que l'application soit installée.
9. **Auto-chargement (v2)** : à l'insertion, le greffon charge le preset le
   plus récent du magasin de l'application (`~/.config/RoomEq/presets`,
   variable `ROOMEQ_PRESETS_DIR` en priorité) EN SAUTANT les presets sans
   effet audible (plats) ; l'état du projet DAW (restauré après construction)
   reste maître — il écrase toujours l'auto-chargement.
10. **Journal (v2)** : best-effort, `~/Library/Logs/RoomEq/roomeq-au.log`
    (une ligne = un fopen/append/fclose) — ouverture (version),
    auto-chargement, initialisation (taux/canaux), premier bloc (preset actif,
    effet audible), fermeture (blocs traités). Il diagnostique à distance
    « l'audio ne passe pas » (0 bloc) vs « passe sans effet » (preset plat),
    comme roomeq-vst.log côté Windows.
11. **Dépendances** : aucune bibliothèque tierce au runtime ; le SDK
    AudioUnitSDK d'Apple (Apache-2.0) est vendangé dans
    `thirdparty/AudioUnitSDK/` avec sa licence — version 1.4.0, fixée.
12. **Vie du dépôt** : le composant est compilé et validé par la CI macOS
    (`auval`, l'outil officiel d'Apple) ; le binaire `.component` signé ad hoc
    est attaché aux releases.

## Critères de test

- natif (Linux, `tests/test_main.cpp`, exécuté aussi par la CI macOS) :
  - mini-JSON : objet/tableau/échappements/nombres flottants, erreurs
    (tronqué, séquence invalide, profondeur), preset réel de l'application ;
  - presets : JSON PascalCase/camelCase, `Enabled` par défaut, type en chaîne
    ou en entier, FFP round-trip (preset C# → `.ffp` → C++ ≈ original),
    magie/erreurs ;
  - DSP : réponse impulsionnelle 48 kHz stéréo ET 44,1 kHz mono identiques aux
    références C# (fichiers `tests/data/*.f32`), preset de bordes (bornes de
    fréquence et de Q), bypass transparent, Reset, courbe de réponse (97
    points) vs référence C# ;
  - **v2 — écriture** : `ToJson` round-trip (preset → JSON → FromJson →
    identique, y compris noms accents/échappements), format exact de
    l'application (PascalCase, énumérations en chaînes) ;
  - **v2 — banque** : nombre de modèles, libellés, index invalide refusé,
    clamps de conversion, préampli recalé ;
  - **v2 — éditeur** : application de bytes (json/ffp), édition/clamps aux
    bornes de l'application, suppression/activation/type, « Manuel » sur
    première bande d'un greffon transparent, chunk round-trip (bytes →
    réapplication), auto-chargement par récence + saut des presets plats +
    magasin absent (v2 § 9) ;
- CI macOS : build 0 erreur, signature ad hoc, `auval -v aufx Rmeq Rmeq` vert,
  artefact `RoomEq-AU-<version>-macos.zip`.
