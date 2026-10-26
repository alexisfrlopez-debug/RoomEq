# I-AUD-06 — Moteur de mesure acoustique

- **Module** : M-AUD-06 · **Version de spec** : 1 · **Code** :
  `src/RoomEq.Audio/Measurement/RoomMeasurementEngine.cs`

## Intention

Orchestrer la calibration : jouer un signal de test sur la sortie, capturer
le micro en parallèle, rogner les transitions, analyser (Welch ou
déconvolution Farina selon le signal) et concevoir la correction. Deux modes :
mesure simple (bruit blanc) et multi-signaux (croisement médian robuste,
signaux défaillants ignorés avec avertissement visible).

## Contrat public

```csharp
sealed class RoomMeasurementEngine : IDisposable
    string? CaptureDeviceId { get; set; }         // "capture|…" ou null = défaut
    int CaptureChannelIndex { get; set; }         // ChannelMixer.MixFirstTwo par défaut
    const double SilencePeakDbfs;                 // -66
    event Action<double>? ProgressChanged;        // 0..1
    event Action<string>? PhaseChanged;           // texte de phase (multi-signaux)
    event Action<float[]>? LiveChunk;             // chunk mono temps réel

    Task<CorrectionResult> MeasureAsync(durationSeconds, amplitude, options,
        micProfile, presetName, ct);
    Task<MultiSignalResult> MeasureMultiSignalAsync(kinds, secondsPerSignal,
        sweepSeconds, amplitude, options, micProfile, presetName, ct);

sealed class MultiSignalResult
    CorrectionResult Correction; List<SignalDeviation> PerSignal;
    int SignalCount; int FilterCount; double ResidualRmsDb; List<string> Warnings;
```

## Invariants

1. **Ordre temporel** : la capture démarre AVANT la lecture (pré-roulement
   700 ms silencieux) — le rognage d'attaque (0,5 s) ne coupe jamais le
   signal utile ; pour le balayage, attente = durée + 0,8 s de marge de fin
   (les aigus de fin de balayage doivent être joués et capturés) ; 400 ms
   de drainage après l'arrêt.
2. **EQ neutre pendant la mesure** : `Eq.Bypass` forcé à vrai et restauré
   dans un `finally` (on mesure la pièce, pas l'égaliseur). Arrêt
   lecture + capture dans le même `finally`.
3. **Réduction mono** : `ChannelMixer.ToMono(samples, ch, CaptureChannelIndex)`
   puis remontée `LiveChunk` — l'analyseur temps réel voit le même signal
   que la mesure.
4. **Rogning** : 0,5 s en tête (transitoire d'attaque, tombe dans le
   pré-roulement), 0,25 s en queue par défaut (0,05 s pour le balayage —
   préserver la fin).
5. **Garde-fous** : crête < −66 dBFS → erreur « le micro ne capte rien »
   (message actionnable : éviter les cartes virtuelles, vérifier la
   sourdine, le niveau) ; segment < 0,5 s → erreur « trop court » ;
   mesure simple < 2 s → erreur dédiée.
6. **Multi-signaux** : chaque signal joué et capturé tour à tour ; bruits →
   Welch 16384 ; balayage → `SweepAnalyzer.Analyze` (déconvolution) hors du
   thread d'orchestration (`Task.Run`) ; un signal en échec est IGNORÉ avec
   avertissement (liste `Warnings`, journal, phase « ⚠ ») — la mesure
   n'échoue complètement que si AUCUN signal n'est exploitable (message
   incluant la dernière erreur).
7. **Croisement** : `MultiSignalAnalysis.Aggregate` (médiane, 1/6 octave) →
   poids de confiance → `CorrectionDesigner.DesignFromAlignedCurve` (sans
   planificateur forcé : la médiane place déjà les bandes sans doublon).
   Le taux de référence de la correction = taux de sortie courant.
8. Journalisation : entréesInfo par segment (durée, crête, RMS), balayage
   (pic, fenêtre IR, bandes), synthèse finale (signaux croisés, filtres,
   préampli, avertissements).

## Critères de test

- constantes : `SilencePeakDbfs = −66`, repli format, marges (0,5/0,25/0,05 s,
  +0,8 s balayage) verrouillées par spec (aucun périphérique requis pour la
  lecture des valeurs) ;
- Linux/CI : le moteur n'est pas instanciable sans périphériques — les
  parties pures (assemblage des segments : rognage tête/queue, concaténation
  exacte) sont vérifiées via la spec et les tests Core sous-jacents
  (SweepAnalyzer, MultiSignalAnalysis, CorrectionDesigner déjà couverts en
  vagues 2-3).
