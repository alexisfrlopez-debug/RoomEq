// Tests natifs du greffon VST (spec I-AUD-08) — liés DIRECTEMENT à la classe
// RoomEqVst (l'ABI C est validée par le mini-hôte, host/mini_host.cpp) :
// parité avec les références C# du module AU, chunks, paramètres, bypass,
// rejets. Sans framework : exécutable autonome, code de sortie non nul si
// échec.
//
// Usage : roomeq_vst_tests [répertoire des données]
//         (défaut : <source>/../roomeq-au/tests/data)
#include "test_util.h"

#include "vst/RoomEqVst.h"

#include "roomeq/dsp/eq_chain.h"
#include "roomeq/preset/preset_io.h"

#include <cstring>
#include <string>
#include <vector>

#ifndef ROOMEQ_VST_SOURCE_DIR
#define ROOMEQ_VST_SOURCE_DIR "."
#endif

using namespace roomeq;

// ---------------------------------------------------------------- hôte factice

namespace {
int g_automateCalls = 0;
float g_lastAutomateValue = -999.0f;
int g_updateDisplayCalls = 0;

vst::IntPtr DummyHost(vst::AEffect*, int32_t opcode, int32_t, vst::IntPtr,
    void*, float opt)
{
    switch (opcode) {
    case vst::kAudioMasterVersion:
        return vst::kVstVersion;
    case vst::kAudioMasterAutomate:
        ++g_automateCalls;
        g_lastAutomateValue = opt;
        return 1;
    case vst::kAudioMasterUpdateDisplay:
        ++g_updateDisplayCalls;
        return 1;
    default:
        return 0;
    }
}

/// Hôte factice minimal pour les tests : version + compteurs d'appels.
/// (défini aussi anonymement dans test_util.h ? non — ici, local.)

std::vector<uint8_t> LoadData(const char* name)
{
    std::vector<uint8_t> bytes;
    if (!ReadFileBytes(DataPath(name), bytes)) {
        ++g_failures;
        std::printf("ÉCHEC lecture %s\n", name);
    }
    return bytes;
}

/// Traite le signal de référence par le greffon (canaux planaires).
std::vector<std::vector<float>> ProcessThrough(RoomEqVst& plugin, int rate,
    int channels, int32_t blockSize = 512)
{
    std::vector<std::vector<float>> planar;
    Deinterleave(MakeReferenceInput(rate, channels), channels, planar);
    std::vector<std::vector<float>> out(static_cast<size_t>(channels));
    for (int c = 0; c < channels; ++c)
        out[static_cast<size_t>(c)].assign(planar[static_cast<size_t>(c)].size(), 0.0f);

    const size_t frames = planar[0].size();
    for (size_t start = 0; start < frames; start += static_cast<size_t>(blockSize)) {
        const int32_t n = static_cast<int32_t>(
            std::min(static_cast<size_t>(blockSize), frames - start));
        std::vector<float*> in, o;
        for (int c = 0; c < channels; ++c) {
            in.push_back(planar[static_cast<size_t>(c)].data() + start);
            o.push_back(out[static_cast<size_t>(c)].data() + start);
        }
        RoomEqVst::ProcessReplace(&plugin, in.data(), o.data(), n);
    }
    return out;
}

/// Compare la sortie du greffon au fichier de référence C# (entrelacé).
void CheckParity(const char* label, RoomEqVst& plugin, const char* referenceFile,
    int rate, int channels, double tolerance)
{
    std::vector<float> expected;
    if (!ReadFloats(DataPath(referenceFile), expected)) {
        ++g_failures;
        std::printf("ÉCHEC lecture %s\n", referenceFile);
        return;
    }
    const auto got = ProcessThrough(plugin, rate, channels);
    const double worst = ComparePlanarToReference(got, expected, channels);
    ++g_checks;
    if (worst > tolerance) {
        ++g_failures;
        std::printf("ÉCHEC parité %s : écart max %.3g > %.3g\n", label, worst,
            tolerance);
    } else {
        std::printf("  %-34s écart max %.2e (tolérance %.0e) — %zu échantillons\n",
            label, worst, tolerance, expected.size());
    }
}

/// Variante mono : la référence est mono (1 canal) mais le greffon est
/// stéréo — CHAQUE canal doit reproduire la référence.
void CheckParityMono(const char* label, RoomEqVst& plugin,
    const char* referenceFile, int rate, double tolerance)
{
    std::vector<float> expected;
    if (!ReadFloats(DataPath(referenceFile), expected)) {
        ++g_failures;
        std::printf("ÉCHEC lecture %s\n", referenceFile);
        return;
    }
    const auto got = ProcessThrough(plugin, rate, 2);
    double worst = 0.0;
    for (size_t i = 0; i < expected.size(); ++i)
        for (int c = 0; c < 2; ++c)
            worst = std::max(worst,
                std::fabs(static_cast<double>(got[static_cast<size_t>(c)][i])
                    - expected[i]));
    ++g_checks;
    if (worst > tolerance) {
        ++g_failures;
        std::printf("ÉCHEC parité %s : écart max %.3g > %.3g\n", label, worst,
            tolerance);
    } else {
        std::printf("  %-34s écart max %.2e (tolérance %.0e) — %zu échantillons\n",
            label, worst, tolerance, expected.size() * 2);
    }
}

} // namespace

