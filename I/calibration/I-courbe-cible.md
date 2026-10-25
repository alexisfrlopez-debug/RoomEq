# I-CAL-01 — Courbe cible

- **Module** : M-CAL-01 · **Version** : 1 · **Code** : `src/RoomEq.Core/Calibration/TargetCurve.cs`

## Intention
La réponse vers laquelle la correction converge : plate, shelf de graves (confort
domestique) ou pente studio (type Harman in-room).

## Contrat
```csharp
enum TargetType { Flat, BassShelf, StudioTilt }
static class TargetCurve
    const double DefaultShelfDb = 4.0;      // shelf +4 dB sous ~105 Hz
    const double DefaultTiltDbPerOct = -1.0; // pente depuis 1 kHz
    double EvaluateDb(double freq, TargetType, double shelfDb = 4.0, double tiltDbPerOct = -1.0);
    List<CurvePoint> SampleCurve(double fMin, double fMax, int pointCount, TargetType, …);
```

## Invariants
1. Flat = 0 dB partout.
2. BassShelf = réponse d'un low-shelf biquad (105 Hz, Q 0,75, fs 48 k) recalibrée
   linéairement vers shelfDb ; plateau bas ≈ shelfDb (±5 %), haute ≈ 0 (±0,3 dB).
3. StudioTilt = tiltDbPerOct·log2(max(f,20)/1000) — 0 dB à 1 kHz, −10 dB à 10 Hz… 
   plafonné à f=20 Hz côté grave.
4. SampleCurve : points log-répartis, bornes incluses.

## Tests
Flat=0 partout ; BassShelf plateau 3,8–4,2 dB à 30 Hz et |dB|<0,3 à 4 kHz avec
shelfDb=4 ; shelfDb=8 → plateau doublé (±5 %) ; StudioTilt −1 dB à 2 kHz, +1 dB
à 500 Hz, plafond à 20 Hz ; SampleCurve bornes incluses et croissance stricte.
