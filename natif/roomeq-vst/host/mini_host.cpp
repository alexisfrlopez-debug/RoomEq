// Mini-hôte VST 2 pour test d'ABI (spec I-AUD-08) : charge le greffon Linux
// (RoomEqVst.so) par dlopen et pilote UN cycle de vie complet par l'ABI C —
// exactement ce que fait un DAW — puis compare le rendu aux références C#.
// Valide la couche « contrat » : offsets de AEffect, signatures des
// callbacks, ordre des opcodes, chunks, bypass.
//
// Usage : roomeq_vst_host <RoomEqVst.so> [répertoire des références]
#include "test_util.h"

#include "../vst/vst2_abi.h"

#include <dlfcn.h>

#include <cstdio>
#include <cstring>
#include <string>
#include <vector>

using namespace vst;

// ---------------------------------------------------------------- hôte factice

namespace {

int g_automateCalls = 0;
int g_updateDisplayCalls = 0;

IntPtr TestHost(AEffect*, int32_t opcode, int32_t, IntPtr, void*, float)
{
    switch (opcode) {
    case kAudioMasterVersion:
        return kVstVersion; // 2400 : ce que répond Live 9
    case kAudioMasterAutomate:
        ++g_automateCalls;
        return 1;
    case kAudioMasterUpdateDisplay:
        ++g_updateDisplayCalls;
        return 1;
    default:
        return 0;
    }
}

typedef AEffect* (*EntryProc)(HostCallback);

/// Traite le signal de référence par blocs et compare à la référence.
double RunAndCompare(AEffect* effect, int rate, const char* referenceFile,
    int channels)
{
    std::vector<float> expected;
    if (!ReadFloats(DataPath(referenceFile), expected)) {
        ++g_failures;
        std::printf("ÉCHEC lecture %s\n", referenceFile);
        return 1e30;
    }

    std::vector<std::vector<float>> planar;
    Deinterleave(MakeReferenceInput(rate, channels), channels, planar);
    const size_t frames = planar[0].size();
    const int32_t blockSize = 512; // bloc DAW typique

    double worst = 0.0;
    for (size_t start = 0; start < frames; start += static_cast<size_t>(blockSize)) {
        const int32_t n = static_cast<int32_t>(
            std::min(static_cast<size_t>(blockSize), frames - start));
        float* in[2] = { planar[0].data() + start, planar[1].data() + start };
        float* out[2] = { planar[0].data() + start, planar[1].data() + start };
        effect->processReplacing(effect, in, out, n); // in-place, comme un DAW
        for (int c = 0; c < channels; ++c)
            for (int32_t i = 0; i < n; ++i)
                worst = std::max(worst,
                    std::fabs(static_cast<double>(in[c][i])
                        - expected[(start + static_cast<size_t>(i))
                                       * static_cast<size_t>(channels)
                                   + static_cast<size_t>(c)]));
    }
    return worst;
}

} // namespace