// ------------------------------------------------------------------ scénarios

static void TestConstruction()
{
    std::puts("— construction / champs AEffect");
    RoomEqVst plugin(&DummyHost);
    CHECK(plugin.magic == vst::kEffectMagic);
    CHECK(plugin.numPrograms == 1);
    CHECK(plugin.numParams == RoomEqVst::kNumParams);
    CHECK(plugin.numInputs == 2);
    CHECK(plugin.numOutputs == 2);
    CHECK(plugin.uniqueID == vst::CConst('R', 'm', 'e', 'q'));
    CHECK(plugin.version == RoomEqVst::kVersion);
    CHECK((plugin.flags & vst::kEffFlagsCanReplacing) != 0);
    CHECK((plugin.flags & vst::kEffFlagsProgramChunks) != 0);
    CHECK(plugin.initialDelay == 0);
    CHECK(plugin.resvd1 == 0 && plugin.resvd2 == 0);
    CHECK(plugin.dispatcher != nullptr);
    CHECK(plugin.processReplacing != nullptr);
    CHECK(plugin.process != nullptr);
    CHECK(plugin.setParameter != nullptr && plugin.getParameter != nullptr);
    CHECK(plugin.PresetVersion() == 0);
    CHECK(plugin.PresetBytes().empty());
    CHECK(plugin.PreampParam() == 0.0f);
    CHECK(plugin.ProgramName() == "Init");
}

static void TestParity()
{
    std::puts("— parité vs références C# (module AU)");
    // Chargement INTERACTIF (updateParameter = true) : le préampli du preset
    // devient le paramètre « Preamp » — c'est lui qui pilote la chaîne, comme
    // dans le greffon AU (les références C# intègrent le préampli du preset).
    {
        RoomEqVst plugin(&DummyHost);
        plugin.SetSampleRate(48000.0);
        const auto bytes = LoadData("preset_demo.json");
        std::string error;
        CHECK(plugin.ApplyPresetBytes(bytes.data(), bytes.size(), true, error));
        CheckParity("demo 48k stereo", plugin, "impulse_demo_48k_stereo.f32",
            48000, 2, 2e-6);
    }
    {
        RoomEqVst plugin(&DummyHost);
        plugin.SetSampleRate(44100.0);
        const auto bytes = LoadData("preset_demo.json");
        std::string error;
        CHECK(plugin.ApplyPresetBytes(bytes.data(), bytes.size(), true, error));
        // 44,1 kHz mono : même signal sur les deux canaux (greffon stéréo)
        CheckParityMono("demo 44k1 mono", plugin, "impulse_demo_44k_mono.f32",
            44100, 2e-6);
    }
    {
        RoomEqVst plugin(&DummyHost);
        plugin.SetSampleRate(48000.0);
        const auto bytes = LoadData("preset_edges.json");
        std::string error;
        CHECK(plugin.ApplyPresetBytes(bytes.data(), bytes.size(), true, error));
        CheckParity("bordes 48k stereo", plugin, "impulse_edges_48k_stereo.f32",
            48000, 2, 2e-6);
    }
    {
        // preset transparent : aucune bande, préampli 0
        RoomEqVst plugin(&DummyHost);
        plugin.SetSampleRate(48000.0);
        const auto bytes = LoadData("preset_flat.json");
        std::string error;
        CHECK(plugin.ApplyPresetBytes(bytes.data(), bytes.size(), true, error));
        CheckParity("transparent 48k stereo", plugin, "impulse_flat_48k_stereo.f32",
            48000, 2, 2e-6);
    }
}

