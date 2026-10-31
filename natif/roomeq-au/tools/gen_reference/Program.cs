// Générateur de données de référence pour le cœur C++ du greffon AU
// (spec I-AUD-07, invariant 1) : les sorties de RoomEq.Core servent de
// référence EXACTE pour les tests natifs (natif/roomeq-au/tests/data).
//
// Usage : dotnet run --project natif/roomeq-au/tools/gen_reference
//         (régénère tests/data — les fichiers sont commités pour que la CI
//          macOS n'ait pas besoin du SDK .NET pour lancer les tests natifs)
using System.Numerics;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using RoomEq.Core.Dsp;
using RoomEq.Core.Eq;
using RoomEq.Core.Io;

var outDir = args.Length > 0
    ? args[0]
    : Path.GetFullPath(Path.Combine(
        AppContext.BaseDirectory, "..", "..", "..", "..", "..", "tests", "data"));
Directory.CreateDirectory(outDir);
Console.WriteLine($"Répertoire de sortie : {outDir}");

// ---------------------------------------------------------------- presets

// Preset réaliste « correction de salle » : 10 bandes mixtes + préampli.
var demo = new EqPreset("Salon — correction v1")
{
    PreampDb = -6.5,
    Description = "Référence de parité pour le greffon AU",
    Bands =
    {
        new EqBand(FilterType.LowShelf, 62, 4.2, 0.9),
        new EqBand(FilterType.Peaking, 125, -5.8, 2.8),
        new EqBand(FilterType.Peaking, 310, -3.1, 1.6),
        new EqBand(FilterType.Peaking, 900, 2.4, 1.2),
        new EqBand(FilterType.Peaking, 2400, -4.6, 3.3),
        new EqBand(FilterType.Peaking, 5600, 2.9, 1.8),
        new EqBand(FilterType.Notch, 7350, 0.0, 8.0),
        new EqBand(FilterType.Peaking, 11000, -2.2, 1.4),
        new EqBand(FilterType.HighShelf, 15000, 3.0, 0.7),
        new EqBand(FilterType.Peaking, 18000, -1.0, 0.5, enabled: false), // inactive
    },
};

// Preset de bordes : valeurs hors plages ramenées aux bornes par les filtres.
var edges = new EqPreset("Bordes")
{
    PreampDb = 2.0,
    Bands =
    {
        new EqBand(FilterType.Peaking, 0.05, 12.0, 100.0),   // f0 → 1 Hz, Q → 50
        new EqBand(FilterType.Peaking, 60000, -9.0, 0.001),  // f0 → fs·0,499, Q → 0,05
        new EqBand(FilterType.HighPass, 40, 0.0, 0.7071),
        new EqBand(FilterType.LowPass, 16000, 0.0, 0.7071),
    },
};

// Preset transparent (aucune bande active).
var flat = new EqPreset("Transparent") { PreampDb = 0.0 };

// ---------------------------------------------------------------- écritures

WriteJson(demo, "preset_demo.json");
WriteJson(edges, "preset_edges.json");
WriteJson(flat, "preset_flat.json");

// .ffp du preset démo (round-trip : réimporté puis comparé aux bandes).
File.WriteAllBytes(Path.Combine(outDir, "preset_demo.ffp"), FabFilterProQ3.Encode(demo));

// réponse impulsionnelle + balayage à travers EqChain (référence exacte)
WriteSignal("impulse_demo_48k_stereo.f32", demo, 48000, 2);
WriteSignal("impulse_demo_44k_mono.f32", demo, 44100, 1);
WriteSignal("impulse_edges_48k_stereo.f32", edges, 48000, 2);
WriteSignal("impulse_flat_48k_stereo.f32", flat, 48000, 2);

// courbe de réponse composite (97 points log-répartis, préampli inclus)
WriteCurve(demo, "curve_demo_48k.csv", 48000);
WriteCurve(edges, "curve_edges_48k.csv", 48000);

Console.WriteLine("Références générées :");
foreach (var f in Directory.GetFiles(outDir).Order())
    Console.WriteLine($"  {Path.GetFileName(f),-32} {new FileInfo(f).Length,8} octets  sha256={Sha256(f)[..16]}…");

// ---------------------------------------------------------------- helpers

void WriteJson(EqPreset preset, string file)
{
    File.WriteAllText(Path.Combine(outDir, file),
        JsonSerializer.Serialize(preset, JsonOpts.Default));
}

void WriteSignal(string file, EqPreset preset, int rate, int channels)
{
    // signal déterministe : impulsion unitaire, silence, puis balayage
    // logarithmique 8192 trames à amplitude 0,5 (l'état des filtres excite
    // transitoire ET régime permanent)
    const int tail = 4096, sweep = 8192;
    int frames = 1 + tail + sweep;
    var data = new float[frames * channels];

    for (int c = 0; c < channels; c++)
        data[c] = 1.0f;

    const double f0 = 20.0, f1 = 20000.0;
    double log0 = Math.Log10(f0), log1 = Math.Log10(f1);
    for (int i = 0; i < sweep; i++)
    {
        double f = Math.Pow(10.0, log0 + (log1 - log0) * i / (sweep - 1));
        double phase = 2.0 * Math.PI * f * i / rate;
        double v = 0.5 * Math.Sin(phase);
        for (int c = 0; c < channels; c++)
            data[(1 + tail + i) * channels + c] = (float)v;
    }

    var chain = new EqChain();
    chain.Configure(rate, channels);
    chain.SetPreset(preset);
    chain.Process(data, 0, data.Length);

    File.WriteAllBytes(Path.Combine(outDir, file), ToBytes(data));
}

void WriteCurve(EqPreset preset, string file, int rate)
{
    var chain = new EqChain();
    chain.Configure(rate, 2);
    chain.SetPreset(preset);
    var pts = chain.ResponseCurve(20.0, 20000.0, 97);
    var sb = new StringBuilder();
    sb.AppendLine("frequency,response_db");
    foreach (var p in pts)
        sb.AppendLine(FormattableString.Invariant($"{p.Freq:R},{p.Db:R}"));
    File.WriteAllText(Path.Combine(outDir, file), sb.ToString());
}

static byte[] ToBytes(float[] data)
{
    var bytes = new byte[data.Length * 4];
    Buffer.BlockCopy(data, 0, bytes, 0, bytes.Length);
    return bytes;
}

static string Sha256(string file)
    => Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(file)));
