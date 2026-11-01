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
#include <ctime>
#include <fstream>
#include <string>
#include <vector>

#if defined(_WIN32)
#include <direct.h>
#include <sys/utime.h>
#else
#include <sys/stat.h>
#include <sys/types.h>
#include <unistd.h>
#include <utime.h>
#endif

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

// ------------------------------------------------- auto-chargement (v3.4.1)
// Retour utilisateur réel : « aucune différence de son quand le greffon est
// activé » — le greffon était TRANSPARENT sans preset chargé. L'auto-
// chargement du magasin de l'application doit rendre la correction audible
// dès l'insertion, avec l'état du projet (chunk) toujours maître.

namespace {

/// Écrit des octets dans un fichier du magasin factice.
bool WriteFileBytes(const std::string& path, const std::vector<uint8_t>& bytes)
{
    std::ofstream f(path, std::ios::binary | std::ios::trunc);
    if (!f)
        return false;
    f.write(reinterpret_cast<const char*>(bytes.data()),
        static_cast<std::streamsize>(bytes.size()));
    return f.good();
}

/// Horodate un fichier (mtime = maintenant + décalage en secondes).
void StampFile(const std::string& path, long long offsetSeconds)
{
    const time_t when = time(nullptr) + static_cast<time_t>(offsetSeconds);
#if defined(_WIN32)
    struct _utimbuf times { when, when };
    _utime(path.c_str(), &times);
#else
    struct utimbuf times { when, when };
    utime(path.c_str(), &times);
#endif
}

void SetStoreEnv(const char* value)
{
#if defined(_WIN32)
    _putenv_s("ROOMEQ_PRESETS_DIR", value);
#else
    setenv("ROOMEQ_PRESETS_DIR", value, 1);
#endif
}

void ClearStoreEnv()
{
#if defined(_WIN32)
    _putenv_s("ROOMEQ_PRESETS_DIR", "");
#else
    unsetenv("ROOMEQ_PRESETS_DIR");
#endif
}

void MakeStoreDir(const std::string& path)
{
#if defined(_WIN32)
    _mkdir(path.c_str());
#else
    mkdir(path.c_str(), 0755);
#endif
}

/// Ouvre le greffon par l'ABI (le chemin réel d'un hôte).
vst::IntPtr OpenPlugin(RoomEqVst& plugin)
{
    return RoomEqVst::Dispatcher(&plugin, vst::kEffOpen, 0, 0, nullptr, 0.0f);
}

} // namespace

