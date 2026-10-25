# I-DSP-07 — Mixage de canaux (capture micro)

- **Module** : M-DSP-07 · **Version de spec** : 1 · **Code** : `src/RoomEq.Core/Dsp/ChannelMixer.cs`

## Intention

Réduire la capture micro entrelacée N canaux en mono : moyenne des deux
premiers canaux (comportement historique stéréo) ou isolation d'un canal précis
(micros multi-canaux dont certains canaux sont muets ou parasites).

## Contrat public

```csharp
static class ChannelMixer
    const int MixFirstTwo;   // -1
    float[] ToMono(float[] interleaved, int channels, int channelIndex);
```

## Invariants

1. `channelIndex = MixFirstTwo` (ou channels = 1) → moyenne des deux premiers
   canaux ; `channelIndex ∈ [0, channels[` → ce canal seul.
2. Index hors plage (périphérique reconfiguré entre-temps) → repli sur le
   mixage des deux premiers, jamais d'exception (robustesse temps réel).
3. `channels < 1` traité comme 1 ; les queues incomplètes (Longueur non
   multiple) sont tronquées à frames = Longueur/channels.
4. Zéro allocation au-delà du buffer de sortie.

## Critères de test

- stéréo L=[1,1,1], R=[−1,−1,−1], MixFirstTwo → [0,0,0] ;
- stéréo index 0 → [1,1,1] ; index 1 → [−1,−1,−1] ;
- 4 canaux, index 2 → troisième canal exactement ;
- 4 canaux, index 9 (invalide) → repli = moyenne des deux premiers ;
- mono → copie directe ; channels=0 → traité comme mono.
