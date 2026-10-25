# I-DSP-04 — Générateurs de signaux de test

- **Module** : M-DSP-04 · **Version de spec** : 1 · **Code** : `src/RoomEq.Core/Dsp/NoiseGenerator.cs`

## Intention

Produire les signaux d'écoute et d'étalonnage : bruits blanc / rose / brun /
violet et sinus, en flux par blocs, déterministes quand la graine est fournie.

## Contrat public

```csharp
sealed class NoiseGenerator(int? seed = null)
    void FillWhite (Span<float> b);   // PSD plate
    void FillPink  (Span<float> b);   // −3 dB/octave (Paul Kellet)
    void FillBrown (Span<float> b);   // −6 dB/octave, intégrateur fuyant
    void FillViolet(Span<float> b);   // +6 dB/octave, différence d'échantillons
    void FillSine  (Span<float> b, double freq, double sampleRate, double amplitude);
```

## Invariants

1. Blanc : uniforme [−1, 1].
2. Rose : filtre de Paul Kellet (coefficients exacts legacy), normalisation
   ×0,11 — spectre −3 dB/octave entre ~50 Hz et ~10 kHz à ±1,5 dB.
3. Brun : intégrateur fuyant `(s + 0,02·w)/1,02`, sortie ×3,5, RMS ≈ 0,2.
4. Violet : `w[n] − w[n−1]`, sortie ×0,25, RMS ≈ 0,29, pente +6 dB/octave.
5. Sinus : phase continue entre appels (pas de clic au franchissement de bloc).
6. Amplitudes calibrées pour un niveau perçu comparable (exigence v1.9.0) :
   RMS mesurés sur 1 s à 48 kHz — blanc ≈ 0,577, rose ≈ 0,10-0,15,
   brun ≈ 0,2, violet ≈ 0,29 (±20 %).
7. Graine fournie → flux reproductibles bit à bit (appels successifs continuent
   le flux, ne le redémarrent pas).

## Critères de test

- blanche : RMS ≈ 0,577 (±5 %), |max| ≤ 1 ;
- rose : pente médiane entre bandes d'octave 100 Hz→100 Hz/1 kHz/10 kHz dans
  [−4,5 ; −1,5] dB/octave ; RMS < 0,25 ;
- brune : RMS ∈ [0,12 ; 0,3] ; énergie du 1er quart de spectre > 70 % du total ;
- violette : RMS ∈ [0,2 ; 0,4] ; énergie du dernier quart > 70 % du total ;
- sinus 1 kHz : 2 blocs consécutifs = continuité de phase (comparer au sinus
  théorique global) ; fréquence mesurée par passages à zéro = 1 kHz ±1 % ;
- reproductibilité : deux générateurs graine 42 → mêmes buffers.

## Non-buts

Signaux composites (multi-tons), fichiers WAV téléchargés (module M-IO-04),
balayages (M-DSP-05).