static void TestAutoLoad()
{
    std::puts("— auto-chargement du magasin (v3.4.1)");

    const auto demo = LoadData("preset_demo.json");
    const auto edges = LoadData("preset_edges.json");
    const auto ffp = LoadData("preset_demo.ffp");
    if (g_failures > 0)
        return; // données de référence absentes : le reste n'aurait pas de sens

    const std::string store = "roomeq-autoload-store";
    MakeStoreDir(store);
    const std::string oldPath = store + "/ancien.json";
    const std::string recentPath = store + "/recent.json";
    const std::string corruptPath = store + "/corrompu.json";

    // A — magasin sain : le plus récent gagne, la correction est AUDIBLE
    CHECK(WriteFileBytes(oldPath, edges));
    CHECK(WriteFileBytes(recentPath, demo));
    StampFile(oldPath, -300);
    StampFile(recentPath, -150);
    SetStoreEnv(store.c_str());
    {
        RoomEqVst plugin(&DummyHost);
        plugin.SetSampleRate(48000.0);
        CHECK(OpenPlugin(plugin) == 1);
        CHECK(plugin.HasPreset());
        const preset::PresetDef loaded = plugin.CurrentPreset();
        CHECK(loaded.name == "Salon — correction v1"); // le plus récent
        size_t active = 0;
        for (const auto& b : loaded.bands)
            if (b.enabled)
                ++active;
        CHECK(active == 9);
        CHECK_NEAR(plugin.PreampParam(), -6.5, 1e-6); // recalcé + notifié
        CHECK_NEAR(g_lastAutomateValue, -6.5f, 1e-6);
        CHECK(plugin.PresetBytes() == demo); // chunk = octets du fichier

        // preuve sonore : la sortie correspond à la référence C# du preset
        // (le scénario exact du retour utilisateur « aucune différence »)
        CheckParity("auto-charge 48k stereo", plugin,
            "impulse_demo_48k_stereo.f32", 48000, 2, 2e-6);
    }

    // B — plus récent corrompu : repli sur le valide suivant (pas d'aîné)
    const std::string garbage = "{ ceci n'est pas un preset";
    CHECK(WriteFileBytes(corruptPath,
        std::vector<uint8_t>(garbage.begin(), garbage.end())));
    StampFile(corruptPath, 0); // le plus récent
    {
        RoomEqVst plugin(&DummyHost);
        plugin.SetSampleRate(48000.0);
        CHECK(OpenPlugin(plugin) == 1);
        CHECK(plugin.HasPreset());
        CHECK(plugin.CurrentPreset().name == "Salon — correction v1"); // repli
    }

    // C — l'état du projet (chunk) reste maître après l'auto-chargement
    {
        RoomEqVst plugin(&DummyHost);
        plugin.SetSampleRate(48000.0);
        CHECK(OpenPlugin(plugin) == 1); // auto-charge « Salon »
        std::string error;
        CHECK(plugin.ApplyPresetBytes(edges.data(), edges.size(), false, error));
        CHECK(plugin.CurrentPreset().name == "Bordes"); // le chunk gagne
    }
    {
        // projet sauvegardé SANS preset : chunk vide → transparent, même si
        // le magasin de l'application contient un preset
        RoomEqVst plugin(&DummyHost);
        plugin.SetSampleRate(48000.0);
        CHECK(OpenPlugin(plugin) == 1);
        CHECK(RoomEqVst::Dispatcher(&plugin, vst::kEffSetChunk, 1, 0, nullptr,
                  0.0f)
            == 1);
        CHECK(!plugin.HasPreset());
        CHECK(plugin.CurrentPreset().bands.empty());
    }

    // D — magasin .ffp uniquement
    CHECK(std::remove(oldPath.c_str()) == 0);
    CHECK(std::remove(recentPath.c_str()) == 0);
    CHECK(std::remove(corruptPath.c_str()) == 0);
    CHECK(WriteFileBytes(store + "/seul.ffp", ffp));
    {
        RoomEqVst plugin(&DummyHost);
        plugin.SetSampleRate(48000.0);
        CHECK(OpenPlugin(plugin) == 1);
        CHECK(plugin.HasPreset());
        CHECK(plugin.CurrentPreset().bands.size() == 9);
    }
    CHECK(std::remove((store + "/seul.ffp").c_str()) == 0);

    // E — répertoire inexistant / vide : transparent, sans échec d'ouverture
    {
        RoomEqVst plugin(&DummyHost);
        plugin.SetSampleRate(48000.0);
        CHECK(OpenPlugin(plugin) == 1);
        CHECK(!plugin.HasPreset());
        CHECK(plugin.CurrentPreset().bands.empty());
    }
    SetStoreEnv((store + "/introuvable").c_str());
    {
        RoomEqVst plugin(&DummyHost);
        plugin.SetSampleRate(48000.0);
        CHECK(OpenPlugin(plugin) == 1);
        CHECK(!plugin.HasPreset());
    }

    // F — sans variable (Linux : aucun magasin) : transparent
    ClearStoreEnv();
    {
        RoomEqVst plugin(&DummyHost);
        plugin.SetSampleRate(48000.0);
        CHECK(OpenPlugin(plugin) == 1);
#if !defined(_WIN32) // Windows : %APPDATA% réel peut exister sur un poste dev
        CHECK(!plugin.HasPreset());
#endif
    }

#if !defined(_WIN32)
    rmdir(store.c_str()); // nettoyage (best effort)
#endif
}

// --------------------------------------- auto-chargement des presets plats (v3.4.2)
// Second retour utilisateur réel : « le VST affiche 10 bandes mais sur la
// courbe je ne vois pas les 10 bandes, aucune différence de son ». Cause :
// le preset le plus récent du magasin était PLAT (10 bandes à 0 dB —
// l'état par défaut de l'onglet Égaliseur de l'application, EqPreset.Flat(),
// sauvegardé tel quel). Le DSP était sain : seul le CHOIX de preset était en
// cause. L'auto-chargement doit SAUTER les presets sans effet audible.

