# I-EQ-01 — Bandes d'égalisation

- **Module** : M-EQ-01 · **Version de spec** : 1 · **Code** : `src/RoomEq.Core/Eq/EqBand.cs`

## Intention

Le réglage élémentaire manipulé par l'utilisateur : type, fréquence centrale,
gain, Q, activation — sérialisable JSON (presets, sauvegardes).

## Contrat public

```csharp
enum FilterType { Peaking, LowShelf, HighShelf, LowPass, HighPass, Notch }  // (I-DSP-03)
sealed class EqBand
    FilterType Type; double Frequency; double GainDb; double Q; bool Enabled;  // { get; set; }
    EqBand(); EqBand(FilterType, double freq, double gainDb, double q, bool enabled = true);
    EqBand Clone();
    string ToString();   // "1000 Hz +3,0 dB Q 1,00"
```

## Invariants

1. Valeurs par défaut : Peaking, 1000 Hz, 0 dB, Q 1,1, activé.
2. `Clone` = copie profonde indépendante.
3. Aucune validation au set (le clamp est fait par BiquadFilter.Configure) :
   la bande est un **modèle de réglage**, pas un filtre.

## Critères de test

- round-trip JSON System.Text.Json (tous champs) ;
- Clone : modifier l'original ne touche pas la copie ;
- ToString contient la fréquence, le gain signé et Q.
