# I-CAL-03 — Planificateur automatique de bandes

- **Module** : M-CAL-03 · **Version** : 1 · **Code** : `…/Calibration/AutoBandPlanner.cs`

## Intention
Placer automatiquement les filtres peaking qui annulent une courbe de déviation :
algorithme glouton, à chaque itération le plus grand écart résiduel est corrigé
par un filtre dont fréquence, gain ET Q sont déduits de la largeur mesurée de
l'anomalie ; simulation par la VRAIE réponse des biquads.

## Contrat
```csharp
sealed class AutoBandOptions { FreqMin=40, FreqMax=16000, MaxBoostDb=6,
    MaxCutDb=12, ThresholdDb=0.75, MaxFilters=20, ResidualTargetRmsDb=1.0,
    MinQ=0.7, MaxQ=8.0, SampleRate, MinSpacingOctaves=1/12, SpacingFactor=0.6;
    static From(CorrectionOptions, int sampleRate); }
sealed class PlannedFilter { Frequency, GainDb, Q }
sealed class AutoBandResult { Filters, DuplicatesRemoved, PredictedDb,
    ResidualDb, ResidualRmsDb, ResidualMaxDb, StopReason }
static class AutoBandPlanner
    static double BandwidthOctaves(double q);        // log2((2Q+1)/(2Q-1))
    static AutoBandResult Plan(double[] freqs, double[] deviationDb,
        AutoBandOptions, Func<double,double>? confidenceWeight = null);
```

## Invariants
1. Boucle : pic résiduel HORS zones occupées → largeur à mi-hauteur → Q
   (√(fLo·fHi)/(fHi−fLo), clampé) → gain (clampé, dosé par confiance) → placement
   + zone d'exclusion + résidu recalculé avec la vraie réponse biquad.
2. ANTI-DOUBLONS : zone d'exclusion = max(MinSpacingOctaves,
   SpacingFactor·BandwidthOctaves(Q)) ; un candidat qui violerait l'espacement
   avec un filtre existant est masqué ; passe finale de déduplication garantie.
3. Arrêts : MaxFilters, résidu RMS ≤ cible (ET résidu max < 2 dB), seuil, tout couvert.
4. Terminaison garantie (budget de masquage).
5. Entrée : ≥ 8 points, longueurs égales, plage valide, sinon `ArgumentException`.

## Tests
Déviation synthétique (créneau +bosse @ 120 Hz) → un filtre placé près de 120 Hz,
résidu < 2 dB ; bumps étroites → Q élevés, bump large → Q bas ; limite MaxFilters=3
respectée ; deux anomalies identiques → jamais deux filtres au même centre
(espacement ≥ MinSpacingOctaves) ; confiance 0 → aucun gain appliqué ; bornes
d'entrée invalides → exceptions.
