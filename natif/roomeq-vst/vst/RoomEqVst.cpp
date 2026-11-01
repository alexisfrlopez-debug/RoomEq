// RoomEq VST — greffon VST 2 « RoomEq Correction » (spec I-AUD-08) :
// implémentation. Le cœur DSP est celui du greffon AU (natif/roomeq-au/core,
// parité bit à bit avec RoomEq.Core) ; cette couche fait l'intégration VST 2.
//
// Modèle de threads (miroir du greffon AU, spec I-AUD-07) :
//   • le thread audio appelle processReplacing — aucune allocation, aucun
//     lock SAUF si la version du preset a changé (rare : chargement
//     interactif, reconstruction au début du bloc) ;
//   • le thread interface (hôte/éditeur) touche preset_/presetBytes_ sous
//     stateMutex_ ; les paramètres sont des atomiques ;
//   • le contrat VST 2 sérialise setSampleRate/setBlockSize/mainsChanged
//     avec process — accès simples pour la configuration du flux.
#include "RoomEqVst.h"

#include "editor_geometry.h"
#include "headphone_bank.h"
#include "preset_write.h"
#include "roomeq/preset/preset_io.h"

#include <algorithm>
#include <cstdarg>
#include <cmath>
#include <cstdio>
#include <cstring>
#include <mutex>
#include <string>
#include <vector>

#if defined(_WIN32) && !defined(ROOMEQ_VST_NO_EDITOR)
#include <direct.h> // _mkdir (journal)
#endif

#if defined(_WIN32)
#ifndef NOMINMAX
#define NOMINMAX // std::min/std::max/std::clamp doivent rester les nôtres
#endif
#include <windows.h> // FindFirstFileW (auto-chargement du magasin)
#else
#include <dirent.h>
#include <sys/stat.h>
#endif

