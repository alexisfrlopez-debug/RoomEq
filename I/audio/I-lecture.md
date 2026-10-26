# I-AUD-02 — Lecture (moteur central, sorties, providers)

- **Module** : M-AUD-02 · **Version de spec** : 1 · **Code** : `src/RoomEq.Audio/Playback/` —
  `AudioOutputs.cs` (sorties), `SampleProviders.cs` (providers), `PlaybackEngine.cs`
  (moteur), `TestSignalCatalog.cs` (catalogue intégré)

## Intention

Le moteur de lecture central : une source (fichier, signal de test) traversant
la chaîne `rééchantillonnage → adaptation de canaux → boucle → EQ → gain →
tap`, rendue sur la sortie choisie (WASAPI partagé, ASIO exclusif, CoreAudio
macOS). Fournit aussi les briques `ISampleProvider` réutilisées par le routage
(I-AUD-03) et le catalogue des signaux de test générés.

## Contrat public

```csharp
record OutputDeviceInfo(string Id, string DisplayName, string Kind); // wasapi|/asio|/coreaudio|

interface IAudioOutput : IDisposable
    string DisplayName { get; }  string Kind { get; }
    int SampleRate { get; }      int Channels { get; }
    PlaybackState State { get; } event EventHandler? PlaybackStopped;
    void Init(ISampleProvider adaptedSource); void Play(); void Pause(); void Stop();

sealed class WasapiOutput : IAudioOutput      // partagé, latence paramétrable (20..200 ms)
sealed class AsioOutput : IAudioOutput        // exclusif, taux préféré 48k>44.1k>96k>88.2k>192k
sealed class PlaybackEngine : IDisposable     // volume 0..1.5 × préampli EQ, Loop, EqChain, TapBuffer
enum TestSignal { WhiteNoise, PinkNoise, BrownNoise, VioletNoise, Sine, LogSweep }

// SampleProviders.cs — briques du graphe
GainSampleProvider(source, Func<double> gain)         // gain dynamique
ChannelAdapterSampleProvider(source, targetChannels)  // upmix/downmix
TapBuffer / TapSampleProvider                          // tampon analyseur (~2 s max)
EqSampleProvider(source, EqChain)                      // EQ dans le flux
GeneratorSampleProvider(rate, channels)                // bruits/sinus/balayage
LoopingSampleProvider(source, restart?)                // boucle à callback

// TestSignalCatalog.cs
static class TestSignalCatalog
    const string DefaultItemId;                        // "bruit-rose"
    IReadOnlyList<GeneratorSignalItem> BuiltIn { get; }
```

## Invariants

1. **Chaîne** : le moteur construit `source → WdlResampling (si taux ≠ sortie)
   → ChannelAdapter (si canaux ≠ sortie) → Looping → Eq → Gain(Volume ×
   10^(PreampDb/20)) → Tap` ; `Init` puis `Play`. Le gain est recalculé à
   chaque bloc (volume live).
2. **Possession des sources** : le moteur possède la source (fichier) et la
   libère au remplacement/échec/`Dispose` — jamais d'exception à la libération.
3. `SetOutput(id)` interrompt la lecture et reconstruit la chaîne en conservant
   la source ; sortie inconnue → `ErrorOccurred`, moteur utilisable.
4. `AsioOutput` sonde les taux préférés à la construction (repli 48 000) ;
   `Channels = clamp(2, 1, sorties pilote)`. `WasapiOutput` lit le format de
   mixage (taux/canaux réels) à la construction.
5. `ChannelAdapter` : 1→N copie le mono sur tous les canaux ; N→1 moyenne ;
   N→M (M>N) duplique le dernier canal ; N→M (M<N) garde les premiers.
   Toujours un nombre entier de frames ; les queues non multiples sont
   tronquées.
6. `TapBuffer` : files de fragments bornée à 192 000 échantillons (~2 s à
   96 kHz stéréo), thread-safe, saturation = éjection des plus anciens.
7. `GeneratorSampleProvider` : remplissage complet du buffer demandé
   (générateur infini) ; le balayage progresse de façon CONTINUE entre les
   appels (un seul générateur par instance).
8. `LoopingSampleProvider` : fin de source avec `Loop` et callback de
   rembobinage → réappel et poursuite ; garde-fou 16 itérations par Read ;
   sinon fin de lecture (0 échantillon lu rendu partiel).
9. **Catalogue** : ids uniques, amplitudes dans ]0, 1], sinus dans [5, 20000]
   Hz, balayages F1 < F2, défaut = bruit rose ; familles Bruits / Sinus /
   Balayages toutes présentes.
10. Énumérations des sorties : macOS → CoreAudio ; Windows → WASAPI actifs +
    pilotes ASIO ; chaque famille en échec (sous-système absent) est
    individuellement tolérée (liste partielle + `ErrorOccurred`).

## Critères de test

- providers purs testables sur Linux : gain, adaptation 1→2/2→1/2→6/6→2,
  tap (ordre FIFO, borne), générateur (spectre bruit blanc ≈ plat, sinus
  fréquence exacte via FFT), boucle (compteur de rembobinages) ;
- catalogue : unicité, bornes, défaut, familles (portage des tests legacy) ;
- `PlaybackEngine` : énumération sans exception sous Linux (listes vides) ;
  `Stop`/`Pause`/`Resume`/`Dispose` sans sortie ouverte = inoffensifs.
