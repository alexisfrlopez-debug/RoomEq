# I-CAL-05 — Précision de correction

- **Module** : M-CAL-05 · **Version** : 1 · **Code** : `…/Calibration/CorrectionPrecision.cs`

## Intention
Quatre niveaux de précision (Douce → Très fine) traduits en paramètres du
planificateur : nombre de bandes, seuils, Q, espacement.

## Contrat
```csharp
enum CorrectionPrecision { Soft, Standard, Fine, Ultra }
record CorrectionPrecisionOption(Kind, Name, Description);
static class CorrectionPrecisionCatalog { All; Option(precision); }
static class CorrectionPrecisionSettings { ToOptions(precision, CorrectionOptions, int sampleRate); }
```

## Invariants (valeurs exactes)
| Niveau | MaxFilters | Seuil | CibleRMS | Q min/max | Espacement | SpacingFactor |
|---|---|---|---|---|---|---|
| Soft | 8 | 1,5 | 2,0 | 0,5–4,0 | 1/6 | 0,8 |
| Standard | 14 | 1,0 | 1,5 | 0,7–6,0 | 1/12 | 0,7 |
| Fine | 20 | 0,75 | 1,0 | 0,7–8,0 | 1/12 | 0,6 |
| Ultra | 30 | 0,5 | 0,75 | 1,0–12,0 | 1/24 | 0,5 |

## Tests
Chaque niveau → paramètres exacts du tableau ; Option() retrouve son entrée ;
catalogue : 4 entrées nommées Douce/Standard/Fine/Très fine.
