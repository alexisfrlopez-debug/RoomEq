# I-AUD-03 — Routage / table de mixage (câble virtuel)

- **Module** : M-AUD-03 · **Version de spec** : 1 · **Code** : `src/RoomEq.Audio/Routing/` —
  `RingBuffer.cs`, `TrackSource.cs`, `AudioDeviceCatalog.cs`, `VirtualAudioProviders.cs`,
  `MixerStore.cs`, `VolumeSyncPolicy.cs`, `EndpointVolumeController.cs`,
  `DefaultDeviceSetter.cs`, `AudioDeviceWatcher.cs`, `DriverSetupManager.cs`,
  `ConsoleChannel.cs`, `ConsoleMixer.cs`

## Intention

Router tout le son du système à travers l'égaliseur : le périphérique virtuel
RoomEq (pilote scellé N-DRV-01) ou une boucle WASAPI alimente des tranches de
mixage ; chaque tranche a son EQ, fader, pan, Mute/Solo ; le bus master
(EQ master + fader) rend sur la sortie physique choisie. Inclut la
persistance de la console, la politique de volume Windows, la bascule du
périphérique par défaut, la surveillance à chaud et l'installation du pilote.

## Contrat public (extraits)

```csharp
// RingBuffer : SPSC sans verrou, positions monotones 64 bits
sealed class RingBuffer(int capacitySamples)
    long Available { get; }  long Dropped { get; }
    int Write(ReadOnlySpan<float>);  int Read(Span<float>);  void Clear();

// Sources de piste
enum TrackSourceKind { Capture, Loopback, Player, Generator, AsioBridge, MacVirtualDevice }
record TrackSource { Kind, DeviceId, Name } + fabriques (CaptureDevice, LoopbackDevice, …)

// Catalogue WASAPI + sonde carte virtuelle
sealed class AudioDeviceCatalog
    EnumerateCaptureSources() / EnumerateLoopbackSources() / EnumerateRenderDevices()
    ResolveDevice(TrackSource) / ProbeVirtualCard(provider?) / GetDefaultCaptureId()
    static IsVirtualRender(name) / IsVirtualCapture(name)
sealed class VirtualCardStatus { Installed, FullyRouted, PartiallyInstalled, … }

// Fournisseurs d'infrastructure virtuelle
interface IVirtualAudioProvider { Id, DisplayName, Kind, IsPreinstalled,
    Installer, RenderNamePatterns, CaptureNamePatterns, GuideLines,
    IsRenderEndpoint(name), IsCaptureEndpoint(name) }
static class VirtualAudioCatalog { All, Default, GetById(id),
    IsVirtualRender(name), IsVirtualCapture(name) }
record VirtualInstallerSpec { DriverFolder, InfFileName, HardwareId, ServiceName, PackagePresent }

// Persistance : %APPDATA%/RoomEq/mixer.json (StoredMixer / StoredChannel)

// Politique de volume (PURE)
static class VolumeSyncPolicy
    const double ChangeEpsilon;   // 0,0005
    const double WriteEpsilon;    // 0,0035
    IsUsable(v) / HasChangedSinceLastSeen(last, cur) / ShouldWriteToEndpoint(cur, target)
    ShouldSyncTopBar(topBar, endpoint) / ClampToEndpointRange(v)

// Volume de la carte physique
sealed class EndpointVolumeController : IDisposable
    static double SliderToEndpoint(double sliderVolume);   // clamp 0..1
    SetTarget(outputId?) / VolumeScalar / Muted / SetVolumeScalar(v) / ToggleMute()

// Périphérique par défaut (PolicyConfig, 3 IID candidates + confirmation relecture)
static class DefaultDeviceSetter
    SetDefaultRender(deviceId) / SetDefaultCapture(deviceId)

// Surveillance à chaud (anti-rebond 1 s)
sealed class AudioDeviceWatcher : IDisposable { IsWatching, event DevicesChanged }

// Installation du pilote (pur pour les parties analysables)
sealed class DriverSetupManager
    static FindInfFile / PackagePresent / ServiceInstalled / BuildInstallArguments /
           ParseDriverStoreName / BuildUninstallArguments / FindBundledSetupExe
    Task<InstallResult> InstallAsync(...) / UninstallAsync(...)

// Console
sealed class ConsoleChannel : IDisposable { Source, Eq, Fader, Pan, Mute, Solo,
    PeakL/PeakR, PlayerFinished, GeneratorEnabled, SetGeneratorSignal(…),
    Prepare(masterRate), StartCapture(catalog), StopCapture(), LoadFile(path),
    UnloadFile(), ReadInto(accum, offset, frames, audible) }
sealed class ConsoleMixer : IDisposable { MasterEq, MasterFader, MasterMute,
    MasterPeakL/R, OutputLatencyMs, AddChannel(source), RemoveChannel(channel),
    SetOutput(id), Start(), Stop(), EnumerateOutputs() }
```

