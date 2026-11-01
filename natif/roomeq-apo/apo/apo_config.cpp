// RoomEq APO — configuration partagée : implémentation (spec I-AUD-09).
#include "apo_config.h"

#include "roomeq/json/mini_json.h"
#include "roomeq/preset/preset_io.h"

#include <atomic>
#include <chrono>
#include <cstdio>
#include <cstring>
#include <cstdlib>
#include <sys/stat.h>

namespace roomeq::apo {

// ------------------------------------------------- horodatage fin (Linux)
#if !defined(_WIN32)
bool RoomeqFileStamp(const char* path, long long* fineTime, long long* size)
{
    struct stat st;
    if (::stat(path, &st) != 0)
        return false;
    *fineTime = static_cast<long long>(st.st_mtim.tv_sec) * 1000000000LL
              + static_cast<long long>(st.st_mtim.tv_nsec);
    *size = static_cast<long long>(st.st_size);
    return true;
}
#endif

// ---------------------------------------------------------------- chemins

std::string ConfigDir()
{
    static const std::string cached = [] {
        if (const char* overrideDir = std::getenv("ROOMEQ_APO_DIR"))
            if (overrideDir[0] != '\0')
                return std::string(overrideDir);
#if defined(_WIN32)
        if (const char* programData = std::getenv("ProgramData"))
            if (programData[0] != '\0')
                return std::string(programData) + "/RoomEq/apo";
#endif
        // repli (développement / harnais Linux) : dossier de travail
        return std::string("roomeq-apo-data");
    }();
    return cached;
}

std::string ActiveCurvePath() { return ConfigDir() + "/active.json"; }

// ---------------------------------------------------------------- journal

void Log(const std::string& line)
{
    const std::string path = ConfigDir() + "/apo.log";
    // cap raisonnable : au-delà d'1 Mo, on repart de zéro (le journal est un
    // outil de diagnostic terrain, pas un historique)
    struct stat st;
    bool truncate = ::stat(path.c_str(), &st) == 0 && st.st_size > 1024 * 1024;
    std::FILE* f = std::fopen(path.c_str(), truncate ? "w" : "a");
    if (!f)
        return;
    std::fprintf(f, "%s\n", line.c_str());
    std::fclose(f);
}

// ---------------------------------------------------------------- signature

bool CurveSignature::SameAs(const preset::PresetDef& other, bool otherBypass) const
{
    if (bypass != otherBypass || preampDb != other.preampDb)
        return false;
    if (preset.bands.size() != other.bands.size())
        return false;
    for (size_t i = 0; i < preset.bands.size(); ++i) {
        const auto& a = preset.bands[i];
        const auto& b = other.bands[i];
        if (a.type != b.type || a.frequency != b.frequency
            || a.gainDb != b.gainDb || a.q != b.q || a.enabled != b.enabled)
            return false;
    }
    return true;
}

void CurveSignature::Assign(const preset::PresetDef& p, bool bypassFlag)
{
    preampDb = p.preampDb;
    bypass = bypassFlag;
    preset = p;
}

// ---------------------------------------------------------------- chargement

Chain* ConfigWatcher::LoadOnce(double sampleRate, int channels)
{
    const std::string path = ActiveCurvePath();
    struct stat st;
    if (::stat(path.c_str(), &st) != 0 || st.st_size <= 0)
        return nullptr; // pas encore de courbe : pass-through

    std::FILE* f = std::fopen(path.c_str(), "rb");
    if (!f)
        return nullptr;
    std::string text(static_cast<size_t>(st.st_size) + 1, '\0');
    size_t got = std::fread(text.data(), 1, static_cast<size_t>(st.st_size), f);
    std::fclose(f);
    text.resize(got);

    // 1) drapeaux applicatifs (Bypass / Revision) — même racine que le preset
    bool bypass = false;
    uint64_t revision = 0;
    {
        json::Value root;
        std::string jerr;
        if (json::Parse(text, root, jerr) && root.IsObject()) {
            const json::Value& b = root.Get("Bypass");
            if (b.IsBool())
                bypass = b.AsBool();
            const json::Value& r = root.Get("Revision");
            if (r.IsNumber() && r.AsNumber() >= 0)
                revision = static_cast<uint64_t>(r.AsNumber());
        }
    }

    // 2) preset (format D23 : PascalCase de l'application, camelCase toléré)
    preset::PresetDef preset;
    std::string perr;
    if (!preset::FromJson(text, preset, perr)) {
        Log("active.json illisible, conservation de la courbe courante : " + perr);
        return nullptr;
    }

    auto* chain = new Chain();
    chain->bypass = bypass;
    chain->revision = revision;
    chain->rate = sampleRate;
    chain->eqChannels = channels;
    chain->signature.Assign(preset, bypass);
    chain->eq.Configure(sampleRate, channels);
    chain->eq.SetPreset(preset);
    return chain;
}

// ---------------------------------------------------------------- surveillant

ConfigWatcher::~ConfigWatcher()
{
    Stop();
    DrainGraveyard();
    if (Chain* c = published_.load(std::memory_order_acquire)) {
        c->Release(); // référence du surveillant
        published_.store(nullptr, std::memory_order_release);
    }
}

void ConfigWatcher::Start(int pollMs)
{
    bool expected = false;
    if (!running_.compare_exchange_strong(expected, true))
        return; // déjà démarré
    stopFlag_.store(false, std::memory_order_release);
    if (pollMs <= 0)
        pollMs = kDefaultPollMs;
    thread_ = std::thread(&ConfigWatcher::Run, this, pollMs);
}

void ConfigWatcher::Stop()
{
    if (!running_.load(std::memory_order_acquire))
        return;
    stopFlag_.store(true, std::memory_order_release);
    if (thread_.joinable())
        thread_.join();
    running_.store(false, std::memory_order_release);
    DrainGraveyard();
}

void ConfigWatcher::Run(int pollMs)
{
    Log("surveillant actif (periode " + std::to_string(pollMs) + " ms)");
    while (!stopFlag_.load(std::memory_order_acquire)) {
        PollOnceForTests(rate_, channels_);
        DrainGraveyard(); // libérations différées du chemin temps réel
        // sommeil fractionné pour réagir vite à l'arrêt
        for (int waited = 0; waited < pollMs && !stopFlag_.load(std::memory_order_acquire);
             waited += 10)
            std::this_thread::sleep_for(std::chrono::milliseconds(10));
    }
    DrainGraveyard();
    Log("surveillant arrete");
}

void ConfigWatcher::ConfigureForProcess(double sampleRate, int channels)
{
    rate_ = sampleRate > 0 ? sampleRate : 48000.0;
    channels_ = channels > 0 ? channels : 2;
}

void ConfigWatcher::RebuildForFormat()
{
    // Re-verrouillage avec un NOUVEAU format (taux ou canalage différents) :
    // la chaîne publiée a été construite pour l'ancien format — le cœur
    // EqChain interprète sampleCount selon SES canaux configurés, il faut
    // donc reconstruire depuis le fichier actuel, même si sa mtime n'a pas
    // changé (sinon : chaîne stéréo alimentée en mono = aucun traitement).
    Chain* current = published_.load(std::memory_order_acquire);
    Chain* fresh = LoadOnce(rate_, channels_);
    if (fresh == nullptr)
        return; // pas de courbe lisible : on garde l'existant (ou rien)
    if (current != nullptr
        && current->rate == fresh->rate && current->eqChannels == fresh->eqChannels
        && current->signature.SameAs(fresh->signature.preset, fresh->signature.bypass)) {
        fresh->Release();
        return; // déjà conforme : aucun swap (états conservés)
    }
    Log("reconstruction de la chaine pour le format courant");
    published_.store(fresh, std::memory_order_release);
    if (current != nullptr)
        current->Release();
}

bool ConfigWatcher::PollOnceForTests(double sampleRate, int channels)
{
    const std::string path = ActiveCurvePath();
    long long fineTime = 0, size = 0;
    if (!RoomeqFileStamp(path.c_str(), &fineTime, &size)) {
        // fichier absent : rien à recharger (on garde la courbe courante,
        // éventuellement aucune → pass-through). La DISPARITION compte comme
        // un changement vu (attente déterministe des tests) ; l'absence
        // persistante ne compte qu'une fois.
        const bool wasPresent = lastMtime_.load(std::memory_order_acquire) != -1;
        lastMtime_.store(-1, std::memory_order_release);
        lastSize_.store(-1, std::memory_order_release);
        if (wasPresent)
            changesSeen_.fetch_add(1, std::memory_order_release);
        return false;
    }
    if (fineTime == lastMtime_.load(std::memory_order_acquire)
        && size == lastSize_.load(std::memory_order_acquire))
        return false; // inchangé : une sonde, zéro allocation (régime établi)

    // fichier présent et CHANGÉ : charger, décider, publier — l'incrément de
    // changesSeen_ vient APRÈS la décision/store (attente déterministe : quand
    // le harnais voit le compteur bouger, la chaîne publiée est déjà à jour).
    Chain* fresh = LoadOnce(sampleRate, channels);
    lastMtime_.store(fineTime, std::memory_order_release);
    lastSize_.store(size, std::memory_order_release);

    Chain* current = published_.load(std::memory_order_acquire);
    if (fresh == nullptr) {
        Log("rechargement impossible : courbe courante conservee");
        changesSeen_.fetch_add(1, std::memory_order_release);
        return false; // illisible → on garde l'existant (son jamais coupé)
    }
    if (current != nullptr && current->signature.SameAs(fresh->signature.preset,
                                                        fresh->signature.bypass)) {
        // contenu identique (l'application réécrit le fichier à chaque
        // révision) : on NE swappe PAS — les états de filtres sont conservés,
        // la continuité échantillon par échantillon est garantie (test).
        char buf[160];
        std::snprintf(buf, sizeof buf, "reecriture a contenu identique (rev %llu) : aucun swap",
                      static_cast<unsigned long long>(fresh->revision));
        Log(buf);
        fresh->Release();
        changesSeen_.fetch_add(1, std::memory_order_release);
        return false;
    }

    char buf[160];
    std::snprintf(buf, sizeof buf, "nouvelle courbe publiee (rev %llu, %s)",
                  static_cast<unsigned long long>(fresh->revision),
                  fresh->bypass ? "bypass" : "active");
    Log(buf);

    published_.store(fresh, std::memory_order_release);
    changesSeen_.fetch_add(1, std::memory_order_release); // APRÈS le store
    if (current != nullptr)
        current->Release(); // référence du surveillant (l'audio peut encore
                            // tenir la sienne le temps d'un bloc)
    return true;
}

} // namespace roomeq::apo
