using Avalonia.Media;
using RoomEq.App.Controls;
using RoomEq.Core.Dsp;

namespace RoomEq.App.ViewModels;

/// <summary>
/// Suivi live du micro pendant la calibration (extrait de
/// CalibrationViewModel en v3) : tampon circulaire alimenté par le flux de
/// capture, spectre Welch publié à chaque battement de la minuterie UI —
/// spec I-APP-02. La courbe est CENTRÉE sur sa moyenne : le niveau absolu
/// dépend du gain micro (souvent sous -40 dBFS, hors fenêtre du graphe) —
/// seule la forme de la réponse compte pour suivre la mesure en direct.
/// </summary>
public sealed class LiveMicMonitor
{
    private readonly RollingBuffer _buffer = new(2);

    /// <summary>Spectre live (sixièmes d'octave, centré sur la moyenne).</summary>
    public event Action<List<PlotSeries>>? SeriesChanged;

    public void AddChunk(float[] chunk) => _buffer.Add(chunk);

    public void Clear() => _buffer.Clear();

    /// <summary>
    /// Un battement de la minuterie : publie la courbe live si assez de données
    /// sont disponibles (≥ 8192 échantillons, ≥ 4 bandes), ne fait rien sinon.
    /// </summary>
    public void Tick()
    {
        var data = _buffer.Snapshot(_buffer.Capacity);
        if (data.Length < 8192) return;
        var db = SpectrumTools.WelchDb(data, _buffer.SampleRate, 4096, out var freqs);
        SpectrumTools.OctaveLevels(freqs, db, 25, 20000, SpectrumTools.SixthOctave,
            out var centers, out var levels);
        if (centers.Length < 4) return;

        double mean = SpectrumTools.MeanDb(centers, levels, 200, 8000);
        var pts = centers.Zip(levels, (f, l) => new CurvePoint(f, l - mean)).ToList();
        SeriesChanged?.Invoke(new List<PlotSeries>
        {
            PlotSeries.Create("Micro (live, centré)", pts, Color.Parse("#22D3EE"), fill: true)
        });
    }
}