namespace {

// ------------------------------------------------------------- journalisme

/// Journal best-effort (diagnostic des vieux hôtes 32 bits — le greffon est
/// instancié puis fermé par le scanner du DAW, impossible à instrumenter
/// autrement côté utilisateur). Une entrée = un fopen/append/fclose : sûr
/// même si plusieurs hôtes chargent le greffon en parallèle.
void Log(const char* fmt, ...) noexcept
{
#ifdef ROOMEQ_VST_DISABLE_LOG
    (void)fmt;
#else
    char path[512];
#if defined(_WIN32)
    const char* root = std::getenv("LOCALAPPDATA");
    if (root == nullptr)
        root = "C:";
    std::snprintf(path, sizeof(path), "%s\\RoomEq\\logs", root);
    _mkdir(path); // best effort (existe après installation de l'application)
    std::snprintf(path, sizeof(path), "%s\\RoomEq\\logs\\roomeq-vst.log", root);
#else
    std::snprintf(path, sizeof(path), "/tmp/roomeq-vst.log");
#endif
    std::FILE* f = std::fopen(path, "a");
    if (f == nullptr)
        return;
    char line[256];
    va_list args;
    va_start(args, fmt);
    std::vsnprintf(line, sizeof(line), fmt, args);
    va_end(args);
    std::fprintf(f, "%s\n", line);
    std::fclose(f);
#endif
}

/// Copie tronquée avec terminateur (les tampons ABI sont bornés).
template <size_t N>
void CopyBounded(char (&dst)[N], const char* text)
{
    std::snprintf(dst, N, "%s", text);
}

/// Nom de preset raccourci pour un nom de programme (≤ 23 caractères).
std::string Shorten(const std::string& text, size_t maxChars)
{
    if (text.size() <= maxChars)
        return text;
    return text.substr(0, maxChars - 1) + "~";
}

// --------------------------------------------------- auto-chargement (v3.4.1)
// Retour utilisateur réel (Live 9, premier test 32 bits) : « aucune
// différence de son quand le greffon est activé ». Cause : le greffon est
// TRANSPARENT tant qu'aucun preset n'est chargé (identité par défaut) et
// l'état « Preset : Transparent » en petit gris passait inaperçu. Trois
// mesures : auto-chargement du magasin de l'application à l'ouverture,
// bannière explicite dans l'éditeur, journal du premier bloc audio.
//
// v3.4.2 — second retour : « 10 bandes affichées mais courbe plate, aucune
// différence de son ». Cause : le magasin contenait un preset PLAT (10
// bandes à 0 dB — l'état par défaut de l'onglet Égaliseur de l'application,
// EqPreset.Flat(), sauvegardé tel quel) plus récent que la vraie correction.
// L'auto-chargement prenait le plus récent sans regarder son contenu. Le
// DSP était sain (parité bit à bit vérifiée) : c'est le CHOIX de preset qui
// était en cause. Correction : l'auto-chargement saute les presets sans
// effet audible et l'éditeur marque les bandes sur la courbe + signale les
// presets plats.

/// Seuil d'inaudibilité (dB) : en dessous, une bande shelving/peaking à ce
/// gain est indiscernable de l'identité à l'oreille.
constexpr double kInaudibleDb = 0.05;

/// Une bande ACTIVE est-elle transparente ? Cas piège : les types filtrants
/// (Notch, LowPass, HighPass) coupent le signal MÊME à 0 dB de gain — ils ne
/// sont donc jamais transparents.
bool BandIsTransparent(const roomeq::preset::EqBandDef& band)
{
    using roomeq::dsp::FilterType;
    switch (band.type) {
    case FilterType::Notch:
    case FilterType::LowPass:
    case FilterType::HighPass:
        return false;
    case FilterType::Peaking:
    case FilterType::LowShelf:
    case FilterType::HighShelf:
        return std::fabs(band.gainDb) <= kInaudibleDb;
    }
    return std::fabs(band.gainDb) <= kInaudibleDb; // valeur inconnue du switch
}

/// Un preset a-t-il un effet AUDIBLE ? (bande active non transparente ou
/// préampli non nul) — un preset plat ne corrige rien : ni courbe visible,
/// ni différence de son. Partagé par l'auto-chargement (choix du fichier),
/// le journal du premier bloc et l'éditeur (mention explicite).
bool PresetIsAudible(const roomeq::preset::PresetDef& preset)
{
    if (std::fabs(preset.preampDb) > kInaudibleDb)
        return true;
    for (const auto& band : preset.bands)
        if (band.enabled && !BandIsTransparent(band))
            return true;
    return false;
}

/// Gain absolu maximal des bandes actives (dB) — pour l'affichage et le
/// journal (résume l'ampleur de la correction d'un coup d'œil).
double MaxActiveGainDb(const roomeq::preset::PresetDef& preset)
{
    double maxGain = 0.0;
    for (const auto& band : preset.bands)
        if (band.enabled)
            maxGain = std::max(maxGain, std::fabs(band.gainDb));
    return maxGain;
}

/// Répertoire des presets de l'application (magasin PresetStore de
/// l'application, cf. RoomEq.Core/Io/Stores.cs : %APPDATA%\RoomEq\presets).
/// La variable ROOMEQ_PRESETS_DIR a priorité (tests natifs, installation
/// portable). Retourne faux si aucun répertoire pertinent (Linux sans
/// variable : le greffon .so de test reste transparent).
bool ResolvePresetsDir(std::wstring& dir)
{
#if defined(_WIN32)
    if (const wchar_t* env = _wgetenv(L"ROOMEQ_PRESETS_DIR");
        env != nullptr && *env != L'\0') {
        dir = env;
        return true;
    }
    if (const wchar_t* appdata = _wgetenv(L"APPDATA");
        appdata != nullptr && *appdata != L'\0') {
        dir = std::wstring(appdata) + L"\\RoomEq\\presets";
        return true;
    }
    return false;
#else
    // POSIX (tests) : chemin étroit converti vers large (ASCII attendu).
    const char* env = std::getenv("ROOMEQ_PRESETS_DIR");
    if (env == nullptr || *env == '\0')
        return false;
    dir.assign(env, env + std::strlen(env));
    return true;
#endif
}

/// Lit le fichier (contenu binaire complet). Chemin large sous Windows.
bool ReadWholeFile(const std::wstring& path, std::vector<uint8_t>& out)
{
#if defined(_WIN32)
    std::FILE* f = _wfopen(path.c_str(), L"rb");
#else
    std::FILE* f = std::fopen(std::string(path.begin(), path.end()).c_str(), "rb");
#endif
    if (f == nullptr)
        return false;
    std::fseek(f, 0, SEEK_END);
    const long size = std::ftell(f);
    std::fseek(f, 0, SEEK_SET);
    if (size <= 0 || size > (64 << 20)) { // garde-fou : 64 Mo maximum
        std::fclose(f);
        return false;
    }
    out.resize(static_cast<size_t>(size));
    const size_t read = std::fread(out.data(), 1, out.size(), f);
    std::fclose(f);
    return read == out.size();
}

/// Liste les presets (.json/.ffp) du répertoire, du plus récent au plus
/// ancien. Répertoire absent ou illisible : liste vide — jamais d'exception,
/// jamais d'échec bloquant pour l'ouverture.
std::vector<std::wstring> PresetFilesByRecency(const std::wstring& dir)
{
    struct Candidate {
        long long stamp;
        std::wstring path;
    };
    std::vector<Candidate> candidates;

#if defined(_WIN32)
    for (const wchar_t* pattern : { L"\\*.json", L"\\*.ffp" }) {
        WIN32_FIND_DATAW fd;
        const HANDLE find = FindFirstFileW((dir + pattern).c_str(), &fd);
        if (find == INVALID_HANDLE_VALUE)
            continue;
        do {
            if ((fd.dwFileAttributes & FILE_ATTRIBUTE_DIRECTORY) != 0)
                continue;
            ULARGE_INTEGER stamp;
            stamp.LowPart = fd.ftLastWriteTime.dwLowDateTime;
            stamp.HighPart = fd.ftLastWriteTime.dwHighDateTime;
            candidates.push_back({ static_cast<long long>(stamp.QuadPart),
                dir + L"\\" + fd.cFileName });
        } while (FindNextFileW(find, &fd) != 0);
        FindClose(find);
    }
#else
    const std::string narrow(dir.begin(), dir.end());
    if (DIR* d = opendir(narrow.c_str())) {
        while (const dirent* entry = readdir(d)) {
            const std::string name = entry->d_name;
            const bool json = name.size() > 5
                && name.compare(name.size() - 5, 5, ".json") == 0;
            const bool ffp = name.size() > 4
                && name.compare(name.size() - 4, 4, ".ffp") == 0;
            if (!json && !ffp)
                continue;
            struct stat st;
            if (stat((narrow + "/" + name).c_str(), &st) != 0
                || (st.st_mode & S_IFMT) != S_IFREG)
                continue;
            std::wstring wide(name.begin(), name.end());
            // nanosecondes (POSIX.1-2008) : deux presets sauvegardés dans
            // la même seconde restent correctement ordonnés (v3.4.2)
            candidates.push_back({ static_cast<long long>(st.st_mtime) * 1000000000LL
                    + st.st_mtim.tv_nsec,
                dir + L'/' + wide });
        }
        closedir(d);
    }
#endif

    // du plus récent au plus ancien (à égalité : ordre de parcours)
    std::stable_sort(candidates.begin(), candidates.end(),
        [](const Candidate& a, const Candidate& b) {
            return a.stamp > b.stamp;
        });

    std::vector<std::wstring> paths;
    paths.reserve(candidates.size());
    for (auto& c : candidates)
        paths.push_back(std::move(c.path));
    return paths;
}

} // namespace

