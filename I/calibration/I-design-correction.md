# I-CAL-06 — Design de correction

- **Module** : M-CAL-06 · **Version** : 1 · **Code** : `…/Calibration/CorrectionDesigner.cs`

## Intention
Le chef d'orchestre de la calibration : spectre mesuré → lissage → correction
micro → alignement → correction par bandes ou placement automatique → preset EQ
avec préampli de sécurité + courbes de prédiction.

## Contrat
```csharp
sealed class CorrectionOptions { FreqMin=40, FreqMax=16000, MaxBoostDb=6,
    MaxCutDb=12, ThresholdDb=0.75, FractionOctave=1/3, Target=Flat, ShelfDb,
    TiltDbPerOct, AlignMinHz=200, AlignMaxHz=8000 }
sealed class CorrectionResult { Options, SampleRate, BandCenters, CorrectionDb,
    MeasuredCurve, TargetCurvePts, PredictedCurve, Preset, ResidualMaxDb(),
    ResidualRmsDb() }
static class CorrectionDesigner
    CorrectionResult Design(freqs, rawMeasuredDb, opt, sampleRate, presetName, micProfile?);
    CorrectionResult DesignFromAlignedCurve(freqs, alignedDb, opt, sampleRate,
        presetName, micProfile?, plannerOptions?, confidenceWeight?);
    CorrectionResult DesignFromCalibrationCurve(source, precision, presetName);
    static double BandQ(double fractionOctave);   // √(2^f)/(2^f − 1)
```

## Invariants
1. Design : lissage 1/6 (min 1/12) → soustraction du gain micro (si actif et
   valide) → alignement (moyennes mesurée/cible sur la plage d'alignement) →
   bandes 1/3 → correction = clamp(cible−mesure) seuillée → un peaking par bande
   corrigée, Q = BandQ(FractionOctave) → préampli = −boost max → prédiction =
   aligné + Σ réponses réelles.
2. DesignFromAlignedCurve : déviation = cible − aligné → AutoBandPlanner
   (confiance optionnelle) → preset + préampli = −boost max → prédiction =
   aligné + PredictedDb.
3. DesignFromCalibrationCurve : ré-import de la courbe MESURÉE (déjà corrigée
   micro) avec la précision choisie ; courbe < 8 points → exception.
4. BandQ(1/3) ≈ 4,318 ; préampli toujours ≤ 0 ; bandes dans la plage ±5 %.
5. Entrées invalides (longueurs incohérentes, < 32 points pour Design,
   < 8 pour les autres) → `ArgumentException`.

## Tests
Pièce synthétique (plateau + résonances + creux, cf. legacy) : Design → ≥ 5
bandes, résidu max < 3,5 dB ; limites boost/coupe respectées ; préampli =
−boost max ; BandQ(1/3) ≈ 4,32 ; profil micro +4 dB aigus → correction des
aigus réduite ; DesignFromAlignedCurve place des filtres et borne le résidu ;
DesignFromCalibrationCurve rejette une courbe vide.
