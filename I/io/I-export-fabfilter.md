# I-IO-06 — Export de preset FabFilter Pro-Q 3 (.ffp)

- **Module** : M-IO-06 · **Version** : 1 · **Code** : `src/RoomEq.Core/Io/FabFilterProQ3.cs`

## Intention

Exporter un preset RoomEq (bandes actives + préampli) en fichier `.ffp`
binaire directement chargeable par le greffon FabFilter Pro-Q 3 (bouton des
presets ▸ Load…, ou glisser-déposer du fichier sur le greffon) — la
correction conçue dans RoomEq se réutilise dans un DAW.

## Contrat
```csharp
static class FabFilterProQ3
    const int MaxBands;                    // 24
    byte[] Encode(EqPreset preset);        // InvalidOperationException si > 24 bandes actives
    void Write(EqPreset preset, string path);
```

## Format (FQ3p, établi par triple recoupement)

En-tête ASCII `FQ3p`, version 4, nombre de paramètres 334, puis 334
flottants 32 bits petit-boutiste : 24 bandes de 13 valeurs, puis 22
paramètres globaux (12 + 334×4 = 1 348 octets).

- **Bande** : activée, non-bypassée, fréquence `log2(Hz)`, gain (dB),
  plage dynamique 0, dynamique activée 1, seuil dynamique 1, Q
  `log10(Q)·0,312098175+0,5` (0 ↔ Q 0,025 et 1 ↔ Q 40), type, pente 1
  (12 dB/oct), placement stéréo 2, puis deux valeurs d'usine (1, 0).
- **Types** : Bell 0, LowShelf 1, LowCut 2, HighShelf 3, HighCut 4,
  Notch 5 — mapping RoomEq : Peaking→Bell, LowShelf/HighShelf identiques,
  HighPass→LowCut, LowPass→HighCut, Notch→Notch.
- **Globaux** : réglages d'usine du greffon (zéro latence, analyseur…)
  SAUF le gain de sortie = préampli normalisé (valeur × 36 = dB).
- **Validation croisée** : le fichier produit est relu par le toolkit
  Python de référence du format (implémentation indépendante) — types,
  fréquences, Q, gains et préampli retrouvés exactement.

## Invariants

1. Seules les bandes **actives** sont exportées : le fichier sonne
   exactement comme l'égaliseur à l'écran (bande décochée = inaudible).
2. Plus de 24 bandes actives → `InvalidOperationException` au message
   actionnable (le greffon est limité à 24).
3. Valeurs hors plages du greffon ramenées aux bornes : fréquence
   10 Hz–30 kHz, Q 0,025–40, gain de bande ±30 dB, gain de sortie ±36 dB.
4. Un emplacement inutilisé = preset vierge d'usine à l'octet près.
5. `Write` écrase le fichier existant ; `Encode` est pur (aucune E/S).

## Tests

En-tête/taille ; preset vierge = usine à l'octet près ; encodage unitaire
de chaque champ (log2, échelle Q, gain direct) ; mapping des 6 types ;
préampli → gain de sortie + bornage ±36 dB ; saut des bandes décochées ;
> 24 actives → exception ; 30 bandes dont 6 décochées acceptées ; bornes
des valeurs ; relecture complète (décodage indépendant dans le test) ;
`Write` = `Encode` ; preset casque de la bibliothèque (10 bandes).