static void TestPreampParameter()
{
    std::puts("— paramètre Preamp");
    const auto bytes = LoadData("preset_demo.json");
    std::string error;

    RoomEqVst plugin(&DummyHost);
    plugin.SetSampleRate(48000.0);
    CHECK(plugin.ApplyPresetBytes(bytes.data(), bytes.size(), false, error));
    plugin.SetParameter(&plugin, 0, -6.0f);
    CHECK_NEAR(plugin.PreampParam(), -6.0f, 1e-9);
    CHECK_NEAR(plugin.GetParameter(&plugin, 0), -6.0f, 1e-9);

    // bornes
    plugin.SetParameter(&plugin, 0, 99.0f);
    CHECK_NEAR(plugin.PreampParam(), 36.0f, 1e-9);
    plugin.SetParameter(&plugin, 0, -99.0f);
    CHECK_NEAR(plugin.PreampParam(), -36.0f, 1e-9);
    plugin.SetParameter(&plugin, 7, 5.0f); // index invalide : ignoré

    // le paramètre s'applique vraiment : sortie = EqChain(préampli -6)
    plugin.SetParameter(&plugin, 0, -6.0f);
    const auto got = ProcessThrough(plugin, 48000, 2);

    preset::PresetDef preset;
    CHECK(preset::FromJson(std::string(bytes.begin(), bytes.end()), preset, error));
    dsp::EqChain reference;
    reference.Configure(48000.0, 2);
    reference.SetPreset(preset);
    reference.SetPreampDb(-6.0);
    auto interleaved = MakeReferenceInput(48000, 2);
    reference.Process(interleaved.data(), interleaved.size());

    const double worst = ComparePlanarToReference(got, interleaved, 2);
    ++g_checks;
    if (worst > 2e-6) {
        ++g_failures;
        std::printf("ÉCHEC preamp -6 dB : écart max %.3g\n", worst);
    } else {
        std::printf("  %-34s écart max %.2e\n", "preamp param -6 dB", worst);
    }
}

