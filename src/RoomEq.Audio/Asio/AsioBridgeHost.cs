using System.IO.MemoryMappedFiles;
using System.Threading;
using NAudio.Wave;
using RoomEq.Audio.Routing;

namespace RoomEq.Audio.Asio;

/// <summary>
/// Accès multiplateforme à la section partagée : nom Windows
/// (Local\…) ou fichier temporaire de même contenu sous les plateformes
/// sans sections nommées (Linux/macOS) — ce qui permet d'exécuter les
/// tests du pont hors Windows.
/// </summary>
public static class AsioBridgeTransport
{
    public static MemoryMappedFile CreateOrOpen(string name, long size)
    {
        try
        {
            return MemoryMappedFile.CreateOrOpen(name, size);
        }
        catch (PlatformNotSupportedException)
        {
            string path = FallbackPath(name);
            var mmf = MemoryMappedFile.CreateFromFile(path, System.IO.FileMode.OpenOrCreate, null, size);
            MakeWorldWritable(path);
            return mmf;
        }
    }

    public static MemoryMappedFile OpenExisting(string name)
    {
        try
        {
            return MemoryMappedFile.OpenExisting(name);
        }
        catch (PlatformNotSupportedException)
        {
            // les sections « nommées » n'existent pas ici : la même donnée vit
            // dans le fichier de repli, ouvert par chemin (FileMode.Open).
            return MemoryMappedFile.CreateFromFile(FallbackPath(name), System.IO.FileMode.Open, null, 0);
        }
    }

    /// <summary>
    /// Chemin POSIX déterministe : « Local\RoomEq.Asio.Bridge.v1 » →
    /// « /tmp/roomeq.asio.bridge.v1.mmf ». La même règle est implémentée par le
    /// greffon macOS (RoomEqHAL) — ne pas changer sans mettre à jour roomeq_hal.c.
    /// </summary>
    public static string FallbackPath(string name)
    {
        int lastSlash = name.LastIndexOf('\\');
        string clean = (lastSlash >= 0 ? name[(lastSlash + 1)..] : name).ToLowerInvariant();
        // concaténation explicite : le chemin est CANONIQUE sur toutes les
        // plateformes (Path.Combine produirait « /tmp\… » sous Windows)
        return "/tmp/" + clean + ".mmf";
    }

    /// <summary>macOS/Linux : rend le fichier accessible en écriture à coreaudiod (autre utilisateur).</summary>
    private static void MakeWorldWritable(string path)
    {
        try
        {
            if (OperatingSystem.IsLinux() || OperatingSystem.IsMacOS())
                System.IO.File.SetUnixFileMode(path,
                    System.IO.UnixFileMode.UserRead | System.IO.UnixFileMode.UserWrite |
                    System.IO.UnixFileMode.GroupRead | System.IO.UnixFileMode.GroupWrite |
                    System.IO.UnixFileMode.OtherRead | System.IO.UnixFileMode.OtherWrite);
        }
        catch
        {
            // système de fichiers exotique : le greffon peut recréer le fichier lui-même
        }
    }
}

/// <summary>
/// Hôte du pont ASIO RoomEq : crée (ou ouvre) la section partagée
/// <c>Local\RoomEq.Asio.Bridge.v1</c>, pompe l'audio écrit par la DLL ASIO
/// (chargée dans le DAW) vers un tampon interne sans verrou, et l'expose
/// comme source de piste du mixeur (« DAW (ASIO) »). Le thread du mixeur
/// tire les échantillons à son rythme (horloge de la vraie carte son) ;
/// l'horloge hôte publiée dans l'en-tête sert à la DLL à recaler sa cadence.
/// </summary>
public sealed class AsioBridgeHost : IDisposable
{
    /// <summary>Instance partagée de l'application (une seule par session).</summary>
    public static AsioBridgeHost Instance { get; } = new();

    private readonly object _stateLock = new();
    private MemoryMappedFile? _mmf;
    private MemoryMappedViewAccessor? _view;
    private EventWaitHandle? _dataEvent;
    private Thread? _reader;
    private CancellationTokenSource? _cts;

