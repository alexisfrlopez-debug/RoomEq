# I-HPH-01 — Bibliothèque de presets casque (AutoEq)

- **Module** : M-HPH-01 · **Version de spec** : 1 · **Code** : `src/RoomEq.Core/Headphones/HeadphonePresetLibrary.cs`

## Intention

Corrections paramétriques intégrées pour casques et écouteurs, dérivées du
projet AutoEq (MIT, Jaakko Pasanen — mesures sur tête artificielle oratory1990,
crinacle, Rtings, Super Review… vers cible Harman). La capture au micro n'étant
pas applicable à ce type de transducteur, les données sont embarquées à la
compilation (JSON de 255 modèles, ressource intégrée).

## Contrat public

```csharp
record HeadphoneFilter(string Kind, double Frequency, double GainDb, double Q)
    FilterType FilterType { get; }       // "peaking" | "low-shelf" | "high-shelf"
    EqBand ToBand();                     // clamp 20–20k Hz, ±24 dB, Q 0,1–20
sealed class HeadphoneModel
    Id, Brand, Model, Type, Gamme, Source, PreampDb, Filters
    DisplayName, TypeLabel, GammeLabel
static class HeadphonePresetLibrary
    IReadOnlyList<HeadphoneModel> LoadAll();      // cache
    IReadOnlyList<string> Brands();               // distinctes, triées
    IReadOnlyList<HeadphoneModel> ByBrand(string);
    HeadphoneModel? Find(string id);
    IReadOnlyList<HeadphoneModel> Search(string query);
    HeadphoneModel? MatchDeviceName(string? deviceFriendlyName);
    EqPreset ToEqPreset(HeadphoneModel);
    int Count { get; }
```

## Invariants

1. Chargement : ressource intégrée `RoomEq.Core.Headphones.headphone_presets.json` ;
   seuls les modèles avec ≥ 1 filtre et marque non vide sont retenus ; cache.
2. `Search` : insensible à la casse, tous les jetons (séparés par espaces) doivent
   apparaître dans « marque modèle » ; requête blanche → tout.
3. `MatchDeviceName` : normalisation (minuscules, accents décomposés, seuls
   alphanumériques conservés) ; « marque+modèle » (score 0) bat « modèle seul »
   (score 1, exigé ≥ 8 caractères normalisés) ; à score égal, le motif le plus
   long gagne ; entrée < 4 caractères ou vide → null.
4. `ToEqPreset` : préampli clampé [−15 ; 0] dB, un EqBand par filtre (clamps
   de sécurité), description sourcée.
5. `TypeLabel`/`GammeLabel` : traductions FR (circum-aural, intra-aural,
   écouteurs classiques, casque gaming / petit prix, gamme moyenne, haut de
   gamme), repli sur la valeur brute.

## Critères de test

- bibliothèque : ≥ 200 modèles, ≥ 10 marques, tous avec filtres et marque ;
- `ByBrand` ne rend que la marque demandée, triée par modèle ;
- `Find` par id insensible à la casse ; id inconnu → null ;
- `Search("sony wh")` ne rend que des Sony dont le modèle contient « wh » ;
- `MatchDeviceName("Casque (Sony WH-1000XM4 Stereo)")` → le modèle
  WH-1000XM4 (score 0), pas un modèle voisin plus court ;
- `ToEqPreset` : préampli dans les bornes, nombre de bandes = filtres, tous
  clamps respectés même avec des données hors bornes ;
- normalisation : « Écouteurs (Beats Studio Pro) » → « beatsstudiopro ».
