using System.Numerics;
using RoomEq.Core.Calibration;
using RoomEq.Core.Dsp;
using RoomEq.Core.Io;
using Xunit;

namespace RoomEq.Core.Tests;

/// <summary>
/// Régressions « la courbe de calibration ne s'affiche pas » : niveaux de segments
/// (détection de capture muette), diagnostics de l'analyse de balayage, journal de
/// diagnostic, et robustesse aux silences de pré-roulement / post-roulement d'une
/// capture réelle (la capture démarre désormais AVANT la lecture).
/// </summary>
public class DiagnosticsTests
{
    // ------------------------------------------------------------------
    // Niveaux de segments : détection de capture muette
    // ------------------------------------------------------------------

    [Fact]
    public void PeakDb_Sine_MatchesAmplitude()
    {
        int fs = 48000;
        var sine = new float[fs];
        for (int i = 0; i < sine.Length; i++)
            sine[i] = (float)(0.5 * Math.Sin(2.0 * Math.PI * 1000.0 * i / fs));

        Assert.Equal(-6.02, SpectrumTools.PeakDb(sine), 2);
    }

    [Fact]
    public void RmsDb_Sine_MatchesTheory()
    {
        int fs = 48000;
        var sine = new float[fs];
        for (int i = 0; i < sine.Length; i++)
            sine[i] = (float)(0.5 * Math.Sin(2.0 * Math.PI * 1000.0 * i / fs));

        // RMS d'un sinus = A/√2 → 20·log10(0,5/1,414) ≈ -9,03 dBFS
        Assert.Equal(-9.03, SpectrumTools.RmsDb(sine), 2);
    }

    [Fact]
    public void Levels_DigitalSilence_AreBelowDetection()
    {
        var silence = new float[48000];
        Assert.True(SpectrumTools.PeakDb(silence) < -200.0);
        Assert.True(SpectrumTools.RmsDb(silence) < -200.0);
        Assert.True(SpectrumTools.PeakDb(silence) < RoomMeasurementSilenceGate);
    }

    /// <summary>Seuil de muet utilisé par le moteur de mesure (copie de la constante
    /// audio pour éviter une dépendance du projet Core vers Audio).</summary>
    private const double RoomMeasurementSilenceGate = -66.0;

    [Fact]
    public void Levels_NoiseFloor_IsAboveSilenceGate()
    {
        // un vrai micro (même au repos) livre un souffle bien au-dessus du seuil de muet
        var rnd = new Random(42);
        var noise = new float[48000];
        for (int i = 0; i < noise.Length; i++)
            noise[i] = (float)((rnd.NextDouble() - 0.5) * 0.002); // ≈ -60 dBFS crête

        Assert.True(SpectrumTools.PeakDb(noise) > RoomMeasurementSilenceGate);
    }

    // ------------------------------------------------------------------
    // Analyse de balayage : diagnostics enrichis + silences de capture réelle
    // ------------------------------------------------------------------

    [Fact]
    public void Analyzer_ExceptionCarriesDiagnostics()
    {
        int fs = 48000;
        var spec = new LogSweepSpec(40.0, 16000.0, 2.0);
        var silence = new float[fs * 2];

        var ex = Assert.Throws<InvalidOperationException>(() =>
            SweepAnalyzer.Analyze(silence, fs, spec));

        Assert.Contains("Balayage non détecté", ex.Message);
        Assert.Contains("rapport pic/bruit", ex.Message);
    }

    /// <summary>Capture de la forme produite par le moteur réel : 0,8 s de silence
    /// (pré-roulement, la capture démarre avant la lecture), le balayage filtré par
    /// des biquads réels, puis 1 s de post-roll. L'analyse doit retrouver la réponse
    /// malgré le silence — c'est le rognage d'attaque qui le consommait avant.</summary>
    private static float[] FilterSweepBiquadsWithPreRoll(float[] sweep, int sampleRate, double preRollSeconds, double delaySeconds)
    {
        var lowPeak = new BiquadFilter(FilterType.Peaking, sampleRate, 100, 6.0, 2.0);
        var highDip = new BiquadFilter(FilterType.Peaking, sampleRate, 4000, -4.0, 3.0);
        int pre = (int)(preRollSeconds * sampleRate);
        int delaySamples = (int)(delaySeconds * sampleRate);
        var result = new float[pre + sweep.Length + sampleRate];
        for (int i = 0; i < result.Length - pre; i++)
        {
            double x = i >= delaySamples && i < delaySamples + sweep.Length ? sweep[i - delaySamples] : 0.0;
            result[i + pre] = (float)highDip.ProcessSample(lowPeak.ProcessSample(x));
        }
        return result;
    }