namespace {

/// Les 10 bandes exactes d'EqPreset.Flat() (31,25 Hz → 16 kHz, pas octave,
/// Q 1,1) — ce que l'onglet Égaliseur de l'application sauvegarde quand
/// l'utilisateur enregistre l'état par défaut.
const char* kFlatPreset10 = R"JSON({
  "Name": "Plat",
  "PreampDb": 0.0,
  "Bands": [
    { "Type": "Peaking", "Frequency": 31.25, "GainDb": 0.0, "Q": 1.1, "Enabled": true },
    { "Type": "Peaking", "Frequency": 62.5, "GainDb": 0.0, "Q": 1.1, "Enabled": true },
    { "Type": "Peaking", "Frequency": 125.0, "GainDb": 0.0, "Q": 1.1, "Enabled": true },
    { "Type": "Peaking", "Frequency": 250.0, "GainDb": 0.0, "Q": 1.1, "Enabled": true },
    { "Type": "Peaking", "Frequency": 500.0, "GainDb": 0.0, "Q": 1.1, "Enabled": true },
    { "Type": "Peaking", "Frequency": 1000.0, "GainDb": 0.0, "Q": 1.1, "Enabled": true },
    { "Type": "Peaking", "Frequency": 2000.0, "GainDb": 0.0, "Q": 1.1, "Enabled": true },
    { "Type": "Peaking", "Frequency": 4000.0, "GainDb": 0.0, "Q": 1.1, "Enabled": true },
    { "Type": "Peaking", "Frequency": 8000.0, "GainDb": 0.0, "Q": 1.1, "Enabled": true },
    { "Type": "Peaking", "Frequency": 16000.0, "GainDb": 0.0, "Q": 1.1, "Enabled": true }
  ]
})JSON";

std::vector<uint8_t> BytesOf(const char* text)
{
    return std::vector<uint8_t>(text, text + std::strlen(text));
}

} // namespace