    private readonly RingBuffer _buffer = new(AsioBridgeInterop.RingFrames * AsioBridgeInterop.Channels);

    private int _session;              // session courante vue côté hôte
    private long _pulledTotal;         // échantillons tirés du pont (interne)
    private long _consumedTotal;       // échantillons consommés par le mixeur

    // libellés d'état (DAW côté ASIO Windows ; périphérique virtuel côté macOS)
    private readonly string _idleText = "Pont ASIO à l'arrêt.";
    private readonly string _connectedText = "DAW connecté (48 kHz)";
    private readonly string _waitingText = "En attente d'un DAW (choisissez « RoomEq ASIO » dans son logiciel audio)";
    private string? _idleTextOverride;
    private string? _connectedTextOverride;
    private string? _waitingTextOverride;

    /// <summary>Fournisseur stéréo 48 kHz exposé au mixeur (pull).</summary>
    public ISampleProvider Provider { get; }

    private AsioBridgeHost()
    {
        MapName = AsioBridgeInterop.MapName;
        EventName = AsioBridgeInterop.EventName;
        Provider = new BridgeProvider(this);
    }

    /// <summary>Pour les tests : construit un hôte pointant une section dédiée.</summary>
    /// <remarks>Réflexion dans les tests : constructeur privé, jamais appelé hors assembly.</remarks>
    private AsioBridgeHost(string mapName, string eventName)
    {
        MapName = mapName;
        EventName = eventName;
        Provider = new BridgeProvider(this);
    }

    /// <summary>Point d'entrée des tests : hôte isolé sur des noms dédiés.</summary>
    internal static AsioBridgeHost ForTests(string mapName)
        => new(mapName, mapName + ".Event");

    /// <summary>
    /// Hôte du pont du PÉRIPHÉRIQUE VIRTUEL macOS : le greffon système RoomEqHAL
    /// (chargé par coreaudiod) y écrit ce que macOS joue dans « RoomEq Virtual ».
    /// Même protocole, autre section — un DAW ASIO et le périphérique ne se
    /// marchent pas dessus.
    /// </summary>
    public static AsioBridgeHost CreateDeviceBridge() => new(
        AsioBridgeInterop.DeviceBridgeMapName, AsioBridgeInterop.DeviceBridgeEventName)
    {
        _idleTextOverride = "Pont du périphérique virtuel à l'arrêt.",
        _connectedTextOverride = "Périphérique virtuel actif (48 kHz)",
        _waitingTextOverride = "En attente du périphérique virtuel — jouez du son sur « RoomEq Virtual » (sortie macOS)",
    };

    public string MapName { get; }
    public string EventName { get; }

    public bool IsHosting { get; private set; }

    /// <summary>Le DAW (DLL ASIO) est-il attaché et en cours de lecture ?</summary>
    public bool ClientConnected
    {
        get
        {
            var v = _view;
            if (v == null) return false;
            uint active = v.ReadUInt32(AsioBridgeHeader.Offsets.ClientActive);
            return active != 0;
        }
    }

    /// <summary>Texte d'état pour l'interface (rafraîchi par le ViewModel).</summary>
    public string StatusText
    {
        get
        {
            if (!IsHosting) return _idleTextOverride ?? _idleText;
            var v = _view;
            if (v == null) return "Pont : mémoire partagée indisponible.";
            bool client = v.ReadUInt32(AsioBridgeHeader.Offsets.ClientActive) != 0;
            long dropped = v.ReadInt64(AsioBridgeHeader.Offsets.DroppedFrames);
            double backlogMs = _buffer.Available * 1000.0
                / (AsioBridgeInterop.BridgeRate * AsioBridgeInterop.Channels);
            return client
                ? $"{_connectedTextOverride ?? _connectedText} — {backlogMs:0} ms en attente"
                  + (dropped > 0 ? $", {dropped} éch. écartées" : "")
                : _waitingTextOverride ?? _waitingText;
        }
    }

