# I-CAL-04 — Croisement multi-signaux

- **Module** : M-CAL-04 · **Version** : 1 · **Code** : `…/Calibration/MultiSignalAnalysis.cs`

## Intention
Croiser les mesures de plusieurs signaux (bruits + sinus) : retrait des pentes
théoriques, alignement de niveau, MÉDIANE inter-signaux robuste, dispersion
quantifiant la fiabilité par bande.

## Contrat
```csharp
sealed class SignalCurve { Name, Kind, Freqs, Db }
sealed class SinePoint { Freq, LevelDb }
sealed class SignalDeviation { Name, Kind, Points }
sealed class AggregateResult { Freqs, AlignedDb, StdDb, PerSignal, ToCurve() }
static class MultiSignalAnalysis
    (double Freq, double LevelDb) SineLevelDb(float[] samples, int sampleRate, double freq);
    AggregateResult Aggregate(curves, sines, CorrectionOptions, MicProfile?, double fractionOctave = 1/6);
    Func<double,double> ConfidenceWeight(double[] freqs, double[] stdDb);
```

## Invariants
1. Grille commune 1/6 d'octave 20 Hz → 20 kHz ; bandes sans donnée écartées.
2. Bruits : correction micro → bandes → retrait de la pente théorique (blanc 0,
   rose −3,01, brun −6,02, violet +6,02 dB/oct, relative à 1 kHz) → alignement
   sur la plage d'alignement → contribution.
3. Sinus : déviation = niveau − moyenne des sinus dans la plage d'alignement
   (≥ 2 sinon ignorés) ; niveau = max du spectre Welch à ±3 % de la fréquence.
4. Agrégat = médiane par bande ; StdDb = écart-type d'échantillon.
5. Alignement final : médiane alignée sur la moyenne de la cible (comme une
   mesure unique) ; < 8 bandes couvertes → `InvalidOperationException`.
6. ConfidenceWeight : interpolation de la dispersion, 1,0 sous 1,5 dB de
   désaccord, 0,35 à 6 dB (linéaire clampé).
7. SineLevelDb : ≥ 8192 échantillons sinon `ArgumentException` ; repli −120 dB.

## Tests
Deux bruits de pente différente sur la même « pièce » simulée (spectre plat
filtré connu) → agrégat plat à ±1,5 dB ; un signal aberrant (±10 dB) ne déplace
pas la médiane ; dispersion élevée là où les signaux divergent ; sinus connu
retrouvé par SineLevelDb (±1,5 dB, scalloping Hann hors bin inclus) ; zéro
mesure → exception.