    [Fact]
    public void Analyzer_RecoversRoom_WithRealisticPreAndPostRollSilence()
    {
        int fs = 48000;
        var spec = new LogSweepSpec(40.0, 16000.0, 3.0);
        var gen = new LogSweepGenerator(spec, fs);
        var sweep = gen.GenerateFull(1.0);

        // pré-roulement 0,8 s (silence capturé avant la lecture) + propagation 0,15 s
        var captured = FilterSweepBiquadsWithPreRoll(sweep, fs, 0.8, 0.15);
        var analysis = SweepAnalyzer.Analyze(captured, fs, spec);

        Assert.InRange(analysis.PeakDelaySeconds, 0.90, 1.00);

        // anomalies de la pièce retrouvées dans les bandes utiles
        double peak100 = analysis.BandLevelsDb.Where((_, i) => analysis.BandFreqs[i] is > 80 and < 130).Max();
        Assert.InRange(peak100, 4.0, 6.5);
        double dip4000 = analysis.BandLevelsDb.Where((_, i) => analysis.BandFreqs[i] is > 3400 and < 4800).Min();
        Assert.InRange(dip4000, -4.8, -2.8);
    }

    // ------------------------------------------------------------------
    // Journal de diagnostic
    // ------------------------------------------------------------------

    [Fact]
    public void DiagnosticsLog_WritesReadableLines()
    {
        string marker = $"essai-{Guid.NewGuid():N}";
        DiagnosticsLog.Info(marker + " info");
        DiagnosticsLog.Warn(marker + " warn");
        DiagnosticsLog.Error(marker + " erreur", new InvalidOperationException("cause racine",
            new ArgumentException("cause interne")));

        Assert.NotNull(DiagnosticsLog.LogPath);
        string content = File.ReadAllText(DiagnosticsLog.LogPath!);
        Assert.Contains(marker + " info", content);
        Assert.Contains(marker + " warn", content);
        Assert.Contains(marker + " erreur", content);
        Assert.Contains("[INFO]", content);
        Assert.Contains("[ERREUR]", content);
        Assert.Contains("InvalidOperationException", content);
        Assert.Contains("cause racine", content);
        Assert.Contains("cause interne", content);

        // chaque entrée tient sur une ligne (les sauts de ligne sont aplatis)
        var lines = File.ReadAllLines(DiagnosticsLog.LogPath!);
        Assert.All(lines, l => Assert.DoesNotContain("\n", l));
    }

    [Fact]
    public void DiagnosticsLog_NeverThrows()
    {
        // messages contenant des cas particuliers : vide, sauts de ligne, null via string vide
        DiagnosticsLog.Info("");
        DiagnosticsLog.Info("ligne1\nligne2\r\nligne3");
        DiagnosticsLog.Error("avec exception", null);
    }

    [Fact]
    public void DiagnosticsLog_EnabledFalse_WritesNothing()
    {
        // VERSION PUBLIQUE : l'interrupteur global coupe TOUT — ni fichier, ni mémoire
        bool previous = DiagnosticsLog.Enabled;
        try
        {
            DiagnosticsLog.Enabled = false;
            try
            {
                DiagnosticsLog.Info("ce-message-ne-doit-jamais-apparaitre-42");
                DiagnosticsLog.Error("ni-cette-erreur-42", new InvalidOperationException("x"));
            }
            finally
            {
                // rien dans le tampon mémoire…
                string memory = DiagnosticsLog.MemoryDump();
                Assert.DoesNotContain("ne-doit-jamais-apparaitre-42", memory);
                Assert.DoesNotContain("ni-cette-erreur-42", memory);
                DiagnosticsLog.Enabled = true;
            }

            // …et rien dans le fichier de journal (s'il en existe un des tests précédents)
            if (DiagnosticsLog.LogPath is { } path)
            {
                string content = File.ReadAllText(path);
                Assert.DoesNotContain("ne-doit-jamais-apparaitre-42", content);
                Assert.DoesNotContain("ni-cette-erreur-42", content);
            }
        }
        finally
        {
            DiagnosticsLog.Enabled = previous;
        }
    }
}