    // ================================================================ cycle de vie

    /// <summary>Ouvre (ou crée) la section et démarre le lecteur.</summary>
    public void Start()
    {
        lock (_stateLock)
        {
            if (IsHosting) return;

            long size = AsioBridgeInterop.MappingBytes;
            _mmf = AsioBridgeTransport.CreateOrOpen(MapName, size);
            _view = _mmf.CreateViewAccessor(0, size);
            try
            {
                // Windows : événement nommé signalé par la DLL à chaque bloc.
                // Autres plateformes (tests) : absent, le lecteur interroge.
                _dataEvent = new EventWaitHandle(false, EventResetMode.AutoReset, EventName);
            }
            catch (PlatformNotSupportedException)
            {
                _dataEvent = null;
            }

            // en-tête : initialisation si vierge, sinon simple réveil de session.
            if (_view.ReadUInt32(AsioBridgeHeader.Offsets.Magic) != AsioBridgeInterop.Magic)
            {
                var h = new AsioBridgeHeader
                {
                    Magic = AsioBridgeInterop.Magic,
                    Version = AsioBridgeInterop.Version,
                    SampleRate = AsioBridgeInterop.BridgeRate,
                    BufferFrames = 256,
                    RingFrames = AsioBridgeInterop.RingFrames,
                    Channels = AsioBridgeInterop.Channels,
                    Session = 1,
                };
                _view.Write(AsioBridgeHeader.Offsets.Magic, h.Magic);
                _view.Write(AsioBridgeHeader.Offsets.Version, h.Version);
                _view.Write(AsioBridgeHeader.Offsets.SampleRate, h.SampleRate);
                _view.Write(AsioBridgeHeader.Offsets.BufferFrames, h.BufferFrames);
                _view.Write(AsioBridgeHeader.Offsets.RingFrames, h.RingFrames);
                _view.Write(AsioBridgeHeader.Offsets.Channels, h.Channels);
                _view.Write(AsioBridgeHeader.Offsets.Session, h.Session);
            }
            _session = (int)_view.ReadUInt32(AsioBridgeHeader.Offsets.Session);
            _view.Write(AsioBridgeHeader.Offsets.HostActive, 1u);
            _view.Write(AsioBridgeHeader.Offsets.HostSampleRate, (uint)AsioBridgeInterop.BridgeRate);

            _cts = new CancellationTokenSource();
            var token = _cts.Token;
            _reader = new Thread(() => ReaderLoop(token))
            {
                IsBackground = true,
                Name = "RoomEq.AsioBridge.Reader",
                Priority = ThreadPriority.AboveNormal,
            };
            _reader.Start();
            IsHosting = true;
        }
    }

    /// <summary>Arrête le lecteur et libère la section.</summary>
    public void Stop()
    {
        lock (_stateLock)
        {
            if (!IsHosting) return;
            try
            {
                _view?.Write(AsioBridgeHeader.Offsets.HostActive, 0u);
            }
            catch { /* la section peut déjà être fermée */ }
            _cts?.Cancel();
            try { _dataEvent?.Set(); } catch { /* déjà libéré */ }
            _reader?.Join(1500);
            _cts?.Dispose();
            _view?.Dispose();
            _mmf?.Dispose();
            _dataEvent?.Dispose();
            _cts = null; _reader = null; _view = null; _mmf = null; _dataEvent = null;
            IsHosting = false;
        }
    }

    public void Dispose() => Stop();

    /// <summary>
    /// Réveille immédiatement le lecteur du pont (diagnostic, tests) —
    /// inutile en production : la DLL signale l'événement elle-même.
    /// </summary>
    public void PokeReader() => _dataEvent?.Set();

    // ================================================================ lecteur

