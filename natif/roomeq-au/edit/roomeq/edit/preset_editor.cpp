// RoomEq — noyau portable d'état de preset éditable : implémentation
// (v3.6.0, spec I-AUD-07). Les prédicats d'audibilité et le tri par récence
// sont les jumeaux exacts de ceux du greffon VST (RoomEqVst.cpp) : les deux
// greffons doivent choisir le même preset à l'auto-chargement et annoncer le
// même état « plat » pour un même magasin.
#include "roomeq/edit/preset_editor.h"

#include "roomeq/headphones/headphone_bank.h"
#include "roomeq/preset/preset_io.h"
#include "roomeq/preset/preset_write.h"

#include <algorithm>
#include <cmath>
#include <cstdio>
#include <cstdlib>
#include <cstring>

#if !defined(_WIN32)
#include <dirent.h>
#include <sys/stat.h>
#else
// Le greffon AU ne tourne que sous macOS ; le stub Windows garde la couche
// compilable partout (l'équivalent Windows vit dans le greffon VST).
#endif

namespace roomeq::edit {
namespace {

/// Seuil d'inaudibilité (dB) : en dessous, une bande shelving/peaking à ce
/// gain est indiscernable de l'identité à l'oreille (jumeau VST).
constexpr double kInaudibleDb = 0.05;

/// Une bande ACTIVE est-elle transparente ? Cas piège : les types filtrants
/// (Notch, LowPass, HighPass) coupent le signal MÊME à 0 dB de gain.
bool BandIsTransparent(const preset::EqBandDef& band)
{
    using dsp::FilterType;
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

/// Gain absolu maximal des bandes actives (dB).
double MaxActiveGainDb(const preset::PresetDef& preset)
{
    double maxGain = 0.0;
    for (const auto& band : preset.bands)
        if (band.enabled)
            maxGain = std::max(maxGain, std::fabs(band.gainDb));
    return maxGain;
}

/// Lit le fichier (contenu binaire complet) — jumeau POSIX du lecteur VST.
bool ReadWholeFile(const std::string& path, std::vector<uint8_t>& out)
{
    std::FILE* f = std::fopen(path.c_str(), "rb");
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

#if !defined(_WIN32)
/// Liste les presets (.json/.ffp) du répertoire, du plus récent au plus
/// ancien (nanosecondes POSIX : deux presets sauvegardés dans la même seconde
/// restent correctement ordonnés — jumeau du tri v3.4.2 du VST).
std::vector<std::string> PresetFilesByRecency(const std::string& dir)
{
    struct Candidate {
        long long stamp;
        std::string path;
    };
    std::vector<Candidate> candidates;

    if (DIR* d = opendir(dir.c_str())) {
        while (const dirent* entry = readdir(d)) {
            const std::string name = entry->d_name;
            const bool json = name.size() > 5
                && name.compare(name.size() - 5, 5, ".json") == 0;
            const bool ffp = name.size() > 4
                && name.compare(name.size() - 4, 4, ".ffp") == 0;
            if (!json && !ffp)
                continue;
            struct stat st;
            if (stat((dir + "/" + name).c_str(), &st) != 0
                || (st.st_mode & S_IFMT) != S_IFREG)
                continue;
#if defined(__APPLE__)
            // BSD : timespec de modification (macOS n'expose pas st_mtim
            // en C++17 strict — CMAKE_CXX_EXTENSIONS OFF)
            const long nanos = st.st_mtimespec.tv_nsec;
#else
            const long nanos = st.st_mtim.tv_nsec;
#endif
            candidates.push_back({ static_cast<long long>(st.st_mtime) * 1000000000LL
                    + nanos,
                dir + "/" + name });
        }
        closedir(d);
    }

    std::stable_sort(candidates.begin(), candidates.end(),
        [](const Candidate& a, const Candidate& b) { return a.stamp > b.stamp; });

    std::vector<std::string> paths;
    paths.reserve(candidates.size());
    for (auto& c : candidates)
        paths.push_back(std::move(c.path));
    return paths;
}
#endif

} // namespace

// ------------------------------------------------------------------ lecture

PresetEditor::PresetEditor()
    : preset_(std::make_shared<const preset::PresetDef>(preset::MakeIdentityPreset()))
{
}

bool PresetEditor::HasPreset() const
{
    std::lock_guard<std::mutex> lock(mutex_);
    return !bytes_.empty();
}

bool PresetEditor::PresetHasEffect() const
{
    // copie via le shared_ptr atomique : pas de verrou, cohérence par version
    const auto preset = CurrentPreset();
    if (std::fabs(preset->preampDb) > kInaudibleDb)
        return true;
    for (const auto& band : preset->bands)
        if (band.enabled && !BandIsTransparent(band))
            return true;
    return false;
}

std::vector<uint8_t> PresetEditor::Bytes() const
{
    std::lock_guard<std::mutex> lock(mutex_);
    return bytes_;
}

// ---------------------------------------------------------------- chargement

bool PresetEditor::ApplyBytes(const uint8_t* data, size_t size, double& preampOut,
    std::string& error)
{
    preampOut = 0.0;
    if (data == nullptr || size == 0) {
        error = "preset vide";
        return false;
    }

    auto parsed = std::make_shared<preset::PresetDef>();
    const auto format = preset::SniffFormat(data, size);
    bool ok = false;
    if (format == "ffp") {
        ok = preset::FromFfp(data, size, *parsed, error);
    } else {
        const std::string text(reinterpret_cast<const char*>(data), size);
        ok = preset::FromJson(text, *parsed, error);
    }
    if (!ok)
        return false; // l'état reste inchangé

    {
        std::lock_guard<std::mutex> lock(mutex_);
        preampOut = parsed->preampDb;
        bytes_.assign(data, data + size);
        // publication atomique pour le thread audio (l'ancien preset survit
        // le temps des rendus en cours via les références partagées)
        std::atomic_store(&preset_, std::shared_ptr<const preset::PresetDef>(
                                       std::move(parsed)));
    }
    version_.fetch_add(1, std::memory_order_release);

    if (notify_)
        notify_();
    return true;
}

bool PresetEditor::LoadHeadphone(size_t index, double& preampOut)
{
    const auto& bank = bank::HeadphoneBank();
    if (index >= bank.size()) {
        preampOut = 0.0;
        return false;
    }

    // conversion miroir de HeadphonePresetLibrary.ToEqPreset (clamps
    // identiques), puis application comme un preset interactif : le blob
    // devient du JSON de l'application (relisible partout)
    auto headphonePreset = std::make_shared<const preset::PresetDef>(
        bank::HeadphonePreset(bank[index]));

    {
        std::lock_guard<std::mutex> lock(mutex_);
        preampOut = headphonePreset->preampDb;
        const std::string json = preset::ToJson(*headphonePreset);
        bytes_.assign(json.begin(), json.end());
        std::atomic_store(&preset_,
            std::shared_ptr<const preset::PresetDef>(std::move(headphonePreset)));
    }
    version_.fetch_add(1, std::memory_order_release);

    if (notify_)
        notify_();
    return true;
}

bool PresetEditor::AutoLoadFromDir(const std::string& dir, double& preampOut,
    std::string& summary)
{
    preampOut = 0.0;

    // un preset est déjà là (restauration effectuée avant, double appel) :
    // l'état existant reste maître
    if (HasPreset()) {
        summary = "auto-chargement inactif : un preset est deja present";
        return false;
    }

#if defined(_WIN32)
    (void)dir;
    summary = "auto-chargement indisponible sur cette plateforme";
    return false;
#else
    if (dir.empty()) {
        summary = "auto-chargement : aucun magasin (ROOMEQ_PRESETS_DIR/HOME)";
        return false;
    }

    // du plus récent au plus ancien : un fichier refusé (corrompu, tronqué…)
    // ou SANS EFFET AUDIBLE ne bloque pas les suivants. Un preset plat —
    // bandes à 0 dB, l'état par défaut de l'onglet Égaliseur de
    // l'application — ne corrige rien (jumeau du correctif VST v3.4.2).
    for (const auto& path : PresetFilesByRecency(dir)) {
        std::vector<uint8_t> bytes;
        if (!ReadWholeFile(path, bytes))
            continue;

        preset::PresetDef candidate;
        std::string error;
        const auto format = preset::SniffFormat(bytes.data(), bytes.size());
        const bool parsed = format == "ffp"
            ? preset::FromFfp(bytes.data(), bytes.size(), candidate, error)
            : preset::FromJson(
                  std::string(reinterpret_cast<const char*>(bytes.data()), bytes.size()),
                  candidate, error);
        if (!parsed) {
            summary = "auto-chargement refuse : " + error;
            continue;
        }
        if (!(std::fabs(candidate.preampDb) > kInaudibleDb)) {
            bool audible = false;
            for (const auto& band : candidate.bands)
                if (band.enabled && !BandIsTransparent(band))
                    audible = true;
            if (!audible) {
                char line[256];
                std::snprintf(line, sizeof(line),
                    "auto-chargement saute « %s » : preset plat (%zu bandes a "
                    "0 dB, preamp %+.1f) — aucune correction",
                    candidate.name.c_str(), candidate.bands.size(),
                    candidate.preampDb);
                summary = line;
                continue;
            }
        }

        if (!ApplyBytes(bytes.data(), bytes.size(), preampOut, error)) {
            summary = "auto-chargement refuse : " + error;
            continue;
        }

        char line[256];
        std::snprintf(line, sizeof(line), "auto-chargement : « %s » (%s)",
            candidate.name.c_str(), PresetSummary(candidate).c_str());
        summary = line;
        return true;
    }

    summary = "auto-chargement : aucun preset avec effet audible dans le magasin";
    return false;
#endif
}

// ------------------------------------------------------------------ édition

void PresetEditor::CommitLocked(std::shared_ptr<const preset::PresetDef> edited)
{
    // re-sérialisation : l'état passe au JSON de l'application (format exact)
    // — un preset .ffp édité devient du JSON, relisible partout.
    const std::string json = preset::ToJson(*edited);
    bytes_.assign(json.begin(), json.end());
    std::atomic_store(&preset_, std::move(edited));
    version_.fetch_add(1, std::memory_order_release);
}

size_t PresetEditor::BandCount() const
{
    std::lock_guard<std::mutex> lock(mutex_);
    return preset_->bands.size();
}

bool PresetEditor::BandAt(size_t index, preset::EqBandDef& out) const
{
    std::lock_guard<std::mutex> lock(mutex_);
    if (index >= preset_->bands.size())
        return false;
    out = preset_->bands[index];
    return true;
}

int PresetEditor::EditBand(size_t index, const double* frequency,
    const double* gainDb, const double* q)
{
    std::lock_guard<std::mutex> lock(mutex_);
    if (index >= preset_->bands.size())
        return -1;

    auto edited = std::make_shared<preset::PresetDef>(*preset_);
    auto& band = edited->bands[index];
    if (frequency != nullptr)
        band.frequency = std::clamp(*frequency, kFreqMin, kFreqMax);
    if (gainDb != nullptr)
        band.gainDb = std::clamp(*gainDb, kGainMin, kGainMax);
    if (q != nullptr)
        band.q = std::clamp(*q, kQMin, kQMax);
    CommitLocked(std::move(edited));

    if (notify_)
        notify_();
    return static_cast<int>(index);
}

int PresetEditor::AddBand(double frequency, double gainDb)
{
    std::lock_guard<std::mutex> lock(mutex_);

    auto edited = std::make_shared<preset::PresetDef>(*preset_);
    // première bande d'un greffon transparent : le preset devient « Manuel »
    // (l'identité « Transparent » ne décrit plus l'état)
    if (edited->bands.empty() && edited->name == "Transparent")
        edited->name = "Manuel";
    preset::EqBandDef band;
    band.type = dsp::FilterType::Peaking;
    band.frequency = std::clamp(frequency, kFreqMin, kFreqMax);
    band.gainDb = std::clamp(gainDb, kGainMin, kGainMax);
    band.q = 1.1;
    band.enabled = true;
    edited->bands.push_back(band);
    const int index = static_cast<int>(edited->bands.size() - 1);
    CommitLocked(std::move(edited));

    if (notify_)
        notify_();
    return index;
}

bool PresetEditor::RemoveBand(size_t index)
{
    std::lock_guard<std::mutex> lock(mutex_);
    if (index >= preset_->bands.size())
        return false;

    auto edited = std::make_shared<preset::PresetDef>(*preset_);
    edited->bands.erase(edited->bands.begin() + static_cast<ptrdiff_t>(index));
    CommitLocked(std::move(edited));

    if (notify_)
        notify_();
    return true;
}

bool PresetEditor::SetBandEnabled(size_t index, bool enabled)
{
    std::lock_guard<std::mutex> lock(mutex_);
    if (index >= preset_->bands.size())
        return false;

    auto edited = std::make_shared<preset::PresetDef>(*preset_);
    edited->bands[index].enabled = enabled;
    CommitLocked(std::move(edited));

    if (notify_)
        notify_();
    return true;
}

bool PresetEditor::SetBandType(size_t index, dsp::FilterType type)
{
    std::lock_guard<std::mutex> lock(mutex_);
    if (index >= preset_->bands.size())
        return false;

    auto edited = std::make_shared<preset::PresetDef>(*preset_);
    edited->bands[index].type = type;
    CommitLocked(std::move(edited));

    if (notify_)
        notify_();
    return true;
}

// ------------------------------------------------------------------ divers

std::string DefaultPresetsDir()
{
    if (const char* env = std::getenv("ROOMEQ_PRESETS_DIR");
        env != nullptr && *env != '\0')
        return env;
#if !defined(_WIN32)
    if (const char* home = std::getenv("HOME"); home != nullptr && *home != '\0')
        return std::string(home) + "/.config/RoomEq/presets";
#endif
    return {};
}

std::string PresetSummary(const preset::PresetDef& preset)
{
    size_t active = 0;
    for (const auto& b : preset.bands)
        if (b.enabled)
            ++active;
    char line[192];
    std::snprintf(line, sizeof(line), "%zu bandes actives, gain max %.1f dB, "
                                     "preamp %+.1f dB",
        active, MaxActiveGainDb(preset), preset.preampDb);
    return line;
}

} // namespace roomeq::edit
