# I-DSP-06 — Outils d'analyse spectrale

- **Module** : M-DSP-06 · **Version de spec** : 1 · **Code** : `src/RoomEq.Core/Dsp/SpectrumTools.cs`

## Intention

De la suite d'échantillons à la courbe exploitable : moyenne de Welch, agrégation
en bandes d'octave fractionnaires, lissage psychoacoustique, interpolations et
niveaux — le socle de l'analyseur temps réel et de la calibration.

## Contrat public

```csharp
readonly record struct CurvePoint(double Freq, double Db);
static class SpectrumTools
    const double SixthOctave;                        // 1/6
    double[] WelchDb(float[] samples, int sampleRate, int fftSize,
                     out double[] binFreqs, WindowType window = Hann,
                     double overlap = 0.5);
    void OctaveLevels(double[] binFreqs, double[] binDb, double fMin,
                      double fMax, double fractionOctave,
                      out double[] centers, out double[] levelsDb);
    double[] FractionalOctaveSmooth(double[] freqs, double[] valuesDb,
                                     double fractionOctave);
    double MeanDb(IReadOnlyList<double> freqs, IReadOnlyList<double> values,
                  double fMin, double fMax);
    double InterpLog(IReadOnlyList<double> freqs, IReadOnlyList<double> values, double f);
    double PeakDb(ReadOnlySpan<float> samples);       // dBFS crête
    double RmsDb(ReadOnlySpan<float> samples);        // dBFS efficace
```

## Invariants

1. `WelchDb` : trames fenêtrées se chevauchant (hop = fftSize·(1−overlap)),
   moyenne des puissances par bin, sortie en dB d'amplitude — un sinus
   d'amplitude A lu sur son bin vaut 20·log10(A) à ±0,2 dB.
2. `OctaveLevels` : centres géométriques de fMin à fMax par pas de
   2^fractionOctave ; niveau = moyenne **énergétique** des bins de la bande ;
   bandes vides omises.
3. `FractionalOctaveSmooth` : moyenne glissante des dB sur ±fraction/2 octave
   en domaine log2, bornes monotones → O(n) ; fraction ≤ 0 ou entrée vide →
   copie intacte.
4. `InterpLog` : interpolation linéaire en log(f) sur courbe triée croissante,
   extrapolation par valeur de bord ; recherche dichotomique.
5. `PeakDb`/`RmsDb` : dBFS (0 = pleine échelle), plancher −240 dB sur entrée
   vide, garde 1e-12 contre log(0).
6. `WelchDb` exige fftSize puissance de 2 et ≥ fftSize/4 échantillons, sinon
   `ArgumentException`.

## Critères de test

- Welch : sinus 1 kHz A=0,5, fft 4096, fs 48 kHz → pic à ~1 kHz à 20·log10(0,5)
  ±0,2 dB ; sinus hors-bin → énergie répartie, pas de NaN ;
- OctaveLevels sur spectre de sinus → la bande contenant 1 kHz domine de
  > 10 dB ses voisines vides ;
- lissage 1/1 octave d'un créneau ([0 dB si 100 Hz ≤ f ≤ 1 kHz, −60 sinon]) →
  transitions élargies mais plateau conservé à ±3 dB ;
- InterpLog : points (100, 0) et (200, 6) → interp à 141,4 Hz = 3 ±0,01 ;
  hors bornes → valeurs de bord ;
- PeakDb([±0,5]) = −6,02 dB ; RmsDb(sin A) = 20·log10(A/√2) ±0,05 ;
- Welch fftSize=1000 → exception ; 100 échantillons pour fft 4096 → exception.

## Non-buts

Spectrogramme (temps-fréquence cumulé), détection de raies, pondération A/C
(signaux de mesure gérés ailleurs).
