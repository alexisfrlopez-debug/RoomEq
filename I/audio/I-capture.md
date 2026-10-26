# I-AUD-01 — Capture micro (WASAPI / CoreAudio)

- **Module** : M-AUD-01 · **Version de spec** : 1 · **Code** : `src/RoomEq.Audio/Capture/CaptureEngine.cs`

## Intention

Capturer le micro pour la mesure acoustique : énumérer les entrées actives,
démarrer une capture sur un périphérique choisi (ou l'entrée par défaut) et
livrer les échantillons au callback de mesure. La capture est la source
amont du moteur de mesure (I-AUD-06) ; elle ne fait AUCUN traitement audio
(réduction mono, analyse : déléguées au consommateur).

## Contrat public

```csharp
sealed class CaptureEngine : IDisposable
    event EventHandler<string>? Stopped;       // arrêt (erreur incluse)
    string? LastError { get; }                 // dernière erreur (UI)
    string LastStartedFormat { get; }          // description du format réel
    IReadOnlyList<OutputDeviceInfo> EnumerateDevices();   // id "capture|<id>"
    int? GetChannelCount(string? deviceId);    // canaux du format de mixage
    void Start(string? deviceId, Action<float[], int, int> onChunk);
    void Stop();
```

- `onChunk(échantillons entrelacés float, canaux, fréquence)` — appelé sur le
  thread audio : rester rapide, jamais d'exception propagée.
- Windows : WASAPI (`WasapiCapture`). macOS : AUHAL (`Mac.CoreAudioCapture`).

## Invariants

1. Format demandé : float 48 kHz mono ; si le périphérique refuse, repli sur
   le format de mixage natif (taux/canaux remontés au callback). Le repli
   est journalisé (`DiagnosticsLog`).
2. `deviceId` null ou ne commençant pas par `capture|` → entrée par défaut.
   `capture|<id>` introuvable → `InvalidOperationException` avec message
   actionnable (« débranché ? »).
3. Le sous-système audio absent (Linux/CI, session sans service) : les
   énumérations renvoient une liste VIDE et positionnent `LastError` —
   jamais d'exception depuis `EnumerateDevices`/`GetChannelCount`.
4. `Start` échoué laisse l'objet dans un état cohérent (ancienne capture
   libérée, `LastError` posé) ; `Stop`/`Dispose` jamais en exception.
5. Le callback de capture ne JAMAIS faire planter le thread audio :
   l'exception d'un `onChunk` est avalée côté émetteur (la mesure gère
   ses propres erreurs).
6. `GetChannelCount` → null si indisponible (utilisé pour proposer la
   sélection mono des micros multi-canaux).

## Critères de test

- Linux (CI) : `EnumerateDevices` vide sans exception, `GetChannelCount`
  null, `Stop`/`Dispose` sans exception — le constructeur n'exige pas
  Windows ;
- le préfixe `capture|` est résolu uniquement côté démarrage (jamais dans
  les énumérations renvoyées aux UI).
