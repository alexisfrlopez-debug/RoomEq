# I-AUD-05 — Pont macOS (CoreAudio / greffon RoomEqHAL)

- **Module** : M-AUD-05 · **Version de spec** : 1 · **Code** : `src/RoomEq.Audio/Mac/` —
  `CoreAudioApi.cs` (P/Invoke), `CoreAudioCatalog.cs`, `CoreAudioOutput.cs`,
  `CoreAudioCapture.cs`, `MacAudioBridge.cs`

## Intention

Équivalent macOS de WASAPI + du pilote virtuel : énumérer les périphériques
CoreAudio, lire (AUHAL sortie), capturer le micro (AUHAL entrée, TCC), et
consommer le pont du périphérique virtuel « RoomEq Virtual » publié par le
greffon scellé N-MAC-01 (même protocole que le pont ASIO, section dédiée).

## Contrat public

```csharp
// CoreAudioApi (interne) : liaisons résolues au PREMIER APPEL uniquement —
// le fichier compile et se charge partout ; les chemins d'exécution sont
// gardés par OperatingSystem.IsMacOS() côté appelants.
internal static class CoreAudioApi { constantes 'glob'/'inpt'/'outp', sélecteurs,
    structures (AudioObjectPropertyAddress, AudioStreamBasicDescription,
    AudioBufferList, AudioComponentDescription, AURenderCallbackStruct, …),
    DllImport CoreAudio/CoreFoundation/AudioToolbox/AudioUnit,
    helpers ReadProperty/ReadDeviceIds/ReadDeviceName/ReadDeviceUid/
    ReadChannelCount/CFStringToString/StringToCFString/DescribeError }

// Catalogue
internal static class CoreAudioCatalog
    EnumerateOutputDevices() / EnumerateInputDevices()      // flu = scope visé
    GetDefaultOutputDeviceId() / GetDefaultInputDeviceId()  // 0 si aucun
    Resolve(uid, scope) / FindRoomEqVirtual() / ToOutputInfos()

// Sortie / capture
sealed class CoreAudioOutput : IAudioOutput   // AUHAL, 48 kHz stéréo float entrelacé
sealed class CoreAudioCapture : IDisposable   // AUHAL entrée, 48 kHz float, TCC
    internal CoreAudioCapture(device, onChunk, channels = 1) ; Start/Stop

// Pont du périphérique virtuel
static class MacAudioBridge
    const string VirtualDeviceName;   // "RoomEq Virtual"
    const string PluginPath;          // /Library/Audio/Plug-Ins/HAL/RoomEqHAL.driver
    static AsioBridgeHost DeviceBridge { get; }
    static bool PluginInstalled { get; }
    static VirtualCardStatus Probe();
```

## Invariants

1. **Résolution paresseuse** : aucune liaison n'est résolue au chargement de
   l'assemblage (tests Linux/CI possibles) ; seuls les chemins macOS
   appellent réellement les frameworks.
2. **Catalogue** : un périphérique est listé dans un scope s'il possède au
   moins un flux dans cette direction ; l'UID stable prime sur le nom
   (fallback nom si vide) ; `Channels` replié à 2 si non lisible.
3. **Sortie AUHAL** : composant `auou/ahal/appl`, périphérique cible posé
   AVANT l'initialisation, format client 48 kHz float entrelacé,
   `MaximumFramesPerSlice` = 4096, rappel de rendu gardé vivant par champ
   (GC ne peut pas le collecter), tampons inattendus (désentrelacé imposé)
   → silence propre ; arrêt/libération tolérants et idempotents.
4. **Capture AUHAL** : entrée activée (élément 0), sortie neutralisée
   (élément 1), format 48 kHz float N canaux, `AudioUnitRender` tire vers un
   tampon non managé dédié puis copie vers le callback ; erreur de rendu →
   l'OS rejoue au cycle suivant (pas d'exception) ; message d'erreur
   dédié pour les permissions refusées (−10878).
5. **Pont** : `DeviceBridge` = `AsioBridgeHost` sur la section
   `Local\RoomEq.Device.Bridge.v1` (distincte de l'ASIO) ; `Probe()`
   considère le périphérique virtuel comme câble dont les deux côtés vont
   ensemble (boucle interne du greffon).
6. Le délégué de rendu et les tampons non managés sont TOUJOURS libérés
   (chemins d'exception compris) — aucune fuite au redémarrage de sortie.

## Critères de test

- Linux/CI : les types CoreAudio ne sont JAMAIS appelés (chemins gardés) ;
  le chargement de l'assemblage et la construction de `MacAudioBridge`
  (section distincte du pont ASIO, chemin du greffon, `PluginInstalled`
  faux) restent testables ;
- `DeviceBridge.MapName` ≠ `AsioBridgeHost.Instance.MapName` (portage
  legacy) ; constantes de chemin exactes.
