namespace RoomEq.Audio.Routing;

/// <summary>
/// Tampon circulaire flottant producteur unique / consommateur unique, sans allocation
/// et sans verrou : écrit depuis le callback de capture WASAPI, lu depuis le thread
/// du mixeur. En cas de saturation, les données les plus anciennes sont écartées afin
/// de borner la latence (le comportement attendu pour un routage temps réel).
/// Les positions sont monotones (entiers 64 bits) : pas d'ambiguïté vide/plein.
/// </summary>
public sealed class RingBuffer
{
    private readonly float[] _buf;
    private readonly int _capacity;
    private long _writePos; // échantillons cumulés écrits
    private long _readPos;  // échantillons cumulés lus

    /// <param name="capacity">Capacité en échantillons (toutes voies confondues).</param>
    public RingBuffer(int capacity)
    {
        if (capacity < 256) capacity = 256;
        _capacity = capacity;
        _buf = new float[capacity];
    }

    public int Capacity => _capacity;

    /// <summary>Nombre d'échantillons disponibles à la lecture.</summary>
    public long Available => Volatile.Read(ref _writePos) - Volatile.Read(ref _readPos);

    /// <summary>Diagnostic : échantillons écartés par saturation depuis le départ.</summary>
    public long Dropped { get; private set; }

    /// <summary>
    /// Écrit les données (thread de capture). Si le tampon déborde, avance la position
    /// de lecture (écarte le plus ancien) pour conserver la place nécessaire.
    /// Retourne le nombre d'échantillons réellement écrits.
    /// </summary>
    public int Write(ReadOnlySpan<float> data)
    {
        int count = data.Length;
        if (count <= 0) return 0;

        // écriture plus grande que la capacité : on ne conserve que la fin du flux
        // (les échantillons excédentaires sont comptés comme écartés)
        if (count > _capacity)
        {
            Dropped += count - _capacity;
            data = data[^_capacity..];
            count = _capacity;
        }

        long avail = Volatile.Read(ref _writePos) - Volatile.Read(ref _readPos);
        long free = _capacity - avail;
        if (count > free)
        {
            long toDrop = count - free;
            Volatile.Write(ref _readPos, Volatile.Read(ref _readPos) + toDrop);
            Dropped += toDrop;
        }

        long w = Volatile.Read(ref _writePos);
        int idx = (int)(w % _capacity);
        int first = Math.Min(count, _capacity - idx);
        data[..first].CopyTo(_buf.AsSpan(idx));
        if (count > first)
            data[first..].CopyTo(_buf.AsSpan(0, count - first));

        Volatile.Write(ref _writePos, w + count);
        return count;
    }

    /// <summary>
    /// Lit au plus count échantillons (thread mixeur) et comble le reste par du silence.
    /// Retourne le nombre d'échantillons réellement disponibles (le surplus est du silence).
    /// </summary>
    public int Read(Span<float> data)
    {
        long avail = Volatile.Read(ref _writePos) - Volatile.Read(ref _readPos);
        int count = (int)Math.Min(avail, data.Length);
        if (count > 0)
        {
            long r = Volatile.Read(ref _readPos);
            int idx = (int)(r % _capacity);
            int first = Math.Min(count, _capacity - idx);
            _buf.AsSpan(idx, first).CopyTo(data);
            if (count > first)
                _buf.AsSpan(0, count - first).CopyTo(data[first..]);
            Volatile.Write(ref _readPos, r + count);
        }
        if (count < data.Length)
            data[count..].Clear();
        return count;
    }

    /// <summary>Vide le tampon (départ / arrêt de piste).</summary>
    public void Clear()
    {
        Volatile.Write(ref _readPos, Volatile.Read(ref _writePos));
        Dropped = 0;
    }
}