// ------------------------------------------------------------------ entrée usine

RoomEqVst::RoomEqVst(vst::HostCallback host) : host_(host)
{
    // AEffect (base) : champs dans l'ordre EXACT de l'ABI.
    magic = vst::kEffectMagic;
    dispatcher = &RoomEqVst::Dispatcher;
    process = &RoomEqVst::ProcessAccumulate;
    setParameter = &RoomEqVst::SetParameter;
    getParameter = &RoomEqVst::GetParameter;
    numPrograms = 1;
    numParams = kNumParams;
    numInputs = kChannels;
    numOutputs = kChannels;
    flags = vst::kEffFlagsCanReplacing | vst::kEffFlagsProgramChunks;
    if (kHasEditor)
        flags |= vst::kEffFlagsHasEditor;
    resvd1 = 0;
    resvd2 = 0;
    initialDelay = 0;
    uniqueID = kUniqueId;
    version = kVersion;
    processReplacing = &RoomEqVst::ProcessReplace;
    processDoubleReplacing = nullptr;
    std::memset(future, 0, sizeof(future));

    preset_ = std::make_shared<const roomeq::preset::PresetDef>(
        roomeq::preset::MakeIdentityPreset());
}

RoomEqVst::~RoomEqVst()
{
    // l'éditeur ne doit pas survivre au greffon (fenêtre enfant orphaned)
    if (kHasEditor)
        VstEditorClose(*this);
}

vst::AEffect* CreateRoomEqVst(vst::HostCallback host)
{
    if (host == nullptr)
        return nullptr;
    return new RoomEqVst(host);
}

// ------------------------------------------------------------------ callbacks

vst::IntPtr RoomEqVst::Dispatcher(vst::AEffect* effect, int32_t opcode,
    int32_t index, vst::IntPtr value, void* ptr, float opt)
{
    if (effect == nullptr || effect->magic != vst::kEffectMagic)
        return 0;
    return static_cast<RoomEqVst*>(effect)->HandleDispatcher(opcode, index,
        value, ptr, opt);
}

void RoomEqVst::SetParameter(vst::AEffect* effect, int32_t index, float value)
{
    if (effect == nullptr || effect->magic != vst::kEffectMagic)
        return;
    auto* self = static_cast<RoomEqVst*>(effect);
    if (index != kParamPreampDb)
        return;
    const float clamped = std::clamp(value, kPreampMinDb, kPreampMaxDb);
    self->preampParam_.store(clamped, std::memory_order_relaxed);
}

float RoomEqVst::GetParameter(vst::AEffect* effect, int32_t index)
{
    if (effect == nullptr || effect->magic != vst::kEffectMagic
        || index != kParamPreampDb)
        return 0.0f;
    return static_cast<RoomEqVst*>(effect)->PreampParam();
}

// ------------------------------------------------------------------ dispatcher

