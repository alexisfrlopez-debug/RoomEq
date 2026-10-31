# I-AUD-07 — Greffon AudioUnit « RoomEq Correction » (.component)

- **Module** : M-AUD-07 · **Version de spec** : 1 (v3.2.0) · **Code** :
  `natif/roomeq-au/` (composant natif N-AU-01, décision D19) — cœur `core/`,
  greffon `au/`, tests natifs `tests/`, SDK vendangé `thirdparty/AudioUnitSDK/`

## Intention

Appliquer la correction acoustique RoomEq **dans un DAW** (Ableton Live en
premier lieu, cible explicite de la demande) sous forme de greffon AudioUnit
AUv2 `.component` : l'utilisateur mesure et règle sa correction dans
l'application RoomEq, exporte son preset, puis charge ce preset dans le greffon
inséré sur une piste ou le master du DAW. Le greffon **applique** la correction
(EQ paramétrique identique à l'application, bit à bit) ; la mesure reste dans
l'application — un greffon ne peut pas piloter le son système ni la boucle de
mesure.

## Périmètre et non-buts

- **Formats d'entrée** : presets RoomEq `.json` (format exact de l'application,
  énumérations en chaînes) et `.ffp` FabFilter Pro-Q 3 (module M-IO-06) —
  détection par magie (« FQ3p » → binaire, « { » → JSON).
- **Non-buts v1** : édition des bandes dans le greffon (lecture seule + préampli
  automatisable), mesure intégrée, bandes dynamiques Pro-Q 3, pentes de coupe
  raides du Pro-Q 3 (l'import FFP applique la pente 12 dB/oct des biquads ;
  les pentes 24/48 importées sont approchées par 12 dB/oct), format VST3.

## Contrat public (extraits)

```cpp
// Cœur C++ portable (testé sous Linux, sans framework) — port EXACT de
// RoomEq.Core (BiquadFilter.cs / EqChain.cs / FabFilterProQ3.cs)
namespace roomeq {
enum class FilterType { Peaking, LowShelf, HighShelf, LowPass, HighPass, Notch };

struct EqBandDef { FilterType type; double frequency; double gainDb; double q; bool enabled; };
struct PresetDef { std::string name; double preampDb; std::vector<EqBandDef> bands; };

namespace json {  // analyseur JSON minimal (aucune dépendance)
    struct Value;  // null / bool / nombre / chaîne / tableau / objet
    bool Parse(std::string_view text, Value& out, std::string& error);
}

namespace preset {
    bool FromJson(const std::string& text, PresetDef& out, std::string& error);
    bool FromFfp(const uint8_t* data, size_t size, PresetDef& out, std::string& error);
}

namespace dsp {
    class BiquadFilter {  // RBJ, Transposed Direct Form II, double précision
        Configure(type, fs, f0, gainDb, q);  // bornes : f0 ∈ [1 ; fs·0,499], Q ∈ [0,05 ; 50]
        double Process(double); void Reset();
        double MagnitudeAt(f); double ResponseDb(f);
    };
    class EqChain {       // cascade multi-canaux + préampli
        Configure(sampleRate, channels); SetPreset(const PresetDef&);
        SetPreampDb(db); SetBypass(bool); Reset();
        Process(float* interleaved, size_t sampleCount);
        double ResponseDb(freq);
    };
}
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
   documenté).
5. **Canaux/taux** : 1 ou 2 canaux, tout taux (chaîne configurée au taux réel
   du flux) ; remise à zéro des états à `Reset` et au changement de format.
6. **État de session** : la sauvegarde du projet DAW restaure le preset
   (blob brut) et le préampli ; le paramètre « Préampli » reste automatisable.
7. **Dépendances** : aucune bibliothèque tierce au runtime ; le SDK
   AudioUnitSDK d'Apple (Apache-2.0) est vendangé dans
   `thirdparty/AudioUnitSDK/` avec sa licence — version 1.4.0, fixée.
8. **Vie du dépôt** : le composant est compilé et validé par la CI macOS
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
- CI macOS : build 0 erreur, signature ad hoc, `auval -v aufx Rmeq Rmeq` vert,
  artefact `RoomEq-AU-<version>-macos.zip`.
