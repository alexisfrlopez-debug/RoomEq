# I-DSP-01 — FFT radix-2 (Cooley-Tukey)

- **Module** : M-DSP-01 · **Version de spec** : 1 · **Code** : `src/RoomEq.Core/Dsp/Fft.cs`
- **Référence legacy** : `roomeq-legacy/…/Dsp/Fft.cs` (v2.2.1)

## Intention

Transformer un signal temporel complexe entre les domaines temps et fréquence,
en place, pour l'analyse spectrale de la mesure acoustique et la déconvolution
de Farina. Tailles puissances de 2 uniquement (256 → 1 048 576 utilisées).

## Contrat public

```csharp
static bool  IsPowerOfTwo(int n);
static void  Forward(Complex[] data);     // DFT directe, en place
static void  Inverse(Complex[] data);     // IDFT, en place, résultat /n
static int   NextPow2(int n);             // plus petite puissance de 2 ≥ n
static double[] MagnitudeSpectrum(float[] frame, double[] window, double windowSum);
```

## Invariants

1. `Forward` puis `Inverse` restitue l'entrée à ±1e-9 relatif (arrondis flottants).
2. `Forward` d'un sinus d'amplitude A exactement sur le bin k (entier) concentre
   toute l'énergie sur les bins k et n−k : `|X[k]| = A·n/2` à ±1e-6 relatif.
3. `MagnitudeSpectrum` retourne n/2+1 bins normalisés par `windowSum` : un sinus
   d'amplitude A sur un bin donne une magnitude A/2 (± tolérance fenêtre) ;
   la trame est complétée par des zéros si plus courte que la fenêtre.
4. Toute taille non puissance de 2 lève `ArgumentException`.
5. Algorithme : radix-2 itératif décimation temporelle, bit-reversal préalable,
   facteurs de rotation recalculés par étape (complexité n·log₂n, sans allocation).

## Critères de test (portés de la legacy, tolérances identiques)

- round-trip sur bruit aléatoire (n = 8, 1024, 4096) ;
- sinus sur bin exact (n = 1024, k = 100, A = 0,7) → |X[k]| = A·n/2 ;
- impulsion de Dirac → spectre plat ;
- `NextPow2` : 1→1, 2→2, 3→4, 1023→1024, 1048577→2097152 ;
- `MagnitudeSpectrum` : sinus fenêtré Hann sur bin → A/2 à ±5 % ;
- taille 1000 → exception ; taille 0 → exception.

## Non-buts

FFT réel-optimisée (pack réelle+imaginaire), FFT de tailles non puissances de 2,
parallélisme SIMD — le profil d'usage (fenêtres ≤ 64 K, appel par trame de
mesure) n'exige pas plus que n·log₂n monothread.
