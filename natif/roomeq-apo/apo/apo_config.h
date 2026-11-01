// RoomEq APO — configuration partagée et chaîne de traitement publiée
// (spec I-AUD-09, N-APO-01).
//
// L'application écrit la courbe active dans <ProgramData>/RoomEq/apo/
// active.json (format preset de l'application, D23 — audiodg tourne en
// LocalService : PAS d'accès aux profils utilisateurs). Chaque instance de
// l'APO possède un thread de fond qui sonde la mtime du fichier (>= 100 ms)
// et reconstruit sa chaîne HORS du chemin audio ; la publication vers le
// traitement temps réel se fait par pointeur atomique.
//
// Invariants RT (I-AUD-09 n° 5) : APOProcess n'alloue pas, ne verrouille pas,
// ne touche ni fichier ni registre — il ne fait que des lectures/écritures
// atomiques et du calcul. La couture entre deux courbes est un fondu enchaîné
// linéaire (~20 ms) : le son ne s'interrompt jamais, même à chaud.
#pragma once

#include "roomeq/dsp/eq_chain.h"
#include "roomeq/preset/preset.h"

#include <atomic>
#include <cstdint>
#include <string>
#include <thread>

namespace roomeq::apo {

/// Horodatage haute résolution d'un fichier (mtime en unités fines —
/// nanosecondes sous Linux, 100 ns sous Windows — et taille). C'est la
/// signature de changement sondée par le surveillant : la granularité
/// SECONDE de st_mtime raterait les réécritures rapprochées (l'application
/// réécrit active.json plusieurs fois par seconde pendant un glissement).
/// Implémentation Linux : apo_config.cpp ; Windows : win_compat.cpp
/// (GetFileAttributesExA, 100 ns). Retourne false si le fichier est absent.
bool RoomeqFileStamp(const char* path, long long* fineTime, long long* size);

/// Répertoire de configuration (%ProgramData%\RoomEq\apo, override
/// ROOMEQ_APO_DIR pour les tests). Déterminé une fois par processus.
std::string ConfigDir();

/// Chemin complet du fichier de courbe active.
std::string ActiveCurvePath();

/// Journal de diagnostic (<dir>\apo.log) — appelé UNIQUEMENT depuis le thread
/// de fond ou l'initialisation, jamais depuis APOProcess.
void Log(const std::string& line);

/// Résume un preset + ses drapeaux pour comparaison rapide de contenu.
struct CurveSignature {
    double preampDb = 0.0;
    bool bypass = false;
    // (les bandes sont comparées via la sérialisation de PresetDef — la
    // comparaison n'a lieu que sur le thread de fond, pas en RT)
    bool SameAs(const preset::PresetDef& other, bool otherBypass) const;
    void Assign(const preset::PresetDef& p, bool bypassFlag);
    preset::PresetDef preset;   // copie de travail du thread de fond
};

/// Chaîne publiée vers le traitement temps réel — immuable une fois publiée.
/// Référencée à la main (atomique) : le thread de fond en possède une
/// référence, le thread audio en prend une le temps d'un bloc.
struct Chain {
    dsp::EqChain eq;                 // configurée au taux/canaux de l'instance
    double rate = 48000.0;           // format de construction (diagnostic/rebuild)
    int eqChannels = 2;
    bool bypass = false;             // true = pass-through transparent
    uint64_t revision = 0;           // lue du fichier (information)
    CurveSignature signature;

    std::atomic<long> refs{1};

    void AddRef() { refs.fetch_add(1, std::memory_order_relaxed); }
    void Release()
    {
        if (refs.fetch_sub(1, std::memory_order_acq_rel) == 1)
            delete this;
    }
};

/// Surveille active.json pour une instance d'APO : un thread léger, une
/// lecture de stat() par période, reconstruction + publication atomique.
class ConfigWatcher {
public:
    /// Période de sondage (>= 100 ms pour la spec ; réduite par les tests).
    static constexpr int kDefaultPollMs = 100;

    ConfigWatcher() = default;
    ~ConfigWatcher();

    ConfigWatcher(const ConfigWatcher&) = delete;
    ConfigWatcher& operator=(const ConfigWatcher&) = delete;

    /// Démarre le thread de fond (idempotent). pollMs <= 0 → défaut.
    void Start(int pollMs = kDefaultPollMs);

    /// Arrête le thread de fond (join). Appelé au Unlock/destructeur.
    void Stop();

    /// Dernière chaîne publiée (peut être nullptr avant la première lecture
    /// réussie — l'appelant traite alors en pass-through). Incrémente la
    /// référence : relâcher par Chain::Release().
    Chain* Acquire()
    {
        Chain* c = published_.load(std::memory_order_acquire);
        if (c)
            c->AddRef();
        return c;
    }

    /// Publie une nouvelle chaîne (prend possession de la référence).
    void Publish(Chain* chain) { published_.store(chain, std::memory_order_release); }

    /// Rend une chaîne au thread de fond pour libération mémoire : le chemin
    /// temps réel (APOProcess) ne fait NI allocation NI libération — le slot
    /// est vidé par la boucle de sondage. Slot occupé (deux fins de fondu en
    /// moins d'une période, très rare) : libération directe, dégradée mais
    /// correcte.
    void RetireOnBackgroundThread(Chain* chain)
    {
        if (chain == nullptr)
            return;
        Chain* expected = nullptr;
        if (!graveyard_.compare_exchange_strong(expected, chain,
                                                std::memory_order_acq_rel))
            chain->Release();
    }

    /// Libère l'éventuelle chaîne du cimetière (thread de fond / arrêt).
    void DrainGraveyard()
    {
        if (Chain* g = graveyard_.exchange(nullptr, std::memory_order_acq_rel))
            g->Release();
    }

    /// Charge/pars le fichier UNE fois (thread de fond ou tests) ; retourne
    /// nullptr si illisible (l'appelant garde alors la chaîne courante).
    /// outBypass/outRevision renseignés si non nuls.
    static Chain* LoadOnce(double sampleRate, int channels);

    /// Révision actuellement publiée (0 si aucune) — pour les diagnostics.
    uint64_t CurrentRevision() const
    {
        Chain* c = const_cast<ConfigWatcher*>(this)->published_.load(std::memory_order_acquire);
        return c ? c->revision : 0;
    }

    // --- coutures de test -------------------------------------------------
    /// Force un passage de sondage immédiat sur le thread appelant (tests).
    bool PollOnceForTests(double sampleRate, int channels);

    /// Nombre de changements de fichier vus par le surveillant (mtime/taille),
    /// y compris les réécritures à contenu identique — pour attendre un
    /// passage de façon déterministe dans les tests.
    std::atomic<uint64_t> changesSeen_{0};

    /// Configure l'instance pour les tests (taux/canaux de la chaîne à
    /// construire lors du prochain chargement).
    void ConfigureForProcess(double sampleRate, int channels);

    /// Reconstruit la chaîne publiée au format courant (appelé au
    /// re-verrouillage : un changement de taux/canaux exige une chaîne
    /// conforme, même si le fichier de courbe n'a pas changé).
    void RebuildForFormat();

private:
    void Run(int pollMs);

    std::atomic<Chain*> published_{nullptr};
    std::atomic<Chain*> graveyard_{nullptr};
    std::atomic<bool> running_{false};
    std::atomic<bool> stopFlag_{false};
    std::atomic<long long> lastMtime_{-1};
    std::atomic<long long> lastSize_{-1};
    double rate_ = 48000.0;
    int channels_ = 2;
    std::thread thread_;
};

} // namespace roomeq::apo
