# I-CAL-08 — Catalogue des signaux de mesure

- **Module** : M-CAL-08 · **Version** : 1 · **Code** : `…/Calibration/TestSignalCatalog.cs`

## Intention
Métadonnées des signaux de mesure : noms, descriptions, pente spectrale théorique
(par octave, au niveau des bandes), plan de mesure, sinus ISO 266, spec de balayage.

## Contrat
```csharp
enum MeasurementSignalKind { WhiteNoise, PinkNoise, BrownNoise, VioletNoise, FrequencySweep }
static class MeasurementSignalCatalog
    double ExpectedBandSlopeDbPerOct(kind);   // 0 / −3,0103 / −6,0206 / +6,0206 / 0
    bool IsBroadband(kind);                   // tout sauf le balayage
    string DisplayName(kind); string Description(kind);
    IReadOnlyList<double> DefaultSineSteps;   // 31,5 … 16 000 (10 valeurs ISO 266)
    LogSweepSpec DefaultSweep(double durationSeconds);
    int SegmentCount(kinds); string PlanSummary(kinds, secondsPerSignal, sweepSeconds);
```

## Invariants
1. Les pentes sont celles des NIVEAUX DE BANDES (moyenne par bin = pente de PSD).
2. DefaultSineSteps : exactement les 10 valeurs ISO 266 croissantes.
3. DefaultSweep garde F1/F2 par défaut (20 → 20 kHz), durée paramétrée.
4. PlanSummary : bruits × secondesPerSignal + balayages × sweepSeconds, format
   « N signal(s) · durée totale ≈ X s ».

## Tests
Pentes exactes ×5 ; IsBroadband ×5 ; noms FR exacts ; 10 sinus ISO ; PlanSummary
(2 bruits + 1 balayage, 10 s, 12 s) = « 3 signal(s) · durée totale ≈ 32 s ».