## Invariants

1. **RingBuffer** : capacité ≥ 256 ; saturation écarte les PLUS ANCIENS
   (latence bornée) et compte `Dropped` ; écrit plus grande que la capacité →
   seule la fin est conservée ; lecture comble le silence ; positions
   monotones (pas d'ambiguïté vide/plein) ; aucun verrou ni allocation au
   régime établi.
2. **Anti-larsen** (avertissement d'ordre SÉCURITÉ) : `ConsoleMixer.Start`
   échoue si (a) une piste loopback capture le périphérique de sortie master,
   (b) le master part vers un câble virtuel (render reconnu par motifs), sur
   Windows comme macOS — messages actionnables.
3. **Bus master** : somme des tranches audibles (`!Mute && (!anySolo || Solo)`
   × SourceReady), EQ master, gain = MasterFader × 10^(PreampDb/20), Mute → 0 ;
   crêtes post-fader publiées via `Volatile.Write` ; sortie multi-canaux :
   L→ch0, R→ch1, reste à zéro (mono : (L+R)/2).
4. **Pan** : loi à puissance constante, angle = (pan+1)·π/4 → (cos, sin) ;
   centre = −3 dB/voie, extrêmes = (1,0)/(0,1).
5. **Tranche** : la chaîne `source → resample → stéréo → EQ` est
   reconstructible à chaud (`Prepare`) sans vider le tampon circulaire ;
   retrait pendant que le bus tourne → disposition DIFFÉRÉE (≥ 1 s) car le
   thread audio peut encore lire l'ancien instantané — jamais de dispose en
   plein vol.
6. **Instantanés** : le bus lit un tableau figé (`_snapshot`) publié par
   affectation atomique de référence sous verrou ; ajout/retrait ne bloquent
   pas le thread audio.
7. **VolumeSyncPolicy** : la sentinelle −1 (carte illisible) n'est JAMAIS
   écrite dans la carte ni affichée ; les epsilons séparent « vrai
   changement » (5e-4) et « écrit utile » (3,5e-3) — les tests legacy font
   foi des valeurs exactes.
8. **DriverSetupManager** : détection paquet (dossier `driver/`, à côté de
   l'exe, `driver*`), arguments pnputil quotés, analyse du magasin tolérante
   aux libellés EN/FR et mojibake CP850 (clés `publi`/`orig`), recherche
   `RoomEqDriverSetup*.exe` ; échec = `InstallResult(false, message)`, jamais
   d'exception.
9. **DefaultDeviceSetter** : 3 IID candidates dans l'ordre (classique Win7→11,
   récente, historique) ; chaque tentative confirmée par RELECTURE du
   périphérique par défaut ; les trois rôles (Console/Multimedia/
   Communications) sont posés.
10. **Watcher** : constructeur inoffensif hors Windows (inerte) ; rafales
    COM → UN événement après anti-rebond 1 s ; jamais d'exception à la
    construction ni à la libération.
11. **MixerStore** : JSON indenté, null ignorés ; fichier absent/corrompu →
    configuration par défaut (jamais d'exception).
12. Les énumérations WASAPI renvoient des listes VIDES (et non des
    exceptions) quand le sous-système audio manque — Linux/CI compris.

## Critères de test

- RingBuffer : aller-retour, silence sur vide, continuité à la couture
  (capacité non alignée), saturation (drops + bornage), Clear (portage des
  6 tests legacy) ;
- ConsoleChannel : générateur produit du signal, fader ½ → RMS ½, inaudible
  → silence + état EQ conservé, loi de pan (centre/extrêmes), pan tout à
  droite → énergie gauche nulle, EQ de piste passe-haut atténue les graves ;
- MixerStore : aller-retour complet (pistes, EQ, faders), défauts sans
  fichier ;
- VolumeSyncPolicy + SliderToEndpoint : tableaux de cas exacts (portage
  legacy) ;
- DriverSetupManager : arguments, analyse pnputil (11 cas EN/FR/mojibake,
  portage legacy), détection paquet/setup, catalogue fournisseurs (2
  fournisseurs, ids héritées → défaut, motifs distincts, guides sans tiers) ;
- Watcher : inertie hors Windows ;
- ConsoleMixer : anti-larsen (sortie master = câble virtuel → refus),
  verdict Mute/Solo, énumération sans exception sous Linux.
