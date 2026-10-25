# I-EQ-02 — Chaîne d'égalisation temps réel

- **Module** : M-EQ-02 · **Version de spec** : 1 · **Code** : `src/RoomEq.Core/Eq/EqChain.cs`

## Intention

Appliquer un preset d'égalisation au flux audio temps réel (N canaux
entrelacés) sans jamais bloquer ni allouer sur le thread audio, et exposer la
courbe de réponse composite pour l'affichage.

## Contrat public

```csharp
sealed class EqChain
    void Configure(double sampleRate, int channels);
    void SetPreset(EqPreset preset);            // copie défensive
    void UpdateBand(int index, EqBand band);    // édition à chaud
    void SetPreamp(double preampDb);
    bool  Bypass { get; set; }
    EqPreset Preset { get; }                    // copie défensive
    double PreampDb { get; }
    double SampleRate { get; }  int Channels { get; }
    event Action? Changed;                      // après chaque modification
    void Process(float[] buffer, int offset, int count);   // entrelacé, en place
    double ResponseDb(double freq);             // dB composite (préampli inclus)
    List<CurvePoint> ResponseCurve(double fMin, double fMax, int pointCount);
```

## Invariants

1. `Process` est appelé sur le thread audio : **aucun lock, aucune allocation** ;
   la reconfiguration publie un nouveau tableau de filtres par affectation de
   référence atomique, l'ancien reste valide pour l'appel en cours.
2. Un canal = une colonne de filtres indépendante (biquad à état par canal) ;
   la préamplification est **appliquée en gain linéaire** (10^(dB/20)) en tête
   de chaîne.
3. `ResponseDb` = somme des réponses dB des bandes actives + préampli.
4. `Bypass` court-circuite `Process` (aucun traitement, échantillons intacts).
5. `Changed` est déclenché hors lock après SetPreset/UpdateBand/SetPreamp/
   Bypass ; le handler ne doit pas réentrer (documenté).
6. `Configure` ne reconstruit que si le format change (tolérance fs ±0,5 Hz) ;
   à la première configuration, le preset courant est matérialisé.
7. UpdateBand d'index invalide = no-op silencieux (robustesse UI).

## Critères de test

- process entrelacé stéréo : le même signal sur les deux canaux → sorties
  identiques entre canaux (filtres indépendants mais identiques) ;
- preset +6 dB @ 1 kHz → sinus 1 kHz en sortie gagne 6 dB ±0,15 ;
- bypass on/off : sortie = entrée exactement ;
- thread-safety de conception : Process sans exception pendant une salve de
  UpdateBand simultanés (test de charge, pas de preuve formelle) ;
- ResponseDb : bande +6 dB @ 1 kHz Q=1 → pic à 1 kHz = 6 ±0,1 dB, écart
  < 0,5 dB à 100 Hz et 10 kHz ; deux bandes s'additionnent ;
- ResponseCurve : points strictement croissants en fréquence, bornes incluses ;
- préampli −3 dB → toutes les réponses abaissées de 3 dB ;
- preset vide (aucune bande) → sortie = entrée (à la préampli près) ;
- reconfiguration de format pendant l'usage : aucun état partagé parasites
  (les filtres repartent à zéro).

## Non-buts

Fondu croisé lors des reconfigurations (les reconfigs par glisser sont déjà
suffisamment fréquentes pour éviter les clics par l'inertie du réglage UI) ;
traitement par canal désynchronisé (l'entrelacement est le contrat d'entrée).
