# I-DSP-03 — Filtres biquad (RBJ Audio EQ Cookbook)

- **Module** : M-DSP-03 · **Version de spec** : 1 · **Code** : `src/RoomEq.Core/Dsp/BiquadFilter.cs`

## Intention

Le filtre élémentaire de chaque bande de l'égaliseur paramétrique et de la
correction de pièce : 6 types, coefficients normalisés RBJ, traitement par
échantillon et par bloc, reconfiguration à chaud sans clic.

## Contrat public

```csharp
enum FilterType { Peaking, LowShelf, HighShelf, LowPass, HighPass, Notch }
sealed class BiquadFilter
    void Configure(FilterType, double fs, double f0, double gainDb, double q);
    void Reset();                                  // état temporel à zéro
    float  Process(float x);                       // un échantillon
    void   Process(Span<float> block);             // un bloc, en place
    double MagnitudeAt(double f);                  // |H(e^{jw})| du filtre config.
    // Propriétés miroir : Type, Frequency, GainDb, Q, SampleRate
```

## Invariants

1. Coefficients exactement conformes au RBJ Cookbook (a0 normalisé à 1) ;
   `Peaking/LowShelf/HighShelf` portent le gain, `LowPass/HighPass/Notch`
   ignorent le gain.
2. Bornes d'entrée : fs > 0 sinon `ArgumentOutOfRangeException` ; f0 clampé
   [1 ; fs·0,499] ; Q clampé [0,05 ; 50].
3. `Configure` remet l'état temporel à zéro (pas de résonance transitoire).
4. `MagnitudeAt(f)` = |H| de la fonction de transfert des coefficients courants,
   exact au niveau formel (évaluation directe de H(z) sur le cercle unité).
5. Stabilité numérique : pôles dans le cercle unité pour toutes les entrées
   valides (garanti par les formules RBJ) ; implémentation Transposed Direct
   Form II (meilleur comportement en virgule flottante que DF1).
6. Un filtre traite UN canal : un par canal actif (la chaîne EQ gère le reste).
7. `Process(Span<float>)` est équivalent échantillon par échantillon à N appels
   de `Process(float)` (même ordre d'opérations par échantillon).

## Critères de test (tolérances legacy)

- Peaking +6 dB @ 1 kHz Q=1 sur sinus 1 kHz → gain de sortie 6 dB ±0,1 dB ;
- hors fréquence centrale (100 Hz, 10 kHz) → gain < 0,5 dB ;
- LowShelf/HighShelf ±4 dB vérifiés à la coupure / loin de la coupure ;
- LowPass 1 kHz : sinus 100 Hz inchangé (±0,1 dB), sinus 4 kHz atténué > 12 dB ;
- Notch 1 kHz Q=8 : sinus 1 kHz atténué > 20 dB, sinus 2 kHz < 1 dB de perte ;
- `MagnitudeAt` cohérent avec la mesure par sinus (±0,15 dB) ;
- reconfiguration à chaud : pas de NaN/Inf, sortie bornée après reconfiguration ;
- bornes : fs ≤ 0 → exception ; f0 = 10 MHz clampé à fs·0,499 sans exception.

## Non-buts

Filtres d'ordre > 2, réglage progressif interpolé des coefficients (la chaîne
EQ gère la transition au niveau supérieur si nécessaire).