vst::IntPtr RoomEqVst::HandleDispatcher(int32_t opcode, int32_t index,
    vst::IntPtr value, void* ptr, float opt)
{
    // index (banque/programme des chunks, index de paramètre…) n'est pas
    // discriminant ici : un seul programme, un seul paramètre.
    (void)index;
    switch (opcode) {
    case vst::kEffOpen:
        return HandleOpen();

    case vst::kEffClose:
        // Journal de close enrichi (v3.5.0) : le nombre de blocs traités
        // tranche à distance « l'audio ne passe pas » (0 bloc) vs « passe
        // sans effet audible » (preset plat) vs « corrige ».
        Log("close : %llu blocs traites", static_cast<unsigned long long>(
                                          blocksProcessed_.load(
                                              std::memory_order_relaxed)));
        delete this;
        return 0;

    case vst::kEffSetProgram:
        return value == 0 ? 1 : 0; // un seul programme

    case vst::kEffGetProgram:
        return 0;

    case vst::kEffSetProgramName:
        if (ptr != nullptr)
            SetProgramName(static_cast<const char*>(ptr));
        return 1;

    case vst::kEffGetProgramName:
        if (ptr != nullptr)
            CopyBounded(*static_cast<char (*)[vst::kMaxProgNameLen]>(ptr),
                ProgramName().c_str());
        return 1;

    case vst::kEffGetParamLabel:
        if (ptr != nullptr)
            CopyBounded(*static_cast<char (*)[vst::kMaxParamStrLen]>(ptr), "dB");
        return 1;

    case vst::kEffGetParamDisplay:
        if (ptr != nullptr) {
            char text[vst::kMaxParamStrLen];
            std::snprintf(text, sizeof(text), "%.1f", PreampParam());
            CopyBounded(*static_cast<char (*)[vst::kMaxParamStrLen]>(ptr), text);
        }
        return 1;

    case vst::kEffGetParamName:
        // kVstMaxParamStrLen = 8 : « Préampli » ne tient pas en UTF-8
        // (« é » = 2 octets) — libellé ASCII stable entre hôtes.
        if (ptr != nullptr)
            CopyBounded(*static_cast<char (*)[vst::kMaxParamStrLen]>(ptr), "Preamp");
        return 1;

    case vst::kEffSetSampleRate:
        SetSampleRate(static_cast<double>(opt));
        SyncChainsFromFormat(); // sérialisé avec process (contrat VST 2)
        return 1;

    case vst::kEffSetBlockSize:
        SetBlockSize(static_cast<int32_t>(value));
        if (static_cast<size_t>(value) > scratch_.size())
            scratch_.resize(static_cast<size_t>(value)); // mode accumulation
        return 1;

    case vst::kEffMainsChanged:
        // 0 = suspend : remise à zéro des états (départ propre au resume,
        // équivalent du Reset AU après un déplacement du curseur du DAW).
        if (value == 0) {
            for (auto& chain : chains_)
                chain.Reset();
        }
        return 1;

    case vst::kEffEditGetRect:
        // v3.5.1 — le rect DOIT couvrir exactement la fenêtre que crée
        // VstEditorOpen (EditorWin32.cpp utilise les mêmes constantes de
        // editor_geometry.h). Retour réel v3.5.0 : le rect annonçait
        // 520×240 pour une fenêtre de 560×320 → le DAW coupait la rangée
        // des contrôles (chargement, listes casque, appliquer).
        if (ptr != nullptr && kHasEditor) {
            static vst::ERect rect = roomeq_editor::EditorRect();
            *static_cast<vst::ERect**>(ptr) = &rect;
            return 1;
        }
        return 0;

    case vst::kEffEditOpen:
        if (ptr != nullptr && kHasEditor && VstEditorOpen(*this, ptr)) {
            Log("editor open");
            return 1;
        }
        return 0;

    case vst::kEffEditClose:
        if (kHasEditor)
            VstEditorClose(*this);
        return 1;

    case vst::kEffEditIdle:
        if (kHasEditor)
            VstEditorIdle(*this);
        return 0;

    case vst::kEffGetChunk: {
        // [index] 0 = bank, 1 = programme — un seul programme, même blob.
        if (ptr == nullptr)
            return 0;
        std::lock_guard<std::mutex> lock(stateMutex_);
        if (presetBytes_.empty()) {
            *static_cast<void**>(ptr) = nullptr;
            return 0;
        }
        chunkOut_ = presetBytes_; // stable jusqu'au prochain GetChunk
        *static_cast<void**>(ptr) = chunkOut_.data();
        return static_cast<vst::IntPtr>(chunkOut_.size());
    }

    case vst::kEffSetChunk: {
        // [ptr] données, [value] taille, [index] 0 bank / 1 programme.
        const auto* data = static_cast<const uint8_t*>(ptr);
        const auto size = static_cast<size_t>(value);
        if (data == nullptr || size == 0) {
            // chunk vide = aucun preset chargé : transparent.
            std::lock_guard<std::mutex> lock(stateMutex_);
            preset_ = std::make_shared<const roomeq::preset::PresetDef>(
                roomeq::preset::MakeIdentityPreset());
            presetBytes_.clear();
            programName_ = "Init";
            presetVersion_.fetch_add(1, std::memory_order_release);
            return 1;
        }
        std::string error;
        const bool ok = ApplyPresetBytes(data, size, false, error);
        if (!ok)
            Log("setChunk refuse (%zu octets) : %s", size, error.c_str());
        return ok ? 1 : 0;
    }

    case vst::kEffGetPlugCategory:
        return vst::kPlugCategoryEffect;

    case vst::kEffSetBypass:
        SetBypassed(value != 0);
        return 1;

    case vst::kEffGetEffectName:
        if (ptr != nullptr)
            CopyBounded(*static_cast<char (*)[vst::kMaxEffectNameLen]>(ptr),
                "RoomEq Correction");
        return 1;

    case vst::kEffGetVendorString:
        if (ptr != nullptr)
            CopyBounded(*static_cast<char (*)[vst::kMaxVendorStrLen]>(ptr),
                "RoomEq");
        return 1;

    case vst::kEffGetProductString:
        if (ptr != nullptr)
            CopyBounded(*static_cast<char (*)[vst::kMaxProductStrLen]>(ptr),
                "RoomEq Correction");
        return 1;

    case vst::kEffGetVendorVersion:
        return kVersion;

    case vst::kEffCanDo: {
        const auto* query = static_cast<const char*>(ptr);
        if (query != nullptr && std::strcmp(query, "bypass") == 0)
            return 1;
        return 0; // « don't know »
    }

    case vst::kEffGetTailSize:
        return 0;

    case vst::kEffGetVstVersion:
        return vst::kVstVersion;

    case vst::kEffStartProcess:
    case vst::kEffStopProcess:
        return 1;

    default:
        return 0;
    }
}

vst::IntPtr RoomEqVst::HandleOpen()
{
    Log("open (VST %d, editor=%d)", vst::kVstVersion, kHasEditor ? 1 : 0);
    SyncChainsFromFormat();
    TryAutoLoadFromApp();
    return 1;
}

