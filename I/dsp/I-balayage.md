# I-DSP-05 — Balayage sinusoïdal exponentiel (ESS, Farina)

- **Module** : M-DSP-05 · **Version de spec** : 1 · **Code** : `src/RoomEq.Core/Dsp/LogSweepGenerator.cs`

## Intention

Générer le signal d'excitation de la mesure de pièce : balayage logarithmique
dont chaque octave dure autant, se prêtant à la déconvolution de Farina pour
extraire la réponse impulsionnelle. Utilisé en flux (lecture audio par blocs)
et en régénération exacte de la référence d'analyse.

## Contrat public

```csharp
record LogSweepSpec(double F1, double F2, double DurationSeconds)
    static LogSweepSpec Default { get; }          // 20 Hz → 20 kHz, 12 s
    double EffectiveF2(int sampleRate);           // min(F2, 0,45·fs)
sealed class LogSweepGenerator(LogSweepSpec, int sampleRate)
    long TotalSamples { get; }
    double PhaseAt(long n);                       // phase exacte (rad)
    double FrequencyAt(long n);                   // fréquence instantanée (Hz)
    void Fill(Span<float> buffer, double amplitude);
    float[] GenerateFull(double amplitude = 1.0); // référence d'analyse
```

## Invariants

1. Phase en forme fermée : φ(n) = K·(e^{n·L/fs} − 1), K = 2π·F1·T/ln(F2eff/F1),
   L = ln(F2eff/F1)/T — la référence régénérée est **identique bit à bit** au
   flux émis (condition de la déconvolution).
2. Fréquence instantanée : F1·e^{n·L/fs} ; à n=0 → F1, à n=TotalSamples → F2eff.
3. Fondus cubiques smoothstep de 10 ms (min 16 échantillons) à l'attaque et à
   la fin ; silence au-delà de TotalSamples.
4. F2 effectif borné par 0,45·fs (Nyquist avec marge).
5. Validation : 0 < F1 < F2 exigé, durée ≥ 0,5 s, sinon `ArgumentException`.
6. Déterminisme total : deux générateurs de même spec → même flux.

## Critères de test (portés de SweepTests legacy)

- continuité de phase sur 3 blocs consécutifs = GenerateFull tronqué ;
- fréquence instantanée aux extrémités : F1 à ±1 Hz, F2eff à ±1 % ;
- durée mono-octave constante : temps de traversée [100, 200] Hz = temps
  [1000, 2000] Hz à ±1 % (propriété du log) ;
- fondu d'attaque : 5 premiers échantillons < 25 % de l'amplitude ;
- F2 > Nyquist → effective = 0,45·fs ; F1 ≥ F2 → exception ; durée 0,3 s →
  exception ;
- RMS global du balayage complet ∈ [0,4 ; 0,8] (sinus balayé, fondus inclus).

## Non-buts

Balayage linéaire, ESS pré-déformée (pre-ringing inverse), boucle infinie
(l'appelant rejoue GenerateFull s'il veut boucler).