static void TestAutoLoadSkipsFlat()
{
    std::puts("— auto-chargement : presets plats ignorés (v3.4.2)");

    const auto demo = LoadData("preset_demo.json");
    if (g_failures > 0)
        return;

    const std::string store = "roomeq-autoload-flat";
    MakeStoreDir(store);
    const std::string flatPath = store + "/plat.json";
    const std::string corrPath = store + "/correction.json";

    // A — le scénario EXACT du retour utilisateur : preset plat de 10 bandes
    // plus récent que la vraie correction → la correction (plus ancienne)
    // doit être chargée, pas le plat
    CHECK(WriteFileBytes(corrPath, demo));
    CHECK(WriteFileBytes(flatPath, BytesOf(kFlatPreset10)));
    StampFile(corrPath, -600);
    StampFile(flatPath, 0); // le plat est le plus récent
    SetStoreEnv(store.c_str());
    {
        RoomEqVst plugin(&DummyHost);
        plugin.SetSampleRate(48000.0);
        CHECK(OpenPlugin(plugin) == 1);
        CHECK(plugin.HasPreset());
        CHECK(plugin.CurrentPreset().name == "Salon — correction v1"); // pas le plat
        CHECK(plugin.PresetHasEffect());
        CHECK_NEAR(plugin.PreampParam(), -6.5, 1e-6);

        // preuve sonore : sortie = référence C# du preset de correction
        CheckParity("plat ignoré 48k stereo", plugin,
            "impulse_demo_48k_stereo.f32", 48000, 2, 2e-6);
    }

    // B — écran affiché pour un plat CHARGÉ EXPLICITEMENT (bouton Charger… ou
    // chunk de session) : le preset reste actif, l'éditeur doit signaler
    // « plat » (PresetHasEffect faux) — l'état est visible, pas subi
    {
        RoomEqVst plugin(&DummyHost);
        plugin.SetSampleRate(48000.0);
        std::string error;
        const auto flat = BytesOf(kFlatPreset10);
        CHECK(plugin.ApplyPresetBytes(flat.data(), flat.size(), true, error));
        CHECK(plugin.HasPreset());
        CHECK(plugin.CurrentPreset().name == "Plat");
        CHECK(plugin.CurrentPreset().bands.size() == 10);
        CHECK(!plugin.PresetHasEffect()); // 10 bandes à 0 dB : aucun effet

        // la courbe est plate (ce que l'utilisateur voyait)
        std::vector<double> curve = plugin.ResponseCurve();
        CHECK(curve.size() == 97 * 2);
        double lo = 1e9, hi = -1e9;
        for (size_t i = 0; i < curve.size() / 2; ++i) {
            lo = std::min(lo, curve[i * 2 + 1]);
            hi = std::max(hi, curve[i * 2 + 1]);
        }
        CHECK_NEAR(hi - lo, 0.0, 1e-9);
        CHECK_NEAR(lo, 0.0, 1e-9);

        // et le son traverse à l'identique (10 peaking à 0 dB = identité)
        const auto identity = MakeReferenceInput(48000, 2);
        const auto got = ProcessThrough(plugin, 48000, 2);
        const double worst = ComparePlanarToReference(got, identity, 2);
        CHECK(worst < 1e-6);
    }

    // C — bande filtrante à 0 dB (Notch) : PAS transparente — un preset qui
    // ne contient qu'un notch doit être auto-chargé
    {
        const char* notchOnly = R"JSON({
  "Name": "Anti-gratte",
  "PreampDb": 0.0,
  "Bands": [ { "Type": "Notch", "Frequency": 7350, "GainDb": 0.0, "Q": 8, "Enabled": true } ]
})JSON";
        CHECK(WriteFileBytes(flatPath, BytesOf(notchOnly)));
        StampFile(flatPath, 0);
        RoomEqVst plugin(&DummyHost);
        plugin.SetSampleRate(48000.0);
        CHECK(OpenPlugin(plugin) == 1);
        CHECK(plugin.HasPreset());
        CHECK(plugin.CurrentPreset().name == "Anti-gratte");
        CHECK(plugin.PresetHasEffect()); // un notch filtre même à 0 dB
    }

    // D — préampli seul (bandes plates mais préampli non nul) : audible
    {
        const char* preampOnly = R"JSON({
  "Name": "Attenuation",
  "PreampDb": -6.0,
  "Bands": [ { "Type": "Peaking", "Frequency": 1000, "GainDb": 0.0, "Q": 1.1, "Enabled": true } ]
})JSON";
        CHECK(WriteFileBytes(flatPath, BytesOf(preampOnly)));
        StampFile(flatPath, 0);
        RoomEqVst plugin(&DummyHost);
        plugin.SetSampleRate(48000.0);
        CHECK(OpenPlugin(plugin) == 1);
        CHECK(plugin.CurrentPreset().name == "Attenuation");
        CHECK(plugin.PresetHasEffect());
    }

    // E — magasin avec des presets plats UNIQUEMENT : transparent + bannière
    // « aucun preset » (rien d'audible à charger — pas de faux « 10 bandes »)
    CHECK(std::remove(corrPath.c_str()) == 0);
    CHECK(WriteFileBytes(flatPath, BytesOf(kFlatPreset10)));
    StampFile(flatPath, 0);
    {
        RoomEqVst plugin(&DummyHost);
        plugin.SetSampleRate(48000.0);
        CHECK(OpenPlugin(plugin) == 1);
        CHECK(!plugin.HasPreset()); // le plat est ignoré → transparent
        CHECK(plugin.CurrentPreset().bands.empty());
        CHECK(!plugin.PresetHasEffect());
    }

    CHECK(std::remove(flatPath.c_str()) == 0);
#if !defined(_WIN32)
    rmdir(store.c_str());
#endif
}



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
    TestAutoLoad();
    TestAutoLoadSkipsFlat();

    std::printf("\n%d vérifications, %d échecs\n", g_checks, g_failures);
    if (g_failures > 0) {
        std::printf("ROOMEQ VST : ÉCHEC\n");
        return 1;
    }
    std::printf("ROOMEQ VST : OK\n");
    return 0;
}
