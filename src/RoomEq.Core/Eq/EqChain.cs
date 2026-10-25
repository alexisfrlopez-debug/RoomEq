using RoomEq.Core.Dsp;

namespace RoomEq.Core.Eq;

/// <summary>
/// Chaîne d'égalisation temps réel (spec I-EQ-02) : N canaux entrelacés
/// traversent chacun la liste des filtres actifs. Les reconfigurations
/// (preset, édition de bande) publient un nouveau tableau de filtres par
/// affectation de référence atomique : <see cref="Process"/> ne prend jamais de
/// lock ni n'alloue, l'ancien tableau reste valide pour l'appel en cours.
/// La préamplification est appliquée en tête de chaîne (gain linéaire).
/// </summary>
public sealed class EqChain
{
    private BiquadFilter[][] _channelFilters = Array.Empty<BiquadFilter[]>();
    private readonly object _sync = new();
    private EqPreset _preset = EqPreset.Flat();
    private bool _bypass;
    private double _preampGain = 1.0;
    private readonly BiquadFilter _scratch = new(); // filtre de calcul pour ResponseDb

    public double SampleRate { get; private set; } = 48000.0;
    public int Channels { get; private set; } = 2;

    /// <summary>
    /// Déclenché après chaque modification (preset, bande, préampli, bypass) —
    /// hors lock. Le handler ne doit pas réentrer dans la chaîne émettrice.
    /// </summary>
    public event Action? Changed;

    public bool Bypass
    {
        get => _bypass;
        set
        {
            if (_bypass == value)
                return;
            _bypass = value;
            Changed?.Invoke();
        }
    }

    /// <summary>Le preset courant (copie défensive ; éditer via UpdateBand/SetPreamp).</summary>
    public EqPreset Preset
    {
        get { lock (_sync) return _preset.Clone(); }
    }

    public double PreampDb
    {
        get { lock (_sync) return _preset.PreampDb; }
    }

    /// <summary>Définit le format de travail. Reconstruit les filtres si le format change
    /// (tolérance fs ±0,5 Hz), matériallise le preset courant à la première configuration.</summary>
    public void Configure(double sampleRate, int channels)
    {
        lock (_sync)
        {
            bool rebuild = channels != Channels
                           || Math.Abs(sampleRate - SampleRate) > 0.5
                           || _channelFilters.Length == 0;
            SampleRate = sampleRate;
            Channels = channels;
            if (rebuild)
                RebuildLocked();
        }
    }

    /// <summary>Remplace le preset actif (copie défensive).</summary>
    public void SetPreset(EqPreset preset)
    {
        ArgumentNullException.ThrowIfNull(preset);
        lock (_sync)
        {
            _preset = preset.Clone();
            RebuildLocked();
        }
        Changed?.Invoke();
    }

    /// <summary>Met à jour une bande (typiquement : glisser une poignée dans l'UI).
    /// Index invalide = no-op silencieux (robustesse UI).</summary>
    public void UpdateBand(int index, EqBand band)
    {
        ArgumentNullException.ThrowIfNull(band);
        lock (_sync)
        {
            if (index < 0 || index >= _preset.Bands.Count)
                return;
            _preset.Bands[index] = band.Clone();
            RebuildLocked();
        }
        Changed?.Invoke();
    }

    /// <summary>Met à jour la préamplification.</summary>
    public void SetPreamp(double preampDb)
    {
        lock (_sync)
        {
            _preset.PreampDb = preampDb;
            _preampGain = Math.Pow(10.0, preampDb / 20.0);
        }
        Changed?.Invoke();
    }

    private void RebuildLocked()
    {
        int ch = Math.Max(1, Channels);
        _preampGain = Math.Pow(10.0, _preset.PreampDb / 20.0);
        var active = _preset.Bands.Where(b => b.Enabled).ToList();
        var newFilters = new BiquadFilter[ch][];
        for (int c = 0; c < ch; c++)
        {
            var col = new BiquadFilter[active.Count];
            for (int i = 0; i < active.Count; i++)
            {
                var b = active[i];
                col[i] = new BiquadFilter(b.Type, SampleRate, b.Frequency, b.GainDb, b.Q);
            }
            newFilters[c] = col;
        }
        _channelFilters = newFilters; // publication par référence atomique
    }

    /// <summary>
    /// Traite un buffer entrelacé en place — thread audio : aucun lock, aucune
    /// allocation. La préamplification est appliquée en tête, puis les filtres
    /// actifs de chaque canal.
    /// </summary>
    public void Process(float[] buffer, int offset, int count)
    {
        ArgumentNullException.ThrowIfNull(buffer);
        if (_bypass)
            return;
        var filters = _channelFilters; // lecture de référence sans lock
        int ch = filters.Length;
        if (ch == 0)
            return;

        int frames = count / ch;
        double preamp = _preampGain;
        bool hasPreamp = Math.Abs(_preset.PreampDb) > 1e-9;
        for (int f = 0; f < frames; f++)
        {
            int i0 = offset + f * ch;
            for (int c = 0; c < ch; c++)
            {
                var col = filters[c];
                double x = hasPreamp ? buffer[i0 + c] * preamp : buffer[i0 + c];
                for (int b = 0; b < col.Length; b++)
                    x = col[b].Process(x);
                buffer[i0 + c] = (float)x;
            }
        }
    }

    /// <summary>Réponse composite de l'égaliseur en dB à la fréquence donnée
    /// (préampli inclus), pour l'affichage.</summary>
    public double ResponseDb(double freq)
    {
        lock (_sync)
        {
            double db = _preset.PreampDb;
            foreach (var b in _preset.Bands)
            {
                if (!b.Enabled)
                    continue;
                _scratch.Configure(b.Type, SampleRate, b.Frequency, b.GainDb, b.Q);
                db += _scratch.ResponseDb(freq);
            }
            return db;
        }
    }

    /// <summary>Réponse composite échantillonnée sur des points log-répartis
    /// (bornes incluses, fréquences strictement croissantes).</summary>
    public List<CurvePoint> ResponseCurve(double fMin, double fMax, int pointCount)
    {
        if (pointCount < 2)
            throw new ArgumentOutOfRangeException(nameof(pointCount), "Au moins deux points sont requis.");
        if (fMin <= 0 || fMax <= fMin)
            throw new ArgumentException("Il faut 0 < fMin < fMax.");
        var pts = new List<CurvePoint>(pointCount);
        double logMin = Math.Log10(fMin), logMax = Math.Log10(fMax);
        for (int i = 0; i < pointCount; i++)
        {
            double f = Math.Pow(10.0, logMin + (logMax - logMin) * i / (pointCount - 1));
            pts.Add(new CurvePoint(f, ResponseDb(f)));
        }
        return pts;
    }
}