void RoomEqVst::TryAutoLoadFromApp()
{
    // L'état du projet reste maître : l'hôte applique effSetChunk APRÈS
    // effOpen — une session restaurée écrase toujours l'auto-chargement,
    // et un chunk vide (projet sauvegardé sans preset) ramène le
    // transparent. Idem si un preset est déjà là (open après SetChunk,
    // double open d'un hôte bavard).
    if (HasPreset())
        return;

    std::wstring dir;
    if (!ResolvePresetsDir(dir)) {
        Log("auto-chargement : aucun magasin (ROOMEQ_PRESETS_DIR/APPDATA)");
        return;
    }

    // du plus récent au plus ancien : un fichier refusé (corrompu, tronqué
    // par une sauvegarde interrompue…) ou SANS EFFET AUDIBLE (v3.4.2) ne
    // bloque pas les suivants. Un preset plat — bandes à 0 dB, l'état par
    // défaut de l'onglet Égaliseur de l'application — ne corrige rien : le
    // charger donnerait « 10 bandes affichées, courbe plate, aucune
    // différence de son », exactement le retour utilisateur v3.4.1.
    for (const auto& path : PresetFilesByRecency(dir)) {
        std::vector<uint8_t> bytes;
        if (!ReadWholeFile(path, bytes))
            continue;

        // analyse AVANT application : un candidat écarté ne touche ni le
        // paramètre de préampli ni les notifications de l'hôte
        roomeq::preset::PresetDef candidate;
        std::string error;
        const auto format = roomeq::preset::SniffFormat(bytes.data(), bytes.size());
        const bool parsed = format == "ffp"
            ? roomeq::preset::FromFfp(bytes.data(), bytes.size(), candidate, error)
            : roomeq::preset::FromJson(
                  std::string(reinterpret_cast<const char*>(bytes.data()), bytes.size()),
                  candidate, error);
        if (!parsed) {
            Log("auto-chargement refuse : %s", error.c_str());
            continue;
        }
        if (!PresetIsAudible(candidate)) {
            Log("auto-chargement saute « %s » : preset plat (%zu bandes a "
                "0 dB, preamp %+.1f) — aucune correction",
                candidate.name.c_str(), candidate.bands.size(),
                candidate.preampDb);
            continue;
        }

        if (!ApplyPresetBytes(bytes.data(), bytes.size(), true, error)) {
            Log("auto-chargement refuse : %s", error.c_str());
            continue;
        }

        size_t active = 0;
        for (const auto& b : candidate.bands)
            if (b.enabled)
                ++active;
        Log("auto-chargement : « %s » (%zu bandes actives, gain max %.1f dB, "
            "preamp %+.1f dB)",
            candidate.name.c_str(), active, MaxActiveGainDb(candidate),
            candidate.preampDb);
        return;
    }

    Log("auto-chargement : aucun preset avec effet audible dans le magasin");
}

// ------------------------------------------------------------------ preset

bool RoomEqVst::ApplyPresetBytes(const uint8_t* data, size_t size,
    bool updateParameter, std::string& error)
{
    if (data == nullptr || size == 0) {
        error = "preset vide";
        return false;
    }

    auto parsed = std::make_shared<roomeq::preset::PresetDef>();
    const auto format = roomeq::preset::SniffFormat(data, size);
    bool ok = false;
    if (format == "ffp") {
        ok = roomeq::preset::FromFfp(data, size, *parsed, error);
    } else {
        const std::string text(reinterpret_cast<const char*>(data), size);
        ok = roomeq::preset::FromJson(text, *parsed, error);
    }
    if (!ok) {
        Log("preset refuse : %s", error.c_str());
        return false;
    }

    double preampOfPreset = 0.0;
    {
        std::lock_guard<std::mutex> lock(stateMutex_);
        preampOfPreset = parsed->preampDb;
        preset_ = std::move(parsed);
        presetBytes_.assign(data, data + size);
        programName_ = Shorten(preset_->name,
            static_cast<size_t>(vst::kMaxProgNameLen) - 1);
    }
    presetVersion_.fetch_add(1, std::memory_order_release);

    if (updateParameter) {
        const float value = static_cast<float>(std::clamp(preampOfPreset,
            static_cast<double>(kPreampMinDb), static_cast<double>(kPreampMaxDb)));
        preampParam_.store(value, std::memory_order_relaxed);
        // notifier l'hôte : automatisation + rafraîchissement de l'affichage
        if (host_ != nullptr) {
            host_(this, vst::kAudioMasterAutomate, kParamPreampDb, 0, nullptr,
                value);
            host_(this, vst::kAudioMasterUpdateDisplay, 0, 0, nullptr, 0.0f);
        }
    }
    Log("preset charge (%zu octets, format %.*s)", size,
        static_cast<int>(format.size()), format.data());
    return true;
}

roomeq::preset::PresetDef RoomEqVst::CurrentPreset() const
{
    std::lock_guard<std::mutex> lock(stateMutex_);
    return *preset_;
}

// ------------------------------------------------------- édition (v3.5.0)

namespace {
/// Bornes de l'application (EqBand/HeadphoneFilter) : identiques de bout en
/// bout pour qu'un preset édité dans le greffon se comporte comme dans l'app.
constexpr double kFreqMin = 20.0, kFreqMax = 20000.0;
constexpr double kGainMin = -24.0, kGainMax = 24.0;
constexpr double kQMin = 0.1, kQMax = 20.0;
} // namespace

