using RoomEq.Core.Dsp;

namespace RoomEq.Audio.Playback;

/// <summary>Un signal de test GÉNÉRÉ à la volée par le moteur (bruits, sinus, balayages).</summary>
public sealed record GeneratorSignalItem(
    string Id,
    string DisplayName,
    string Category,
    TestSignal Signal,
    double Amplitude,
    double SineFrequency = 1000.0,
    LogSweepSpec? SweepSpec = null);

/// <summary>
/// Catalogue des signaux de test générés par le moteur (spec I-AUD-02) :
/// bruits de référence, sinus utiles du grave à la limite d'audibilité,
/// balayages logarithmiques. Les fichiers WAV téléchargés s'y ajoutent depuis
/// la bibliothèque (I-IO-04), hors de ce module.
/// </summary>
public static class TestSignalCatalog
{
    /// <summary>Identifiant de l'élément sélectionné par défaut (bruit rose : réglage du niveau d'écoute).</summary>
    public const string DefaultItemId = "bruit-rose";

    /// <summary>Signaux intégrés, amplitudes calées pour un niveau perçu comparable.</summary>
    public static IReadOnlyList<GeneratorSignalItem> BuiltIn { get; } = new List<GeneratorSignalItem>
    {
        // ---------------------------------------------------------------- bruits
        new("bruit-blanc", "Bruit blanc", "Bruits", TestSignal.WhiteNoise, 0.20),
        new("bruit-rose", "Bruit rose", "Bruits", TestSignal.PinkNoise, 0.35),
        new("bruit-brun", "Bruit brun (rouge)", "Bruits", TestSignal.BrownNoise, 0.50),
        new("bruit-violet", "Bruit violet (bleu)", "Bruits", TestSignal.VioletNoise, 0.15),

        // ---------------------------------------------------------------- sinus
        new("sinus-50", "Sinus 50 Hz (grave)", "Sinus", TestSignal.Sine, 0.25, 50),
        new("sinus-100", "Sinus 100 Hz", "Sinus", TestSignal.Sine, 0.25, 100),
        new("sinus-250", "Sinus 250 Hz (bas-médium)", "Sinus", TestSignal.Sine, 0.25, 250),
        new("sinus-440", "Sinus 440 Hz (La de référence)", "Sinus", TestSignal.Sine, 0.25, 440),
        new("sinus-1000", "Sinus 1 kHz (référence)", "Sinus", TestSignal.Sine, 0.20, 1000),
        new("sinus-2000", "Sinus 2 kHz (sensibilité max. de l'oreille)", "Sinus", TestSignal.Sine, 0.20, 2000),
        new("sinus-4000", "Sinus 4 kHz", "Sinus", TestSignal.Sine, 0.20, 4000),
        new("sinus-8000", "Sinus 8 kHz", "Sinus", TestSignal.Sine, 0.18, 8000),
        new("sinus-12500", "Sinus 12,5 kHz", "Sinus", TestSignal.Sine, 0.15, 12500),
        new("sinus-15000", "Sinus 15 kHz (limite d'audibilité)", "Sinus", TestSignal.Sine, 0.15, 15000),

        // ------------------------------------------------------------ balayages
        new("balayage-20-20k", "Balayage 20 Hz → 20 kHz (12 s)", "Balayages",
            TestSignal.LogSweep, 0.20, SweepSpec: new LogSweepSpec(20.0, 20000.0, 12.0)),
        new("balayage-200-2k", "Balayage 200 Hz → 2 kHz (8 s, médium)", "Balayages",
            TestSignal.LogSweep, 0.25, SweepSpec: new LogSweepSpec(200.0, 2000.0, 8.0)),
    }.AsReadOnly();
}
