# I-CAL-07 — Profils de micro

- **Module** : M-CAL-07 · **Version** : 1 · **Code** : `…/Calibration/MicProfile.cs` + `MicProfileLibrary.cs`

## Intention
Courbe gain/fréquence d'un micro de mesure, appliquée AVANT analyse
(spectre_corrigé = spectre_mesuré − gain_micro) ; import tolérant et bibliothèque
intégrée de typicités constructeur.

## Contrat
```csharp
sealed class MicProfile { Name, SourceFile, Enabled, Frequencies, GainsDb,
    CreatedAt, Brand, Technology, Notes; IsValid; PointCount; FreqMin; FreqMax;
    GainAt(freq); ToCurve(); static ParseText(text, name, sourceFile=""); }
static class MicProfileLibrary { LoadAll(); Search(library, query); ToEditableCopy(profile, newName?); }
```

## Invariants
1. ParseText accepte : .cal REW (décimales point, en-têtes, colonnes en plus),
   CSV `;` ou `,`, tabulations, espaces, décimales `,` ou `.` ; bornes plausibles
   0 < f ≤ 1e6, |g| ≤ 40 dB ; tri croissant + déduplication (dernier gagne).
2. IsValid ⇔ ≥ 5 points ; GainAt = InterpLog avec écrêtage aux bornes.
3. Bibliothèque : ressource intégrée MicLibrary.json (mise en cache) ; seuls les
   profils valides sont retenus ; Search insensible à la casse sur nom/marque/
   technologie/notes ; ToEditableCopy = copie modifiable sourcée.
4. GainAt sur profil vide = 0 (jamais d'exception).

## Tests
.cal REW 5 points → parsé, GainAt interpolé ; CSV français `;` + décimales `,` ;
interpolation log : (100,0)-(10000,20) → GainAt(1000) = 10 ; lignes invalides
ignorées ; déduplication ; bibliothèque charge ≥ 10 profils valides croissants ;
Search filtre ; ToEditableCopy indépendante.
