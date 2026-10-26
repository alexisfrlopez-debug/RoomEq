// perf/Program.cs — harnais de mesure (décision D15 : mesurer AVANT d'optimiser).
//
// Répond par des faits à la demande « optimiser au max » : mesure les marges
// de temps réel des chemins critiques v3 SANS toucher une seule ligne de
// src/. Méthode : échauffement (JIT tiers), 5 lots, on retient le lot le plus
// rapide (le minimum est la mesure standard en micro-benchmark — le bruit du
// ordonnanceur ne peut qu'ajouter du temps). Machine partagée : les chiffres
// sont des bornes inférieures conservatrices.

using System.Diagnostics;
using System.Numerics;
using RoomEq.Core.Dsp;
using RoomEq.Core.Eq;
using RoomEq.Audio.Routing;

const int Fs = 48_000;
const int Channels = 2;
const int Frames = 480;                     // bloc de 10 ms
const int BlockFloats = Frames * Channels;  // 960 échantillons entrelacés

Console.WriteLine("RoomEq v3 — harnais de mesure perf/ (D15 : mesure avant optimisation)");
Console.WriteLine($"runtime .NET {Environment.Version}, {Environment.ProcessorCount} vCPU logiques (machine partagée)");
Console.WriteLine("charge : 48 kHz, stéréo, blocs de 10 ms, Release obligatoire");
Console.WriteLine();

// Signal de test déterministe (bruit LCG borné ±0,25)
var block = new float[BlockFloats];
uint seed = 42;
for (int i = 0; i < block.Length; i++)
{
    seed = seed * 1664525u + 1013904223u;
    block[i] = (seed >> 8) / (float)(1 << 24) * 0.5f - 0.25f;
}

// ── 1. FFT 4096 — chemin d'affichage RTA (hors thread audio) ────────────
var spectrum = new Complex[4096];
for (int i = 0; i < spectrum.Length; i++)
    spectrum[i] = new Complex(Math.Sin(2 * Math.PI * 123.45 * i / 4096) * 0.5, 0);
double fft4k = Bench("FFT 4096 en place (affichage RTA)", 2000, () => Fft.Forward(spectrum));
Margin("FFT 4096 — trame de 85,3 ms d'audio", fft4k, 4096, Fs);

// ── 2. FFT 1 048 576 — ordre de grandeur du chemin de calibration ───────
// (déconvolution de Farina : FFT de la taille du balayage complétée en
// puissance de 2 — l'opération unitaire la plus lourde de l'application)
var big = new Complex[1 << 20];
for (int i = 0; i < big.Length; i++)
    big[i] = new Complex(Math.Sin(2 * Math.PI * 997.0 * i / big.Length) * 0.5, 0);
double fft1m = Bench("FFT 1 048 576 en place (calibration)", 5, () => Fft.Forward(big));
Margin("FFT 1M — ~21,8 s d'audio traités d'un bloc", fft1m, 1 << 20, Fs);

// ── 3. Chaîne EQ 33 filtres/canal — LE thread audio ─────────────────────
var chain = new EqChain();
chain.Configure(Fs, Channels);
var preset = new EqPreset("perf-33");
double f = 25.0;
while (f <= 16000.0)
{
    preset.Bands.Add(new EqBand(FilterType.Peaking, f, (f * 7.0) % 13.0 - 6.0, 1.41));
    f *= Math.Pow(2.0, 1.0 / 3.0);
}
preset.Bands.Add(new EqBand(FilterType.LowShelf, 60.0, 2.5, 0.7));
preset.Bands.Add(new EqBand(FilterType.HighShelf, 8000.0, -2.5, 0.7));
chain.SetPreset(preset);
Console.WriteLine($"   EqChain : {preset.Bands.Count} bandes actives × {Channels} canaux, bloc de 10 ms entrelacé");
double eq = Bench("EqChain.Process, bloc 10 ms stéréo", 5000, () => chain.Process(block, 0, BlockFloats));
Margin("EqChain — thread audio, 10 ms de son", eq, Frames, Fs);

// Preuve d'allocation nulle : la propriété critique anti-GC du thread audio
GC.Collect(); GC.WaitForPendingFinalizers(); GC.Collect();
long before = GC.GetAllocatedBytesForCurrentThread();
for (int i = 0; i < 100_000; i++)
    chain.Process(block, 0, BlockFloats);
long alloc = GC.GetAllocatedBytesForCurrentThread() - before;
Console.WriteLine($"   allocations de EqChain.Process sur 100 000 blocs (1 000 s de son) : {alloc} octet(s)");
Console.WriteLine();

// ── 4. Balayage log 12 s — référence de calibration (hors ligne) ────────
double sweep = Bench("Balayage log 12 s (ESS, régénération complète)", 40,
    () => new LogSweepGenerator(LogSweepSpec.Default, Fs).GenerateFull(0.5));
Margin("Balayage — 12 s de signal générés", sweep, 12 * Fs, Fs);

// ── 5. RingBuffer SPSC — routage capture → mixeur ───────────────────────
var ring = new RingBuffer(Fs * Channels);
var sink = new float[BlockFloats];
double rb = Bench("RingBuffer SPSC, écriture + lecture d'un bloc 10 ms", 20_000, () =>
{
    ring.Write(block);
    ring.Read(sink);
});
Margin("RingBuffer — routage, 10 ms de son", rb, Frames, Fs);

Console.WriteLine();
Console.WriteLine("Lecture : « marge N × temps réel » = combien de fois le travail tient dans son budget");
Console.WriteLine("audio (N ≥ 10 suffit largement en production ; la machine partagée sous-estime N).");

// ── helpers ──────────────────────────────────────────────────────────────
static double Bench(string name, int iters, Action op)
{
    int warm = Math.Min(iters, 500);
    for (int i = 0; i < warm; i++) op();
    double best = double.MaxValue;
    for (int b = 0; b < 5; b++)
    {
        long t0 = Stopwatch.GetTimestamp();
        for (int i = 0; i < iters; i++) op();
        double us = (Stopwatch.GetTimestamp() - t0) * 1_000_000.0 / Stopwatch.Frequency / iters;
        best = Math.Min(best, us);
    }
    Console.WriteLine($"   {name,-52} : {best,12:0.000} µs/opération");
    return best;
}

static void Margin(string what, double usPerOp, int frames, int fs)
{
    double audioUs = frames * 1_000_000.0 / fs;
    Console.WriteLine($"      → {what,-52} marge {audioUs / usPerOp,12:0} × temps réel");
}