void RoomEqVst::CommitEditedPresetLocked(
    std::shared_ptr<const roomeq::preset::PresetDef> edited)
{
    // re-sérialisation : le chunk passe au JSON de l'application (format
    // exact) — un preset .ffp édité devient du JSON, relisible partout.
    const std::string json = roomeq_vst::PresetToJson(*edited);
    presetBytes_.assign(json.begin(), json.end());
    preset_ = std::move(edited);
    presetVersion_.fetch_add(1, std::memory_order_release);
}

void RoomEqVst::NotifyHostDisplayChanged()
{
    if (host_ != nullptr)
        host_(this, vst::kAudioMasterUpdateDisplay, 0, 0, nullptr, 0.0f);
}

size_t RoomEqVst::BandCount() const
{
    std::lock_guard<std::mutex> lock(stateMutex_);
    return preset_->bands.size();
}

bool RoomEqVst::BandAt(size_t index, roomeq::preset::EqBandDef& out) const
{
    std::lock_guard<std::mutex> lock(stateMutex_);
    if (index >= preset_->bands.size())
        return false;
    out = preset_->bands[index];
    return true;
}

int RoomEqVst::EditBand(size_t index, const double* frequency, const double* gainDb,
    const double* q)
{
    std::lock_guard<std::mutex> lock(stateMutex_);
    if (index >= preset_->bands.size())
        return -1;

    auto edited = std::make_shared<roomeq::preset::PresetDef>(*preset_);
    auto& band = edited->bands[index];
    if (frequency != nullptr)
        band.frequency = std::clamp(*frequency, kFreqMin, kFreqMax);
    if (gainDb != nullptr)
        band.gainDb = std::clamp(*gainDb, kGainMin, kGainMax);
    if (q != nullptr)
        band.q = std::clamp(*q, kQMin, kQMax);
    CommitEditedPresetLocked(std::move(edited));

    NotifyHostDisplayChanged();
    return static_cast<int>(index);
}

int RoomEqVst::AddBand(double frequency, double gainDb)
{
    std::lock_guard<std::mutex> lock(stateMutex_);

    auto edited = std::make_shared<roomeq::preset::PresetDef>(*preset_);
    // première bande d'un greffon transparent : le preset devient « Manuel »
    // (l'identité « Transparent » ne décrit plus l'état).
    if (edited->bands.empty() && edited->name == "Transparent") {
        edited->name = "Manuel";
        programName_ = "Manuel";
    }
    roomeq::preset::EqBandDef band;
    band.type = roomeq::dsp::FilterType::Peaking;
    band.frequency = std::clamp(frequency, kFreqMin, kFreqMax);
    band.gainDb = std::clamp(gainDb, kGainMin, kGainMax);
    band.q = 1.1;
    band.enabled = true;
    edited->bands.push_back(band);
    const int index = static_cast<int>(edited->bands.size() - 1);
    CommitEditedPresetLocked(std::move(edited));

    NotifyHostDisplayChanged();
    return index;
}

bool RoomEqVst::RemoveBand(size_t index)
{
    std::lock_guard<std::mutex> lock(stateMutex_);
    if (index >= preset_->bands.size())
        return false;

    auto edited = std::make_shared<roomeq::preset::PresetDef>(*preset_);
    edited->bands.erase(edited->bands.begin() + static_cast<ptrdiff_t>(index));
    CommitEditedPresetLocked(std::move(edited));

    NotifyHostDisplayChanged();
    return true;
}

bool RoomEqVst::SetBandEnabled(size_t index, bool enabled)
{
    std::lock_guard<std::mutex> lock(stateMutex_);
    if (index >= preset_->bands.size())
        return false;

    auto edited = std::make_shared<roomeq::preset::PresetDef>(*preset_);
    edited->bands[index].enabled = enabled;
    CommitEditedPresetLocked(std::move(edited));

    NotifyHostDisplayChanged();
    return true;
}

bool RoomEqVst::SetBandType(size_t index, roomeq::dsp::FilterType type)
{
    std::lock_guard<std::mutex> lock(stateMutex_);
    if (index >= preset_->bands.size())
        return false;

    auto edited = std::make_shared<roomeq::preset::PresetDef>(*preset_);
    edited->bands[index].type = type;
    CommitEditedPresetLocked(std::move(edited));

    NotifyHostDisplayChanged();
    return true;
}

void RoomEqVst::SetPreampDb(double db)
{
    const float value = static_cast<float>(
        std::clamp(db, static_cast<double>(kPreampMinDb),
            static_cast<double>(kPreampMaxDb)));
    preampParam_.store(value, std::memory_order_relaxed);
    if (host_ != nullptr) {
        host_(this, vst::kAudioMasterAutomate, kParamPreampDb, 0, nullptr, value);
        host_(this, vst::kAudioMasterUpdateDisplay, 0, 0, nullptr, 0.0f);
    }
}

// -------------------------------------------------- banque casques (v3.5.0)

size_t RoomEqVst::HeadphoneCount() const
{
    return roomeq_vst::HeadphoneBank().size();
}

std::string RoomEqVst::HeadphoneLabel(size_t index) const
{
    const auto& bank = roomeq_vst::HeadphoneBank();
    if (index >= bank.size())
        return {};
    return bank[index].brand + " " + bank[index].model;
}

