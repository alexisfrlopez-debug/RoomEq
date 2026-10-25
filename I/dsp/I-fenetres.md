# I-DSP-02 — Fenêtrage spectral

- **Module** : M-DSP-02 · **Version de spec** : 1 · **Code** : `src/RoomEq.Core/Dsp/Windowing.cs`

## Intention

Appliquer une pondération temporelle aux trames avant FFT afin de réduire la
fuite spectrale des raies hors-bins, pour l'analyseur temps réel et la mesure.

## Contrat public

```csharp
enum WindowType { Hann, BlackmanHarris, Rectangular }
static double[] Create(WindowType type, int n);   // coefficients
static double Sum(double[] w);                    // somme (normalisation)
```

## Invariants

1. `Hann` : w[i] = 0,5·(1 − cos(2π·i/(n−1))) ; bornes [0, 1] ; symétrique ;
   si n est pair le sommet exact (i = (n−1)/2) n'est pas échantillonné —
   ex. Hann(8) : w[0] = 0, w[3] = w[4] = 0,5·(1+cos(π/7)) ≈ 0,9505.
2. `BlackmanHarris` 4 termes : 0,35875 − 0,48829·cos(t) + 0,14128·cos(2t) −
   0,01168·cos(3t), t = 2π·i/(n−1).
3. `Rectangular` : tous 1.
4. n = 1 : fenêtre singleton (valeur 1 pour Hann/BH par la formule i=0 ; la
   division par n−1 est protégée).
5. `Sum` = somme exacte des coefficients (utilisée comme dénominateur de
   normalisation de `Fft.MagnitudeSpectrum`).

## Critères de test

- Hann(8) : w[0]=0, w[3]=w[4]≈0,9505 (±1e-9) ; somme = n/2 à ±0,5 % ;
- BlackmanHarris(1024) : somme ≈ 0,358·n (±1 %) ; toutes bornes [0, 1] ;
- Rectangular : somme = n exactement ;
- symétrie w[i] = w[n−1−i] (±1e-12).

## Non-buts

Fenêtres paramétriques (Kaiser, Gauss), fenêtres périodiques (DFT-even) —
l'analyse par trames recouvrantes n'est pas au contrat.
