# I-CAL-02 — Analyse de balayage (déconvolution de Farina)

- **Module** : M-CAL-02 · **Version** : 1 · **Code** : `…/Calibration/SweepAnalyzer.cs`

## Intention
Extraire la réponse de la pièce d'une capture du balayage ESS : déconvolution
fréquentielle H = Y·conj(X)/|X|² (inverse régularisé), fenêtrage de la réponse
impulsionnelle autour du son direct, niveaux 1/6 d'octave.

## Contrat
```csharp
sealed record SweepAnalysis { BandFreqs, BandLevelsDb, PeakDelaySeconds,
                              PeakMagnitude, IrWindowSeconds }
static class SweepAnalyzer
    const double FractionOctave = 1/6;
    SweepAnalysis Analyze(float[] captured, int sampleRate, LogSweepSpec spec);
```

## Invariants
1. Référence régénérée à l'identique (LogSweepGenerator) ; FFT de taille
   NextPow2(lenY+lenX) — déconvolution linéaire sans repliement.
2. Régularisation : plancher |X|²·max·1e-8 (hors bande du balayage).
3. Recherche du pic IR : ±(0,5 s ; 1,2 s) incluant les retards repliés ;
   rejet si pic < 1000× la médiane (`InvalidOperationException` explicite).
   [v3/D8 : la v2.2.2 rejetait sous 8× — laissait passer une capture de bruit
   seul, ratio ~186 par effet différenciateur du 1/|X|², et rendait des bandes
   absurdes ; balayage réel mesuré ~1,2e7, bruit passe-bas réaliste 9,2 → le
   seuil 1000 sépare avec quatre décades de marge]
4. Fenêtre IR : 5 ms avant le pic (demi-Hann montante), corps plat, chute cosinus
   sur le dernier quart ; queue = clamp(0,10 ; 0,40 ; 0,85·T·ln2/ln(F2eff/F1)) s —
   évite les harmoniques de rang 2+ de l'ESS.
5. Sortie : bandes 1/6 d'octave de F1·1,05 à F2eff·0,97 ; ≥ 8 bandes sinon exception.
6. Captures < fs/2 ou fs < 8000 → `ArgumentException`.

## Tests
Bouclage audio simulé (réponse connue : filtre peaking +6 dB @ 1 kHz appliqué au
balayage) → bande maximale de 700–1400 Hz ≥ 3,5 dB au-dessus du lointain (la
moyenne énergétique 1/6 d'octave dilue un pic Q≈1,2 : ~3,9 dB mesurés) ;
capture de bruit seul (blanc ou passe-bas) → exception « balayage non détecté » ;
fragment trop court → exception ; retard du pic cohérent avec le décalage
injecté (±2 ms).