static void TestPresetLoadingAndNotify()
{
    std::puts("— chargement de preset (.json/.ffp) + notification hôte");
    const auto json = LoadData("preset_demo.json");
    const auto ffp = LoadData("preset_demo.ffp");
    std::string error;

    RoomEqVst plugin(&DummyHost);
    plugin.SetSampleRate(48000.0);

    // .ffp (magie binaire) — la bande inactive du preset n'est pas exportée
    const int automateBefore = g_automateCalls;
    const int displayBefore = g_updateDisplayCalls;
    CHECK(plugin.ApplyPresetBytes(ffp.data(), ffp.size(), true, error));
    const preset::PresetDef ffpPreset = plugin.CurrentPreset();
    CHECK(ffpPreset.bands.size() == 9);
    CHECK(plugin.PresetBytes() == ffp);
    CHECK(plugin.PresetVersion() == 1);
    // notification : automate + update display, préampli recalcé
    CHECK(g_automateCalls == automateBefore + 1);
    CHECK_NEAR(g_lastAutomateValue, ffpPreset.preampDb, 1e-6);
    CHECK(g_updateDisplayCalls == displayBefore + 1);
    // le nom du programme suit le preset
    CHECK(plugin.ProgramName() != "Init");

    // .json par-dessus
    CHECK(plugin.ApplyPresetBytes(json.data(), json.size(), true, error));
    CHECK(plugin.PresetVersion() == 2);
    CHECK(plugin.PresetBytes() == json);

    // rejets
    CHECK(!plugin.ApplyPresetBytes(nullptr, 10, true, error));
    CHECK(!plugin.ApplyPresetBytes(json.data(), 0, true, error));
    CHECK(!plugin.ApplyPresetBytes(reinterpret_cast<const uint8_t*>("ceci n'est"),
             9, true, error));
    std::vector<uint8_t> truncated(ffp.begin(),
        ffp.begin() + 24); // magie FQ3p mais tronqué
    CHECK(!plugin.ApplyPresetBytes(truncated.data(), truncated.size(), true, error));
    CHECK(plugin.PresetVersion() == 2); // rien n'a changé
    CHECK(plugin.PresetBytes() == json);
}

static void TestChunks()
{
    std::puts("— chunks (sauvegarde/restauration de session)");
    const auto json = LoadData("preset_demo.json");
    std::string error;

    // instance source : preset chargé, préampli réglé par l'utilisateur
    RoomEqVst source(&DummyHost);
    source.SetSampleRate(48000.0);
    CHECK(source.ApplyPresetBytes(json.data(), json.size(), false, error));
    source.SetParameter(&source, 0, -3.5f);

    // effGetChunk : octets bruts du preset
    void* chunkPtr = nullptr;
    const vst::IntPtr size = RoomEqVst::Dispatcher(&source, vst::kEffGetChunk, 1,
        0, &chunkPtr, 0.0f);
    CHECK(size == static_cast<vst::IntPtr>(json.size()));
    CHECK(chunkPtr != nullptr);
    CHECK(std::memcmp(chunkPtr, json.data(), json.size()) == 0);

    // instance de restauration : chunk appliqué SANS toucher au paramètre
    RoomEqVst restored(&DummyHost);
    restored.SetSampleRate(48000.0);
    CHECK(RoomEqVst::Dispatcher(&restored, vst::kEffSetChunk, 1, size, chunkPtr,
              0.0f)
        == 1);
    CHECK_NEAR(restored.PreampParam(), 0.0f, 1e-9); // l'hôte fait foi
    CHECK(restored.PresetBytes() == json);
    CHECK(restored.ProgramName() == source.ProgramName());

    // les deux instances rendent la même chose (à préampli égal)
    restored.SetParameter(&restored, 0, -3.5f);
    const auto a = ProcessThrough(source, 48000, 2);
    const auto b = ProcessThrough(restored, 48000, 2);
    double worst = 0.0;
    for (size_t c = 0; c < a.size(); ++c)
        for (size_t i = 0; i < a[c].size(); ++i)
            worst = std::max(worst,
                std::fabs(static_cast<double>(a[c][i]) - b[c][i]));
    CHECK(worst == 0.0); // bit à bit : même preset, même préampli

    // chunk vide = transparent
    RoomEqVst cleared(&DummyHost);
    CHECK(RoomEqVst::Dispatcher(&cleared, vst::kEffSetChunk, 1, 0, nullptr, 0.0f)
        == 1);
    CHECK(cleared.PresetBytes().empty());
    CHECK(cleared.ProgramName() == "Init");

    // chunk invalide : refus (retour 0), état inchangé
    const uint8_t garbage[] = "ni json ni ffp";
    RoomEqVst untouched(&DummyHost);
    CHECK(untouched.ApplyPresetBytes(json.data(), json.size(), false, error));
    CHECK(RoomEqVst::Dispatcher(&untouched, vst::kEffSetChunk, 1,
              static_cast<vst::IntPtr>(sizeof(garbage)), const_cast<uint8_t*>(garbage),
              0.0f)
        == 0);
    CHECK(untouched.PresetBytes() == json);
}

