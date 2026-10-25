# I-EQ-03 — Presets d'égalisation

- **Module** : M-EQ-03 · **Version de spec** : 1 · **Code** : `src/RoomEq.Core/Eq/EqPreset.cs`

## Intention

Un réglage complet d'égalisation : nom, description, préamplification,
bandes — unité de sauvegarde, d'import/export et d'application instantanée.

## Contrat public

```csharp
sealed class EqPreset
    string Name; string? Description; double PreampDb;
    DateTime CreatedAt; List<EqBand> Bands;      // { get; set; }
    EqPreset(); EqPreset(string name);
    EqPreset Clone();
    static EqPreset Flat(int bandCount = 10, double fMin = 31.25, double fMax = 16000);
```

## Invariants

1. `Flat(n)` : n bandes Peaking à 0 dB, Q 1,1, fréquences réparties
   géométriquement (ratio constant ^(1/(n−1))) entre fMin et fMax.
2. `Clone` = bandes clonées une à une (indépendance totale).
3. Sérialisable JSON sans convertisseur personnalisé.

## Critères de test

- Flat(10) : fréquences 31,25 → 16 000, ratio ≈ 1,89 entre bandes adjacentes,
  tous gains 0 dB, tous Q 1,1 ;
- Flat(1) ne lève pas (bande unique) ;
- Clone profond : vider les bandes de la copie ne vide pas l'original ;
- round-trip JSON complet.
