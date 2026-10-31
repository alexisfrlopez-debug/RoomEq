// Tests natifs du cœur C++ du greffon AU (spec I-AUD-07) : parité EXACTE avec
// RoomEq.Core sur des données de référence générées par le code C#
// (tools/gen_reference → tests/data), plus robustesse des analyseurs.
// Sans framework : exécutable autonome, code de sortie non nul si échec.
#include "roomeq/dsp/biquad.h"
#include "roomeq/dsp/eq_chain.h"
#include "roomeq/json/mini_json.h"
#include "roomeq/preset/preset_io.h"

#include <algorithm>
#include <cmath>
#include <cstdio>
#include <cstdlib>
#include <cstring>
#include <fstream>
#include <string>
#include <vector>

#ifndef ROOMEQ_AU_SOURCE_DIR
#define ROOMEQ_AU_SOURCE_DIR "."
#endif

static int g_failures = 0;
static int g_checks = 0;

#define CHECK(cond)                                                                    \
    do {                                                                               \
        ++g_checks;                                                                    \
        if (!(cond)) {                                                                 \
            ++g_failures;                                                              \
            std::printf("ÉCHEC %s:%d : %s\n", __FILE__, __LINE__, #cond);              \
        }                                                                              \
    } while (0)

#define CHECK_NEAR(a, b, tol)                                                          \
    do {                                                                               \
        ++g_checks;                                                                    \
        const double _d = std::fabs(static_cast<double>(a) - static_cast<double>(b));  \
        if (!(_d <= (tol))) {                                                          \
            ++g_failures;                                                              \
            std::printf("ÉCHEC %s:%d : |%.9g - %.9g| = %.3g > %.3g (%s)\n",            \
                __FILE__, __LINE__, static_cast<double>(a), static_cast<double>(b),    \
                _d, static_cast<double>(tol), #a " ≈ " #b);                            \
        }                                                                              \
    } while (0)

using namespace roomeq;

static std::string g_dataDir;

static std::string DataPath(const char* name) { return g_dataDir + "/" + name; }

static bool ReadFileBytes(const std::string& path, std::vector<uint8_t>& out)
{
    std::ifstream f(path, std::ios::binary);
    if (!f)
        return false;
    f.seekg(0, std::ios::end);
    const auto size = f.tellg();
    f.seekg(0, std::ios::beg);
    out.resize(static_cast<size_t>(size));
    f.read(reinterpret_cast<char*>(out.data()), size);
    return f.good() || f.eof();
}

static bool ReadFileText(const std::string& path, std::string& out)
{
    std::ifstream f(path, std::ios::binary);
    if (!f)
        return false;
    out.assign(std::istreambuf_iterator<char>(f), std::istreambuf_iterator<char>());
    return true;
}

static bool ReadFloats(const std::string& path, std::vector<float>& out)
{
    std::vector<uint8_t> bytes;
    if (!ReadFileBytes(path, bytes) || bytes.size() % 4 != 0)
        return false;
    out.resize(bytes.size() / 4);
    std::memcpy(out.data(), bytes.data(), bytes.size());
    return true;
}

// ------------------------------------------------------------- mini JSON

static void TestMiniJson()
{
    std::printf("* mini JSON\n");
    json::Value v;
    std::string err;

    CHECK(json::Parse("null", v, err) && v.IsNull());
    CHECK(json::Parse("true", v, err) && v.IsBool() && v.AsBool());
    CHECK(json::Parse("false", v, err) && v.IsBool() && !v.AsBool());
    CHECK(json::Parse(" -12.5e2 ", v, err) && v.IsNumber());
    CHECK_NEAR(v.AsNumber(), -1250.0, 0.0);
    CHECK(json::Parse("\"a\\nb\\u00e9\\u4f60\\\"q\\\\\"", v, err) && v.IsString());
    CHECK(v.AsString() == "a\nbé你\"q\\");

    CHECK(json::Parse("[1, 2, 3]", v, err) && v.IsArray() && v.Size() == 3);
    CHECK_NEAR(v.At(2).AsNumber(), 3.0, 0.0);
    CHECK(v.At(99).IsNull()); // hors bornes : valeur nulle, pas de plantage

    CHECK(json::Parse(
              "{\"Name\":\"X\",\"PreampDb\":-6.5,\"Bands\":[{\"Q\":1.25,\"Enabled\":false}]}",
              v, err)
          && v.IsObject());
    CHECK(v.Get("Name").AsString() == "X");
    CHECK_NEAR(v.Get("PreampDb").AsNumber(), -6.5, 0.0);
    CHECK(v.Get("Bands").At(0).Get("Q").IsNumber());
    CHECK(v.Get("Bands").At(0).Get("Enabled").IsBool() && !v.Get("Bands").At(0).Get("Enabled").AsBool());
    CHECK(!v.Has("Inconnu") && v.Get("Inconnu").IsNull());

    // erreurs attendues
    CHECK(!json::Parse("{\"a\":", v, err) && !err.empty());
    CHECK(!json::Parse("[1, 2", v, err) && !err.empty());
    CHECK(!json::Parse("\"\\q\"", v, err) && !err.empty()); // échappement inconnu
    CHECK(!json::Parse("12 34", v, err) && !err.empty());   // reste après valeur
    // profondeur bornée
    std::string deep;
    for (int i = 0; i < 40; ++i)
        deep += "[";
    for (int i = 0; i < 40; ++i)
        deep += "]";
    CHECK(!json::Parse(deep, v, err) && !err.empty());
}

// ------------------------------------------------------------- preset JSON

static void TestPresetJson()
{
    std::printf("* preset JSON (format application)\n");

    // fichier réel produit par l'application (PascalCase + énumérations en chaînes)
    std::string text;
    CHECK(ReadFileText(DataPath("preset_demo.json"), text));
    preset::PresetDef p;
    std::string err;
    CHECK(preset::FromJson(text, p, err));
    CHECK(p.name == "Salon — correction v1");
    CHECK_NEAR(p.preampDb, -6.5, 0.0);
    CHECK(p.bands.size() == 10);

    CHECK(p.bands[0].type == dsp::FilterType::LowShelf);
    CHECK_NEAR(p.bands[0].frequency, 62.0, 0.0);
    CHECK_NEAR(p.bands[0].gainDb, 4.2, 0.0);
    CHECK_NEAR(p.bands[0].q, 0.9, 0.0);
    CHECK(p.bands[0].enabled);

    CHECK(p.bands[6].type == dsp::FilterType::Notch);
    CHECK_NEAR(p.bands[6].q, 8.0, 0.0);
    CHECK(!p.bands[9].enabled); // bande inactive conservée (mais ignorée par la chaîne)

    // camelCase toléré + Enabled par défaut vrai + type en entier
    CHECK(preset::FromJson(
        "{\"name\":\"n\",\"preampDb\":3.5,\"bands\":[{\"type\":1,\"frequency\":200,"
        "\"gainDb\":-2,\"q\":2}]}",
        p, err));
    CHECK(p.name == "n" && p.bands.size() == 1 && p.bands[0].enabled);
    CHECK(p.bands[0].type == dsp::FilterType::LowShelf);

    // sans bandes : transparent valide
    CHECK(preset::FromJson("{}", p, err) && p.bands.empty());

    // erreurs
    CHECK(!preset::FromJson("{\"bands\": 3}", p, err) && !err.empty());
    CHECK(!preset::FromJson("{\"bands\":[{\"frequency\":NaN}]}", p, err) && !err.empty());

    // bordes : valeurs folles mais finies → acceptées (bornées par les filtres)
    CHECK(ReadFileText(DataPath("preset_edges.json"), text));
    CHECK(preset::FromJson(text, p, err));
    CHECK_NEAR(p.bands[0].frequency, 0.05, 0.0); // non borné à la lecture
    CHECK_NEAR(p.bands[0].q, 100.0, 0.0);
}

// ------------------------------------------------------------- preset FFP

static void TestPresetFfp()
{
    std::printf("* preset FFP (FabFilter Pro-Q 3)\n");
    std::vector<uint8_t> bytes;
    CHECK(ReadFileBytes(DataPath("preset_demo.ffp"), bytes));
    CHECK(bytes.size() == 1348); // 12 + 334×4 (spec M-IO-06)

    preset::PresetDef p;
    std::string err;
    CHECK(preset::FromFfp(bytes.data(), bytes.size(), p, err));
    CHECK(p.bands.size() == 9); // la bande inactive n'est pas exportée

    // valeurs d'origine (toutes dans les plages du greffon → exactes à ~1e-4,
    // le stockage FFP étant en float32 avec encodages log)
    static const double kFreq[] = { 62, 125, 310, 900, 2400, 5600, 7350, 11000, 15000 };
    static const double kGain[] = { 4.2, -5.8, -3.1, 2.4, -4.6, 2.9, 0.0, -2.2, 3.0 };
    static const double kQ[] = { 0.9, 2.8, 1.6, 1.2, 3.3, 1.8, 8.0, 1.4, 0.7 };
    for (size_t i = 0; i < 9; ++i) {
        // le stockage FFP est en float32 (log2 pour la fréquence) : tolérance
        // relative serrée plutôt qu'absolue
        CHECK_NEAR(p.bands[i].frequency, kFreq[i], kFreq[i] * 3e-6 + 0.01);
        CHECK_NEAR(p.bands[i].gainDb, kGain[i], 1e-4);
        CHECK_NEAR(p.bands[i].q, kQ[i], 2e-3);
        CHECK(p.bands[i].enabled);
    }
    CHECK(p.bands[0].type == dsp::FilterType::LowShelf);
    CHECK(p.bands[6].type == dsp::FilterType::Notch);
    CHECK(p.bands[8].type == dsp::FilterType::HighShelf);
    for (size_t i = 1; i < 6; ++i)
        CHECK(p.bands[i].type == dsp::FilterType::Peaking);
    CHECK_NEAR(p.preampDb, -6.5, 1e-4); // gain de sortie → préampli

    // magie / erreurs
    CHECK(!preset::FromFfp(nullptr, 10, p, err) && !err.empty());
    std::vector<uint8_t> garbage = { 'F', 'Q', '2', 'p', 4, 0, 0, 0 };
    CHECK(!preset::FromFfp(garbage.data(), garbage.size(), p, err));
    std::vector<uint8_t> truncated = bytes;
    truncated.resize(100);
    CHECK(!preset::FromFfp(truncated.data(), truncated.size(), p, err));

    // détection de format
    CHECK(preset::SniffFormat(bytes.data(), bytes.size()) == "ffp");
    const char* jsonText = "  {\"Name\":\"x\"}";
    CHECK(preset::SniffFormat(reinterpret_cast<const uint8_t*>(jsonText), std::strlen(jsonText)) == "json");
    const char* junk = "hello";
    CHECK(preset::SniffFormat(reinterpret_cast<const uint8_t*>(junk), std::strlen(junk)).empty());
}

// ------------------------------------------------------------- biquad

static void TestBiquad()
{
    std::printf("* biquad (bornes RBJ)\n");
    dsp::BiquadFilter f;

    // bornes de fréquence : f0 < 1 Hz → 1 Hz ; f0 > fs·0,499 → fs·0,499
    // (les coefficients d'un filtre configuré à 0,05 Hz sont identiques à ceux
    // d'un filtre configuré directement à la borne 1 Hz)
    f.Configure(dsp::FilterType::Peaking, 48000, 0.05, 6.0, 1.0);
    auto g = dsp::BiquadFilter(dsp::FilterType::Peaking, 48000, 1.0, 6.0, 1.0);
    CHECK_NEAR(f.Coefficients().b0, g.Coefficients().b0, 1e-12);
    CHECK_NEAR(f.Coefficients().a1, g.Coefficients().a1, 1e-12);

    auto h = dsp::BiquadFilter(dsp::FilterType::Peaking, 48000, 60000, 6.0, 1.0);
    auto hRef = dsp::BiquadFilter(dsp::FilterType::Peaking, 48000, 48000 * 0.499, 6.0, 1.0);
    CHECK_NEAR(h.Coefficients().b1, hRef.Coefficients().b1, 1e-12);

    // bornes de Q
    auto qLow = dsp::BiquadFilter(dsp::FilterType::Peaking, 48000, 1000, 6.0, 0.001);
    auto qLowRef = dsp::BiquadFilter(dsp::FilterType::Peaking, 48000, 1000, 6.0, 0.05);
    CHECK_NEAR(qLow.Coefficients().a2, qLowRef.Coefficients().a2, 1e-12);

    // un bell de gain G vérifie |H(f0)| = G
    f.Configure(dsp::FilterType::Peaking, 48000, 1000, 9.0, 1.4);
    CHECK_NEAR(f.ResponseDb(1000), 9.0, 1e-9);
    // un coupe-haut (HighPass) Butterworth à f0 vaut −3 dB (10·log10 ½)
    f.Configure(dsp::FilterType::HighPass, 48000, 1000, 0.0, 0.7071067811865476);
    CHECK_NEAR(f.ResponseDb(1000), -3.0102999566398138, 1e-9);
    // état temporel : Reset remet la sortie à zéro pour une entrée nulle
    f.Reset();
    CHECK_NEAR(f.Process(0.0), 0.0, 0.0);

    // réponse à zéro en dehors du spectre utile (LowPass 100 Hz à 20 kHz)
    f.Configure(dsp::FilterType::LowPass, 48000, 100, 0.0, 0.7071);
    CHECK(f.ResponseDb(20000) < -60.0);
}

// ------------------------------------------------------------- parité DSP

/// Reconstruit le signal d'entrée déterministe du générateur C#
/// (impulsion + balayage log 20 Hz → 20 kHz, amplitude 0,5).
static std::vector<float> MakeReferenceInput(int rate, int channels)
{
    const int tail = 4096, sweep = 8192;
    const size_t frames = 1 + static_cast<size_t>(tail) + sweep;
    std::vector<float> data(frames * static_cast<size_t>(channels), 0.0f);
    for (int c = 0; c < channels; ++c)
        data[static_cast<size_t>(c)] = 1.0f;

    const double f0 = 20.0, f1 = 20000.0;
    const double log0 = std::log10(f0), log1 = std::log10(f1);
    for (int i = 0; i < sweep; ++i) {
        const double f = std::pow(10.0, log0 + (log1 - log0) * i / (sweep - 1));
        const double phase = 6.283185307179586476925286766559 * f * i / rate;
        const double v = 0.5 * std::sin(phase);
        for (int c = 0; c < channels; ++c)
            data[(static_cast<size_t>(1 + tail + i)) * channels + c] = static_cast<float>(v);
    }
    return data;
}

static void CheckParitySignal(const char* referenceFile, const preset::PresetDef& preset,
    int rate, int channels, double tolerance)
{
    std::vector<float> expected;
    if (!ReadFloats(DataPath(referenceFile), expected)) {
        ++g_failures;
        std::printf("ÉCHEC lecture %s\n", DataPath(referenceFile).c_str());
        return;
    }
    std::vector<float> data = MakeReferenceInput(rate, channels);
    if (data.size() != expected.size()) {
        ++g_failures;
        std::printf("ÉCHEC taille %s : %zu ≠ %zu\n", referenceFile, data.size(), expected.size());
        return;
    }

    dsp::EqChain chain;
    chain.Configure(rate, channels);
    chain.SetPreset(preset);
    chain.Process(data.data(), data.size());

    double worst = 0.0;
    for (size_t i = 0; i < data.size(); ++i)
        worst = std::max(worst, std::fabs(static_cast<double>(data[i]) - expected[i]));
    ++g_checks;
    if (worst > tolerance) {
        ++g_failures;
        std::printf("ÉCHEC parité %s : écart max %.3g > %.3g\n", referenceFile, worst, tolerance);
    } else {
        std::printf("  %-32s écart max %.2e (tolérance %.0e) — %zu échantillons\n",
            referenceFile, worst, tolerance, data.size());
    }
}

static void CheckParityCurve(const char* referenceFile, const preset::PresetDef& preset, int rate)
{
    std::string text;
    if (!ReadFileText(DataPath(referenceFile), text)) {
        ++g_failures;
        std::printf("ÉCHEC lecture %s\n", referenceFile);
        return;
    }
    dsp::EqChain chain;
    chain.Configure(rate, 2);
    chain.SetPreset(preset);

    size_t line = 0, points = 0;
    double worst = 0.0;
    size_t pos = 0;
    while (pos < text.size()) {
        size_t eol = text.find('\n', pos);
        if (eol == std::string::npos)
            eol = text.size();
        std::string row = text.substr(pos, eol - pos);
        pos = eol + 1;
        ++line;
        if (line == 1 || row.empty())
            continue; // en-tête
        const size_t comma = row.find(',');
        if (comma == std::string::npos)
            continue;
        const double freq = std::strtod(row.c_str(), nullptr);
        const double db = std::strtod(row.c_str() + comma + 1, nullptr);
        const double got = chain.ResponseDb(freq);
        worst = std::max(worst, std::fabs(got - db));
        ++points;
    }
    ++g_checks;
    if (points != 97 || worst > 1e-9) {
        ++g_failures;
        std::printf("ÉCHEC courbe %s : %zu points, écart max %.3g\n", referenceFile, points, worst);
    } else {
        std::printf("  %-32s écart max %.2e — %zu points\n", referenceFile, worst, points);
    }
}

static void TestParity()
{
    std::printf("* parité DSP ↔ RoomEq.Core (références C#)\n");
    preset::PresetDef demo, edges, flat;
    std::string text, err;
    if (!ReadFileText(DataPath("preset_demo.json"), text)
        || !preset::FromJson(text, demo, err)) {
        ++g_failures;
        std::printf("ÉCHEC lecture preset_demo.json : %s\n", err.c_str());
        return;
    }
    if (!ReadFileText(DataPath("preset_edges.json"), text)
        || !preset::FromJson(text, edges, err)) {
        ++g_failures;
        std::printf("ÉCHEC lecture preset_edges.json : %s\n", err.c_str());
        return;
    }
    if (!ReadFileText(DataPath("preset_flat.json"), text)
        || !preset::FromJson(text, flat, err)) {
        ++g_failures;
        std::printf("ÉCHEC lecture preset_flat.json : %s\n", err.c_str());
        return;
    }

    // invariant 1 : parité numérique (les deux chaînes calculent en double,
    // le stockage final est en float32 → tolérance serrée 2e-6)
    CheckParitySignal("impulse_demo_48k_stereo.f32", demo, 48000, 2, 2e-6);
    CheckParitySignal("impulse_demo_44k_mono.f32", demo, 44100, 1, 2e-6);
    CheckParitySignal("impulse_edges_48k_stereo.f32", edges, 48000, 2, 2e-6);
    CheckParitySignal("impulse_flat_48k_stereo.f32", flat, 48000, 2, 2e-6);

    // courbes de réponse composites (double → double)
    CheckParityCurve("curve_demo_48k.csv", demo, 48000);
    CheckParityCurve("curve_edges_48k.csv", edges, 48000);
}

// ------------------------------------------------------------- chaîne

static void TestEqChain()
{
    std::printf("* chaîne (bypass, reset, préampli, bandes actives)\n");
    dsp::EqChain chain;
    chain.Configure(48000, 2);

    preset::PresetDef p;
    p.preampDb = -6.0;
    p.bands = { preset::EqBandDef{ .type = dsp::FilterType::Peaking,
                     .frequency = 1000,
                     .gainDb = 6.0,
                     .q = 1.0,
                     .enabled = true },
        preset::EqBandDef{ .type = dsp::FilterType::Peaking,
            .frequency = 5000,
            .gainDb = -3.0,
            .q = 2.0,
            .enabled = false } };
    chain.SetPreset(p);
    CHECK(chain.ActiveBandCount() == 1); // bande inactive ignorée

    // bypass : signal inchangé bit à bit
    float signal[8] = { 0.25f, -0.25f, 0.5f, -0.5f, 0.75f, -0.75f, 1.0f, -1.0f };
    float reference[8];
    std::memcpy(reference, signal, sizeof(signal));
    chain.SetBypass(true);
    chain.Process(signal, 8);
    CHECK(std::memcmp(signal, reference, sizeof(signal)) == 0);
    chain.SetBypass(false);

    // Reset : un Dirac après Reset traverse depuis un état nul
    chain.Reset();
    float impulse[4] = { 1.0f, 1.0f, 0.0f, 0.0f };
    chain.Process(impulse, 4);
    CHECK(std::fabs(impulse[0]) > 1e-3);

    // préampli modifiable à chaud (temps réel sûr)
    chain.SetPreampDb(0.0);
    CHECK_NEAR(chain.PreampDb(), 0.0, 0.0);

    // Reset remet l'état : deux passes identiques après deux Reset
    float pass1[4] = { 1.0f, 1.0f, 0.0f, 0.0f };
    chain.Reset();
    chain.Process(pass1, 4);
    float pass2[4] = { 1.0f, 1.0f, 0.0f, 0.0f };
    chain.Reset();
    chain.Process(pass2, 4);
    CHECK(pass1[0] == pass2[0] && pass1[1] == pass2[1]);
}

// ------------------------------------------------------------- main

int main(int argc, char** argv)
{
    g_dataDir = argc > 1 ? argv[1] : ROOMEQ_AU_SOURCE_DIR "/tests/data";
    std::printf("RoomEq AU — tests natifs du cœur (données : %s)\n\n", g_dataDir.c_str());

    TestMiniJson();
    TestPresetJson();
    TestPresetFfp();
    TestBiquad();
    TestParity();
    TestEqChain();

    std::printf("\n%d vérifications, %d échec(s)\n", g_checks, g_failures);
    if (g_failures != 0)
        std::printf("RÉSULTAT : ÉCHEC\n");
    else
        std::printf("RÉSULTAT : OK\n");
    return g_failures == 0 ? 0 : 1;
}