static void TestBypassAndReset()
{
    std::puts("— bypass / suspension");
    const auto json = LoadData("preset_demo.json");
    std::string error;

    RoomEqVst plugin(&DummyHost);
    plugin.SetSampleRate(48000.0);
    CHECK(plugin.ApplyPresetBytes(json.data(), json.size(), false, error));

    // bypass : sortie = entrée, bit à bit
    plugin.SetBypassed(true);
    auto got = ProcessThrough(plugin, 48000, 2);
    const auto input = MakeReferenceInput(48000, 2);
    double worst = ComparePlanarToReference(got, input, 2);
    CHECK(worst == 0.0);

    // effSetBypass par l'ABI
    CHECK(RoomEqVst::Dispatcher(&plugin, vst::kEffSetBypass, 0, 1, nullptr, 0.0f)
        == 1);
    CHECK(plugin.Bypassed());
    CHECK(RoomEqVst::Dispatcher(&plugin, vst::kEffSetBypass, 0, 0, nullptr, 0.0f)
        == 1);
    CHECK(!plugin.Bypassed());

    // suspension (effMainsChanged 0) : états remis à zéro → une instance
    // fraîche traitant la même entrée donne le même résultat
    RoomEqVst fresh(&DummyHost);
    fresh.SetSampleRate(48000.0);
    CHECK(fresh.ApplyPresetBytes(json.data(), json.size(), false, error));
    plugin.SetBypassed(false);
    plugin.SetParameter(&plugin, 0, 0.0f);
    const auto after = ProcessThrough(plugin, 48000, 2);
    const auto virgin = ProcessThrough(fresh, 48000, 2);
    worst = 0.0;
    for (size_t c = 0; c < after.size(); ++c)
        for (size_t i = 0; i < after[c].size(); ++i)
            worst = std::max(worst,
                std::fabs(static_cast<double>(after[c][i]) - virgin[c][i]));
    CHECK(worst == 0.0);
}

static void TestAccumulateMode()
{
    std::puts("— mode accumulation (process historique)");
    RoomEqVst plugin(&DummyHost);
    plugin.SetSampleRate(48000.0);

    const int frames = 64;
    std::vector<float> inL(frames, 0.25f), inR(frames, 0.25f);
    std::vector<float> outL(frames, 0.5f), outR(frames, 0.5f); // contenu préexistant
    float* inputs[2] = { inL.data(), inR.data() };
    float* outputs[2] = { outL.data(), outR.data() };

    RoomEqVst::ProcessAccumulate(&plugin, inputs, outputs, frames);
    // preset transparent : traitement = identité → out = 0,5 + 0,25
    for (int i = 0; i < frames; ++i) {
        CHECK_NEAR(outputs[0][i], 0.75f, 1e-7);
        CHECK_NEAR(outputs[1][i], 0.75f, 1e-7);
    }
}