bool RoomEqVst::LoadHeadphone(size_t index)
{
    const auto& bank = roomeq_vst::HeadphoneBank();
    if (index >= bank.size())
        return false;

    // conversion miroir de HeadphonePresetLibrary.ToEqPreset (clamps
    // identiques), puis application comme un preset interactif
    auto preset = std::make_shared<const roomeq::preset::PresetDef>(
        roomeq_vst::HeadphonePreset(bank[index]));

    double preampOfPreset = 0.0;
    size_t bandCount = 0;
    std::string name;
    {
        std::lock_guard<std::mutex> lock(stateMutex_);
        preampOfPreset = preset->preampDb;
        bandCount = preset->bands.size();
        name = preset->name;
        const std::string json = roomeq_vst::PresetToJson(*preset);
        presetBytes_.assign(json.begin(), json.end());
        preset_ = std::move(preset);
        programName_ = Shorten("Casque",
            static_cast<size_t>(vst::kMaxProgNameLen) - 1);
    }
    presetVersion_.fetch_add(1, std::memory_order_release);

    SetPreampDb(preampOfPreset);
    Log("preset casque charge : « %s » (%zu bandes, preamp %+.1f dB)",
        name.c_str(), bandCount, preampOfPreset);
    return true;
}

std::vector<uint8_t> RoomEqVst::PresetBytes() const
{
    std::lock_guard<std::mutex> lock(stateMutex_);
    return presetBytes_;
}

bool RoomEqVst::HasPreset() const
{
    std::lock_guard<std::mutex> lock(stateMutex_);
    return !presetBytes_.empty();
}

bool RoomEqVst::PresetHasEffect() const
{
    std::lock_guard<std::mutex> lock(stateMutex_);
    return PresetIsAudible(*preset_);
}

std::string RoomEqVst::ProgramName() const
{
    std::lock_guard<std::mutex> lock(stateMutex_);
    return programName_;
}

void RoomEqVst::SetProgramName(const char* name)
{
    if (name == nullptr)
        return;
    std::lock_guard<std::mutex> lock(stateMutex_);
    programName_ = Shorten(name,
        static_cast<size_t>(vst::kMaxProgNameLen) - 1);
}


// ------------------------------------------------------------------ courbe

std::vector<double> RoomEqVst::ResponseCurve() const
{
    constexpr int kCurvePoints = 97; // comme la vue du greffon AU

    std::shared_ptr<const roomeq::preset::PresetDef> preset;
    {
        std::lock_guard<std::mutex> lock(stateMutex_);
        preset = preset_;
    }
    const double rate = sampleRate_ > 0.0 ? sampleRate_ : 48000.0;
    const double preampParam = static_cast<double>(PreampParam());

    roomeq::dsp::EqChain chain; // chaîne de calcul, pas de rendu
    chain.Configure(rate, 1);
    chain.SetPreset(*preset);

    std::vector<double> pairs(static_cast<size_t>(kCurvePoints) * 2);
    const double logMin = std::log10(20.0), logMax = std::log10(20000.0);
    for (int i = 0; i < kCurvePoints; ++i) {
        const double f = std::pow(10.0,
            logMin + (logMax - logMin) * i / (kCurvePoints - 1));
        pairs[static_cast<size_t>(i) * 2] = f;
        pairs[static_cast<size_t>(i) * 2 + 1] = chain.ResponseDb(f)
                                                + (preampParam - preset->preampDb);
    }
    return pairs;
}

// ------------------------------------------------------------------ rendu

void RoomEqVst::SyncChainsFromFormat()
{
    // Reconstruire si le format a changé (premier bloc ou reconfiguration).
    if (configuredChannels_ == kChannels && configuredRate_ == sampleRate_)
        return;
    for (auto& chain : chains_)
        chain.Configure(sampleRate_, 1);
    configuredChannels_ = kChannels;
    configuredRate_ = sampleRate_;
}