int main(int argc, char** argv)
{
    if (argc < 2) {
        std::printf("usage : %s <RoomEqVst.so> [répertoire des références]\n",
            argv[0]);
        return 2;
    }
    g_dataDir = argc > 2 ? argv[2] : "natif/roomeq-au/tests/data";
    const char* soPath = argv[1];

    // ---------------------------------------------------------------- chargement
    void* module = dlopen(soPath, RTLD_NOW | RTLD_LOCAL);
    CHECK(module != nullptr);
    if (module == nullptr) {
        std::printf("dlopen : %s\n", dlerror());
        return 1;
    }

    auto entry = reinterpret_cast<EntryProc>(dlsym(module, "VSTPluginMain"));
    CHECK(entry != nullptr);
    auto legacyEntry = reinterpret_cast<EntryProc>(dlsym(module, "main"));
    CHECK(legacyEntry != nullptr);
    if (entry == nullptr || legacyEntry == nullptr) {
        std::printf("ÉCHEC : exports absents\n");
        return 1;
    }

    // -------------------------------------------------------------- création
    AEffect* effect = entry(&TestHost);
    CHECK(effect != nullptr);
    if (effect == nullptr)
        return 1;
    CHECK(effect->magic == kEffectMagic);
    CHECK(effect->numInputs == 2);
    CHECK(effect->numOutputs == 2);
    CHECK(effect->numParams == 1);
    CHECK(effect->numPrograms == 1);
    CHECK(effect->uniqueID == CConst('R', 'm', 'e', 'q'));
    CHECK((effect->flags & kEffFlagsCanReplacing) != 0);
    CHECK((effect->flags & kEffFlagsProgramChunks) != 0);
    CHECK(effect->dispatcher != nullptr);
    CHECK(effect->processReplacing != nullptr);
    CHECK(effect->setParameter != nullptr && effect->getParameter != nullptr);

    // ------------------------------------------------------------- cycle de vie
    CHECK(effect->dispatcher(effect, kEffOpen, 0, 0, nullptr, 0.0f) == 1);
    CHECK(effect->dispatcher(effect, kEffSetSampleRate, 0, 0, nullptr, 48000.0f)
        == 1);
    CHECK(effect->dispatcher(effect, kEffSetBlockSize, 0, 512, nullptr, 0.0f)
        == 1);
    CHECK(effect->dispatcher(effect, kEffMainsChanged, 0, 1, nullptr, 0.0f) == 1);

    // preset par chunk (comme la restauration d'un projet DAW)
    std::vector<uint8_t> presetBytes;
    CHECK(ReadFileBytes(DataPath("preset_demo.json"), presetBytes));
    CHECK(effect->dispatcher(effect, kEffSetChunk, 1,
              static_cast<IntPtr>(presetBytes.size()), presetBytes.data(), 0.0f)
        == 1);
    CHECK(effect->getParameter(effect, 0) == 0.0f); // chunk ≠ paramètre

    // comme un DAW restaurant un projet : le chunk pose le preset, l'hôte
    // réapplique ensuite le paramètre qu'il avait sauvegardé (préampli du
    // preset de démo : −6,5 dB — intégré aux références C#)
    effect->setParameter(effect, 0, -6.5f);

    // rendu vs référence C#
    const double worst = RunAndCompare(effect, 48000, "impulse_demo_48k_stereo.f32", 2);
    ++g_checks;
    if (worst > 2e-6) {
        ++g_failures;
        std::printf("ÉCHEC parité ABI : écart max %.3g > 2e-6\n", worst);
    } else {
        std::printf("  parité ABI 48k stereo : écart max %.2e\n", worst);
    }

    // relecture du chunk : octets préservés
    void* chunk = nullptr;
    const IntPtr chunkSize =
        effect->dispatcher(effect, kEffGetChunk, 1, 0, &chunk, 0.0f);
    CHECK(chunkSize == static_cast<IntPtr>(presetBytes.size()));
    CHECK(chunk != nullptr);
    CHECK(chunkSize <= 0
        || std::memcmp(chunk, presetBytes.data(), presetBytes.size()) == 0);

    // automatisation du préampli
    effect->setParameter(effect, 0, -6.0f);
    CHECK_NEAR(effect->getParameter(effect, 0), -6.0f, 1e-9);

    // bypass : sortie = entrée bit à bit
    CHECK(effect->dispatcher(effect, kEffSetBypass, 0, 1, nullptr, 0.0f) == 1);
    {
        const auto input = MakeReferenceInput(48000, 2);
        std::vector<std::vector<float>> planar;
        Deinterleave(input, 2, planar);
        float* in[2] = { planar[0].data(), planar[1].data() };
        float* out[2] = { planar[0].data(), planar[1].data() };
        effect->processReplacing(effect, in, out, 256);
        double bypassWorst = 0.0;
        for (int c = 0; c < 2; ++c)
            for (int i = 0; i < 256; ++i)
                bypassWorst = std::max(bypassWorst,
                    std::fabs(static_cast<double>(out[c][i])
                        - input[static_cast<size_t>(i) * 2 + c]));
        CHECK(bypassWorst == 0.0);
    }
    CHECK(effect->dispatcher(effect, kEffSetBypass, 0, 0, nullptr, 0.0f) == 1);

    // suspension/reprise
    CHECK(effect->dispatcher(effect, kEffMainsChanged, 0, 0, nullptr, 0.0f) == 1);
    CHECK(effect->dispatcher(effect, kEffMainsChanged, 0, 1, nullptr, 0.0f) == 1);

    // identité : entrée usine historique « main » fabrique aussi un greffon
    AEffect* legacyEffect = legacyEntry(&TestHost);
    CHECK(legacyEffect != nullptr);
    if (legacyEffect != nullptr) {
        CHECK(legacyEffect->dispatcher(legacyEffect, kEffOpen, 0, 0, nullptr, 0.0f)
            == 1);
        CHECK(legacyEffect->dispatcher(legacyEffect, kEffClose, 0, 0, nullptr, 0.0f)
            == 0);
    }

    // ---------------------------------------------------------------- fermeture
    CHECK(effect->dispatcher(effect, kEffMainsChanged, 0, 0, nullptr, 0.0f) == 1);
    CHECK(effect->dispatcher(effect, kEffClose, 0, 0, nullptr, 0.0f) == 0);
    CHECK(dlclose(module) == 0);

    std::printf("\n%d vérifications, %d échecs\n", g_checks, g_failures);
    if (g_failures > 0) {
        std::printf("MINI-HOTE VST : ÉCHEC\n");
        return 1;
    }
    std::printf("MINI-HOTE VST : OK\n");
    return 0;
}
