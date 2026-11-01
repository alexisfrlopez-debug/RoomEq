// RoomEq VST — utilitaires de test partagés (tests natifs + mini-hôte) :
// génération du signal de référence déterministe (IDENTIQUE à
// tools/gen_reference du module AU et à ses tests), lecture des données de
// référence C# commises dans natif/roomeq-au/tests/data, et macros de
// vérification sans framework (code de sortie non nul si échec).
#pragma once

#include <algorithm>
#include <cmath>
#include <cstdint>
#include <cstdio>
#include <cstdlib>
#include <cstring>
#include <fstream>
#include <string>
#include <vector>

// ---------------------------------------------------------------- vérifs

inline int g_failures = 0;
inline int g_checks = 0;

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

// ---------------------------------------------------------------- fichiers

inline std::string g_dataDir; // répertoire des références (argv[1] ou défaut)

inline std::string DataPath(const char* name) { return g_dataDir + "/" + name; }

inline bool ReadFileBytes(const std::string& path, std::vector<uint8_t>& out)
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

inline bool ReadFloats(const std::string& path, std::vector<float>& out)
{
    std::vector<uint8_t> bytes;
    if (!ReadFileBytes(path, bytes) || bytes.size() % 4 != 0)
        return false;
    out.resize(bytes.size() / 4);
    std::memcpy(out.data(), bytes.data(), bytes.size());
    return true;
}

// ---------------------------------------------------------------- signal

/// Signal de référence déterministe —impulsion puis silence puis balayage
/// log 20 Hz → 20 kHz — IDENTIQUE à MakeReferenceInput des tests AU
/// (natif/roomeq-au/tests/test_main.cpp) et au générateur C#.
inline std::vector<float> MakeReferenceInput(int rate, int channels)
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

// ----------------------------------------------------- (dé)entrelacement

/// Entrelacé → planaire (VST 2 traite par tableaux de canaux).
inline void Deinterleave(const std::vector<float>& in, int channels,
    std::vector<std::vector<float>>& out)
{
    const size_t frames = in.size() / static_cast<size_t>(channels);
    out.assign(static_cast<size_t>(channels), std::vector<float>(frames, 0.0f));
    for (size_t i = 0; i < frames; ++i)
        for (int c = 0; c < channels; ++c)
            out[static_cast<size_t>(c)][i] = in[i * static_cast<size_t>(channels) + c];
}

/// Planaire → entrelacé.
inline void Interleave(const std::vector<std::vector<float>>& in,
    std::vector<float>& out)
{
    const int channels = static_cast<int>(in.size());
    const size_t frames = in.empty() ? 0 : in[0].size();
    out.resize(frames * static_cast<size_t>(channels));
    for (size_t i = 0; i < frames; ++i)
        for (int c = 0; c < channels; ++c)
            out[i * static_cast<size_t>(channels) + c] = in[static_cast<size_t>(c)][i];
}

/// Compare une sortie planaire du greffon à la référence entrelacée C#.
/// Renvoie l'écart maximal trouvé.
inline double ComparePlanarToReference(const std::vector<std::vector<float>>& got,
    const std::vector<float>& expected, int channels)
{
    const size_t frames = expected.size() / static_cast<size_t>(channels);
    double worst = 0.0;
    for (size_t i = 0; i < frames; ++i)
        for (int c = 0; c < channels; ++c)
            worst = std::max(worst, std::fabs(static_cast<double>(got[static_cast<size_t>(c)][i])
                - expected[i * static_cast<size_t>(channels) + c]));
    return worst;
}