void RoomEqVst::ProcessChannels(float** inputs, float** outputs,
    int32_t sampleFrames, bool accumulate)
{
    if (sampleFrames <= 0 || inputs == nullptr || outputs == nullptr)
        return;

    SyncChainsFromFormat();

    // reconfiguration paresseuse du preset : version atomique d'abord, mutex
    // seulement si un chargement a eu lieu (rare — action utilisateur)
    const uint64_t version = PresetVersion();
    std::shared_ptr<const roomeq::preset::PresetDef> pending;
    bool needRebuild = false;
    for (int c = 0; c < kChannels; ++c)
        if (seenVersion_[c] != version)
            needRebuild = true;
    if (needRebuild) {
        std::lock_guard<std::mutex> lock(stateMutex_);
        pending = preset_;
    }

    const double preamp = static_cast<double>(PreampParam());
    const bool bypass = Bypassed();

    // diagnostic : UNE ligne au premier bloc rend compte de l'état réel du
    // rendu (retour utilisateur « aucune différence de son » → le journal
    // distingue « pas de preset », « preset plat » et « correction
    // appliquée » sans aller-retour)
    if (!loggedFirstBlock_.exchange(true, std::memory_order_relaxed)) {
        const roomeq::preset::PresetDef preset = CurrentPreset();
        size_t active = 0;
        for (const auto& b : preset.bands)
            if (b.enabled)
                ++active;
        Log("process : %.0f Hz, preset « %s », %zu bandes actives, gain max "
            "%.1f dB, preamp %+.1f dB, effet audible %d, bypass %d",
            sampleRate_, preset.name.c_str(), active, MaxActiveGainDb(preset),
            preamp, PresetHasEffect() ? 1 : 0, bypass ? 1 : 0);
    }

    for (int c = 0; c < kChannels; ++c) {
        if (needRebuild && pending != nullptr) {
            chains_[c].SetPreset(*pending);
            seenVersion_[c] = version;
        }
        chains_[c].SetPreampDb(preamp);
        chains_[c].SetBypass(bypass);

        const float* src = inputs[c];
        float* dst = outputs[c];
        if (src == nullptr || dst == nullptr)
            continue;

        // VU-mètre (v3.5.0) : peak d'entrée avant traitement
        if (c == 0) {
            float peak = 0.0f;
            for (int32_t i = 0; i < sampleFrames; ++i)
                peak = std::max(peak, std::fabs(src[i]));
            inPeak_.store(peak, std::memory_order_relaxed);
        }

        if (!accumulate) {
            // remplacer : traitement en place après copie éventuelle
            if (src != dst)
                std::memcpy(dst, src, static_cast<size_t>(sampleFrames) * sizeof(float));
            chains_[c].Process(dst, static_cast<size_t>(sampleFrames));
        } else {
            // accumulation (process, déprécié) : out += traitement(in)
            if (static_cast<size_t>(sampleFrames) > scratch_.size())
                scratch_.resize(static_cast<size_t>(sampleFrames));
            std::memcpy(scratch_.data(), src,
                static_cast<size_t>(sampleFrames) * sizeof(float));
            chains_[c].Process(scratch_.data(), static_cast<size_t>(sampleFrames));
            for (int32_t i = 0; i < sampleFrames; ++i)
                dst[i] += scratch_[static_cast<size_t>(i)];
        }

        // VU-mètre : peak de sortie après traitement (canal 0 seulement,
        // l'éditeur affiche une barre stéréo par max des deux canaux)
        if (c == 0) {
            float peak = 0.0f;
            for (int32_t i = 0; i < sampleFrames; ++i)
                peak = std::max(peak, std::fabs(dst[i]));
            outPeak_.store(peak, std::memory_order_relaxed);
        }
    }
    blocksProcessed_.fetch_add(1, std::memory_order_relaxed);
}

void RoomEqVst::ProcessReplace(vst::AEffect* effect, float** inputs,
    float** outputs, int32_t sampleFrames)
{
    if (effect == nullptr || effect->magic != vst::kEffectMagic)
        return;
    static_cast<RoomEqVst*>(effect)->ProcessChannels(inputs, outputs,
        sampleFrames, false);
}

void RoomEqVst::ProcessAccumulate(vst::AEffect* effect, float** inputs,
    float** outputs, int32_t sampleFrames)
{
    if (effect == nullptr || effect->magic != vst::kEffectMagic)
        return;
    static_cast<RoomEqVst*>(effect)->ProcessChannels(inputs, outputs,
        sampleFrames, true);
}

// ------------------------------------------------------------------ exports

#if defined(_WIN32)
#define ROOMEQ_EXPORT extern "C" __declspec(dllexport)
#else
#define ROOMEQ_EXPORT extern "C" __attribute__((visibility("default")))
#endif

/// Entrée usine VST 2 moderne (tous les hôtes ≥ VST 2.0 l'utilisent).
ROOMEQ_EXPORT vst::AEffect* VSTPluginMain(vst::HostCallback host)
{
    if (host == nullptr)
        return nullptr;
    // saluer le hôte (usage standard : effet nul pour la requête de version)
    const vst::IntPtr version =
        host(nullptr, vst::kAudioMasterVersion, 0, 0, nullptr, 0.0f);
    if (version < 1)
        Log("hote sans audioMasterVersion (%d) — chargement quand meme",
            static_cast<int>(version));
    return CreateRoomEqVst(host);
}

/// Entrée historique (hôtes antérieurs à VST 2.0). Le C++ interdit un
/// `main` de signature différente : le symbole est posé par étiquette
/// d'assembleur — le nom exporté reste exactement « main », sans trait de
/// soulignement, comme l'exige GetProcAddress. (Exclu des exécutables de
/// test, qui ont leur propre main — le mini-hôte valide l'export via
/// dlopen sur le module.)
#if !defined(ROOMEQ_VST_NO_LEGACY_MAIN)
#if defined(_WIN32)
// Windows : l'export d'un symbole littéral « main » déclenche l'attachement
// de l'amorçage EXE du CRT MinGW (scrnsave/WinMain) — le symbole garde donc
// son nom propre et l'export « main » est posé par ALIAS dans le .def
// (build-vst.sh). Sous Linux, l'étiquette d'assembleur suffit (mini-hôte).
extern "C" vst::AEffect* roomeq_vst_legacy_main(vst::HostCallback host);
extern "C" vst::AEffect* roomeq_vst_legacy_main(vst::HostCallback host)
{
    return VSTPluginMain(host);
}
#else
extern "C" vst::AEffect* roomeq_vst_legacy_main(vst::HostCallback host)
    __asm__("main");
extern "C" vst::AEffect* roomeq_vst_legacy_main(vst::HostCallback host)
{
    return VSTPluginMain(host);
}
#endif
#endif
