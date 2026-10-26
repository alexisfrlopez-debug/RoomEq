# I-AUD-04 — Pont ASIO (mémoire partagée DAW → RoomEq)

- **Module** : M-AUD-04 · **Version de spec** : 1 · **Code** : `src/RoomEq.Audio/Asio/` —
  `AsioBridgeInterop.cs` (protocole), `AsioBridgeHost.cs` (transport + hôte),
  `AsioManager.cs` (pilotes)

## Intention

Recevoir l'audio d'un DAW via la DLL ASIO RoomEq (bloc natif scellé N-ASI-01) :
la DLL écrit dans une section partagée `Local\RoomEq.Asio.Bridge.v1`, l'hôte
pompe vers un tampon interne sans verrou, le mixeur tire à son rythme. Même
protocole réutilisé par le pont du périphérique virtuel macOS (I-AUD-05).
Aucun SDK Steinberg embarqué : les pilotes ASIO tiers sont pilotés via NAudio.

## Contrat public

```csharp
static class AsioBridgeInterop
    const uint Magic;         // 0x52454153 « ROOMEQ ASIO »
    const uint Version;       // 1
    const string MapName;     // "Local\RoomEq.Asio.Bridge.v1"
    const string EventName;   // "Local\RoomEq.Asio.Data.v1"
    const string DeviceBridgeMapName / DeviceBridgeEventName;
    const int RingFrames;     // 32768 (puissance de 2)
    const int Channels;       // 2
    const int HeaderBytes;    // 256
    const int BridgeRate;     // 48000
    static long MappingBytes { get; }

struct AsioBridgeHeader { … }         // miroir EXACT du C (Pack 8, 88 octets utiles)
    static class Offsets { … }         // décalages binaires verrouillés par tests

static class AsioBridgeTransport
    MemoryMappedFile CreateOrOpen(name, size) / OpenExisting(name);
    static string FallbackPath(name); // "/tmp/<nom-nettoyé>.mmf" (POSIX)

sealed class AsioBridgeHost : IDisposable
    static AsioBridgeHost Instance { get; }        // pont DAW (singleton)
    static AsioBridgeHost CreateDeviceBridge();    // pont périphérique macOS
    ISampleProvider Provider { get; }              // stéréo 48 kHz float (pull)
    bool IsHosting { get; }  bool ClientConnected { get; }  string StatusText { get; }
    void Start(); void Stop(); void PokeReader();

static class AsioManager
    string[] DriverNames; bool HasDrivers; OutputChannels / InputChannels /
    SupportedSampleRates / FramesPerBuffer / PlaybackLatency / ShowControlPanel
```

## Invariants

1. **Miroir binaire** : constantes, ordre des champs et décalages de
   l'en-tête strictement identiques à `asio/bridge_core/roomeq_asio_protocol.h`
   (Pack 8) ; `Marshal.SizeOf<AsioBridgeHeader>() == 88 ≤ 256`. L'anneau est
   une puissance de 2 (masque de couture).
2. **Transport** : sous Windows, sections nommées `Local\…` ; sinon fichier
   de repli `/tmp/<nom>.mmf` — règle CANONIQUE dupliquée dans
   `roomeq_hal.c` : ne pas changer sans mettre à jour le greffon. Le fichier
   POSIX est rendu inscriptible au groupe/autres (coreaudiod).
3. **Hôte** : `Start` crée/ouvre la section, initialise l'en-tête si vierge
   (magic, version, 48 kHz, 256 frames/tampon, anneau, canaux, session 1),
   pose `HostActive = 1`, démarre un lecteur prioritaire. Idempotent.
   `Stop` pose `HostActive = 0`, arrête le lecteur (join 1,5 s), libère tout.
4. **Lecteur** : réveil par événement DLL (repli : sondage 50 ms) ;
   changement de session (redémarrage DAW) → saut de l'historique
   (`ReadPos = WritePos`, tampon interne vidé) ; retard > anneau → saut au
   présent ; copie par segments contigus à la couture ; publication de
   l'horloge hôte (`HostClock`/`HostBeat`) pour le recadrage DLL ; aucune
   exception ne sort du thread.
5. **Consommateur** : `Provider.Read` rend TOUJOURS le nombre demandé
   (silence en cas de disette) ; jamais d'exception.
6. **Statut** : textes distincts arrêt / DAW connecté (backlog ms + échecs) /
   attente ; le pont périphérique a ses propres libellés.
7. `AsioManager` : toutes les sondes tolèrent l'absence d'ASIO (liste vide,
   jamais d'exception hors Windows).

## Critères de test (multiplateformes grâce au repli fichier)

- constantes et décalages = miroir du C (portage legacy) ;
- `FallbackPath` déterministe pour les trois noms (portage legacy) ;
- `Start` initialise l'en-tête ; idempotent ; `Stop` remet `HostActive = 0` ;
- `Provider` stéréo 48 kHz float ; silence sans client ;
- aller-retour : écriture « DLL » dans l'anneau → l'hôte livre les trames,
  `ReadPos` avance, `HostClock` publiée ;
- changement de session → réalignement du lecteur ;
- pont périphérique : section distincte, libellés dédiés.
