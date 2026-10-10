using RoomEq.Core.Dsp;

namespace RoomEq.Core.Eq;

/// <summary>
/// Chaîne d'égalisation temps réel : N canaux, chaque canal traverse la liste des filtres actifs.
/// Les reconfigurations (preset, édition de bande) échangent atomiquement un tableau de filtres
/// reconstruit, afin de ne jamais bloquer le thread audio.
/// </summary>
public sealed class EqChain
{
    private BiquadFilter[][] _channelFilters = Array.Empty<BiquadFilter[]>();
    private readonly object _sync = new();
    private EqPreset _preset = EqPreset.Flat();
    private bool _bypass;
    private readonly BiquadFilter _scratch = new(); // filtre de calcul pour ResponseDb (aucune allocation)

    public double SampleRate { get; private set; } = 48000.0;
    public int Channels { get; private set; } = 2;

    /// <summary>
    /// Déclenché après chaque modification (preset, bande, préampli, bypass).
    /// Permet à un observateur (ex. bus master de la table de mixage) de rester
    /// synchronisé avec cette chaîne en temps réel. Le handler ne doit pas re-entrer
    /// dans la chaîne émettrice.
    /// </summary>
    public event Action? Changed;

    public bool Bypass
    {
        get => _bypass;
        set
        {
            if (_bypass == value) return;
            _bypass = value;
            Changed?.Invoke();
        }
    }

    /// <summary>Le preset courant (copie défensive à l'affectation ; éditer via UpdateBand / SetPreamp).</summary>
    public EqPreset Preset
    {
        get { lock (_sync) return _preset.Clone(); }
    }

    /// <summary>Définit le format de travail. Reconstruit les filtres si nécessaire.</summary>
    public void Configure(double sampleRate, int channels)
    {
        lock (_sync)
        {
            bool rebuild = channels != Channels
                           || Math.Abs(sampleRate - SampleRate) > 0.5
                           || _channelFilters.Length == 0;
            SampleRate = sampleRate;
            Channels = channels;
            if (rebuild) RebuildLocked();
        }
    }

    /// <summary>Remplace le preset actif (copie défensive).</summary>
    public void SetPreset(EqPreset preset)
    {
        lock (_sync)
        {
            _preset = preset.Clone();
            RebuildLocked();
        }
        Changed?.Invoke();
    }

    /// <summary>Met à jour une bande (typiquement : glisser une poignée dans l'UI).</summary>
    public void UpdateBand(int index, EqBand band)
    {
        lock (_sync)
        {
            if (index < 0 || index >= _preset.Bands.Count) return;
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
        }
        Changed?.Invoke();
    }

    public double PreampDb { get { lock (_sync) return _preset.PreampDb; } }

    private void RebuildLocked()
    {
        int ch = Math.Max(1, Channels);
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
        _channelFilters = newFilters; // affectation atomique (référence)
    }

    /// <summary>
    /// Traite un buffer entrelacé en place. Appelé sur le thread audio : aucune allocation, aucun lock.
    /// </summary>
    public void Process(float[] buffer, int offset, int count)
    {
        if (_bypass) return;
        var filters = _channelFilters; // lecture de référence sans lock
        int ch = filters.Length;
        if (ch == 0) return;

        int frames = count / ch;
        for (int f = 0; f < frames; f++)
        {
            int i0 = offset + f * ch;
            for (int c = 0; c < ch; c++)
            {
                var col = filters[c];
                double x = buffer[i0 + c];
                for (int b = 0; b < col.Length; b++)
                    x = col[b].ProcessSample(x);
                buffer[i0 + c] = (float)x;
            }
        }
    }

    /// <summary>Réponse composite de l'égaliseur en dB à la fréquence donnée (pour l'affichage).</summary>
    public double ResponseDb(double freq)
    {
        lock (_sync)
        {
            double db = 0;
            foreach (var b in _preset.Bands)
            {
                if (!b.Enabled) continue;
                _scratch.Configure(b.Type, SampleRate, b.Frequency, b.GainDb, b.Q);
                db += _scratch.ResponseDb(freq, SampleRate);
            }
            return db;
        }
    }

    /// <summary>Réponse composite échantillonnée sur des points log-répartis.</summary>
    public List<CurvePoint> ResponseCurve(double fMin, double fMax, int pointCount)
    {
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