static void TestStrings()
{
    std::puts("— chaînes ABI");
    RoomEqVst plugin(&DummyHost);

    char name[vst::kMaxParamStrLen] = { 0 };
    char label[vst::kMaxParamStrLen] = { 0 };
    char display[vst::kMaxParamStrLen] = { 0 };
    CHECK(RoomEqVst::Dispatcher(&plugin, vst::kEffGetParamName, 0, 0, name, 0.0f)
        == 1);
    CHECK(RoomEqVst::Dispatcher(&plugin, vst::kEffGetParamLabel, 0, 0, label, 0.0f)
        == 1);
    plugin.SetParameter(&plugin, 0, -12.5f);
    CHECK(RoomEqVst::Dispatcher(&plugin, vst::kEffGetParamDisplay, 0, 0, display,
              0.0f)
        == 1);
    CHECK(std::strcmp(name, "Preamp") == 0);
    CHECK(std::strcmp(label, "dB") == 0);
    CHECK(std::strcmp(display, "-12.5") == 0);
    CHECK(std::strlen(name) < vst::kMaxParamStrLen); // terminateur inclus

    char effect[vst::kMaxEffectNameLen] = { 0 };
    char vendor[vst::kMaxVendorStrLen] = { 0 };
    char product[vst::kMaxProductStrLen] = { 0 };
    char program[vst::kMaxProgNameLen] = { 0 };
    CHECK(RoomEqVst::Dispatcher(&plugin, vst::kEffGetEffectName, 0, 0, effect, 0.0f)
        == 1);
    CHECK(RoomEqVst::Dispatcher(&plugin, vst::kEffGetVendorString, 0, 0, vendor,
              0.0f)
        == 1);
    CHECK(RoomEqVst::Dispatcher(&plugin, vst::kEffGetProductString, 0, 0, product,
              0.0f)
        == 1);
    CHECK(RoomEqVst::Dispatcher(&plugin, vst::kEffGetProgramName, 0, 0, program,
              0.0f)
        == 1);
    CHECK(std::strcmp(effect, "RoomEq Correction") == 0);
    CHECK(std::strcmp(vendor, "RoomEq") == 0);
    CHECK(std::strcmp(product, "RoomEq Correction") == 0);
    CHECK(std::strcmp(program, "Init") == 0);

    // renommage du programme (cosmétique)
    CHECK(RoomEqVst::Dispatcher(&plugin, vst::kEffSetProgramName, 0, 0,
              const_cast<char*>("Mon preset"), 0.0f)
        == 1);
    CHECK(plugin.ProgramName() == "Mon preset");

    CHECK(RoomEqVst::Dispatcher(&plugin, vst::kEffGetVstVersion, 0, 0, nullptr,
              0.0f)
        == 2400);
    CHECK(RoomEqVst::Dispatcher(&plugin, vst::kEffGetVendorVersion, 0, 0, nullptr,
              0.0f)
        == RoomEqVst::kVersion);
    CHECK(RoomEqVst::Dispatcher(&plugin, vst::kEffGetPlugCategory, 0, 0, nullptr,
              0.0f)
        == 1);
    CHECK(RoomEqVst::Dispatcher(&plugin, vst::kEffCanDo, 0, 0,
              const_cast<char*>("bypass"), 0.0f)
        == 1);
    CHECK(RoomEqVst::Dispatcher(&plugin, vst::kEffCanDo, 0, 0,
              const_cast<char*>("somethingElse"), 0.0f)
        == 0);
    CHECK(RoomEqVst::Dispatcher(&plugin, 9999, 0, 0, nullptr, 0.0f) == 0);
}

static void TestCloseThroughAbi()
{
    std::puts("— effClose par l'ABI (destruction)");
    auto* effect = CreateRoomEqVst(&DummyHost);
    CHECK(effect != nullptr);
    CHECK(RoomEqVst::Dispatcher(effect, vst::kEffOpen, 0, 0, nullptr, 0.0f) == 1);
    CHECK(RoomEqVst::Dispatcher(effect, vst::kEffClose, 0, 0, nullptr, 0.0f) == 0);
    // (mémoire libérée — plus aucun accès autorisé après effClose)
}

// ------------------------------------------------------------------ principal

int main(int argc, char** argv)
{
    g_dataDir = argc > 1 ? argv[1]
                         : ROOMEQ_VST_SOURCE_DIR "/../roomeq-au/tests/data";
    std::printf("Répertoire des références : %s\n", g_dataDir.c_str());

    TestConstruction();
    TestParity();
    TestPreampParameter();
    TestPresetLoadingAndNotify();
    TestChunks();
    TestBypassAndReset();
    TestAccumulateMode();
    TestStrings();
    TestCloseThroughAbi();

    std::printf("\n%d vérifications, %d échecs\n", g_checks, g_failures);
    if (g_failures > 0) {
        std::printf("ROOMEQ VST : ÉCHEC\n");
        return 1;
    }
    std::printf("ROOMEQ VST : OK\n");
    return 0;
}