    private void ReaderLoop(CancellationToken token)
    {
        var scratch = new float[4096];
        while (!token.IsCancellationRequested)
        {
            try
            {
                // réveil sur événement DLL (nouveau bloc) ou battement de sécurité
                if (_dataEvent != null) _dataEvent.WaitOne(50);
                else Thread.Sleep(20);

                var v = _view;
                if (v == null) return;

                // changement de session (redémarrage DAW, changement de réglage) :
                // on saute l'historique pour repartir « maintenant ».
                int session = (int)v.ReadUInt32(AsioBridgeHeader.Offsets.Session);
                if (session != _session)
                {
                    _session = session;
                    long w = v.ReadInt64(AsioBridgeHeader.Offsets.WritePos);
                    v.Write(AsioBridgeHeader.Offsets.ReadPos, w);
                    DiscardBuffered();
                }

                long writePos = v.ReadInt64(AsioBridgeHeader.Offsets.WritePos);
                long readPos = v.ReadInt64(AsioBridgeHeader.Offsets.ReadPos);
                long avail = writePos - readPos;
                if (avail <= 0)
                {
                    continue;
                }
                if (avail > AsioBridgeInterop.RingFrames)   // lecteur en retard : saute
                {
                    readPos = writePos - AsioBridgeInterop.RingFrames;
                    avail = AsioBridgeInterop.RingFrames;
                }

                long done = 0;
                while (done < avail && !token.IsCancellationRequested)
                {
                    int take = (int)Math.Min(scratch.Length / 2, avail - done);
                    long frame = readPos + done;
                    int offsetInRing = (int)(frame & (AsioBridgeInterop.RingFrames - 1));
                    // segment contigu dans l'anneau (peut couvrir la couture)
                    int contiguous = Math.Min(take, AsioBridgeInterop.RingFrames - offsetInRing);
                    long byteOff = AsioBridgeInterop.HeaderBytes
                        + (long)offsetInRing * AsioBridgeInterop.Channels * sizeof(float);
                    v.ReadArray((int)byteOff, scratch, 0, contiguous * AsioBridgeInterop.Channels);
                    _buffer.Write(scratch.AsSpan(0, contiguous * AsioBridgeInterop.Channels));
                    done += contiguous;
                }
                Interlocked.Add(ref _pulledTotal, done * AsioBridgeInterop.Channels);
                v.Write(AsioBridgeHeader.Offsets.ReadPos, readPos + done);
            }
            catch (ObjectDisposedException)
            {
                return;
            }
            catch
            {
                // jamais d'exception du thread de fond vers l'UI : petit somme et on retente
                try { Thread.Sleep(100); } catch { return; }
            }
        }
    }

    private void DiscardBuffered()
    {
        // vide le tampon interne : le consommateur verra du silence jusqu'à ce
        // que de nouvelles trames arrivent du DAW
        _buffer.Clear();
    }

    // ================================================================ consommateur (mixeur)

    private int ReadBridge(float[] buffer, int offset, int count)
    {
        int got = _buffer.Read(buffer.AsSpan(offset, count));
        if (got < count)
        {
            Array.Clear(buffer, offset + got, count - got);   // silence en cas de disette
        }
        Interlocked.Add(ref _consumedTotal, count);

        // publication de l'horloge hôte : la DLL recadre sa cadence dessus
        var v = _view;
        if (v != null)
        {
            try
            {
                v.Write(AsioBridgeHeader.Offsets.HostClock, Volatile.Read(ref _consumedTotal) / AsioBridgeInterop.Channels);
                v.Write(AsioBridgeHeader.Offsets.HostBeat, Volatile.Read(ref _consumedTotal) / 256);
            }
            catch { /* section fermée : le mixeur ne doit jamais lever */ }
        }
        return count;
    }

    /// <summary>Source stéréo 48 kHz tirée par le bus master (via le mixeur).</summary>
    private sealed class BridgeProvider(AsioBridgeHost host) : ISampleProvider
    {
        public WaveFormat WaveFormat { get; } =
            WaveFormat.CreateIeeeFloatWaveFormat(AsioBridgeInterop.BridgeRate, AsioBridgeInterop.Channels);

        public int Read(float[] buffer, int offset, int count) =>
            host.ReadBridge(buffer, offset, count);
    }
}
