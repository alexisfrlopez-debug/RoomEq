// RoomEq — noyau portable d'état de preset ÉDITABLE (v3.6.0, spec I-AUD-07
// invariants 7-9). Partagé par le greffon AU et testable sous Linux : ce
// fichier ne dépend NI d'AudioUnit, NI de VST — l'intégration (RoomEqAu.cpp)
// se contente d'appeler ces opérations et de traduire les changements en
// notifications d'hôte. Miroir assumé de la logique d'édition du VST
// (RoomEqVst.cpp, v3.5.0) : mêmes bornes, mêmes règles de nommage, même
// re-sérialisation JSON (l'unicité du format est garantie par le cœur
// partagé roomeq::preset::ToJson).
#pragma once

#include "roomeq/dsp/filter_type.h"
#include "roomeq/preset/preset.h"

#include <atomic>
#include <cstdint>
#include <functional>
#include <memory>
#include <mutex>
#include <string>
#include <vector>

namespace roomeq::edit {

/// Bornes de l'application (EqBand/HeadphoneFilter) : identiques de bout en
/// bout pour qu'un preset édité dans le greffon se comporte comme dans l'app.
constexpr double kFreqMin = 20.0, kFreqMax = 20000.0;
constexpr double kGainMin = -24.0, kGainMax = 24.0;
constexpr double kQMin = 0.1, kQMax = 20.0;

/// État de preset éditable, thread-safe :
///   • thread audio : CurrentPreset()/Version() SANS verrou (shared_ptr
///     atomique + compteur de version — l'ancien preset reste valide le temps
///     du rendu en cours) ;
///   • thread interface : toutes les mutations (verrou interne), chacune
///     re-sérialise le preset au JSON exact de l'application et incrémente la
///     version ; le callback `notify` est appelé HORS verrou après chaque
///     mutation réussie (l'intégration y notifie l'hôte/ses vues).
class PresetEditor {
public:
    /// Appelé (thread interface) après chaque changement d'état réussi.
    using NotifyFn = std::function<void()>;

    PresetEditor();

    // ---- lecture (thread audio, sans verrou)
    /// Preset courant — la référence partagée le maintient en vie le temps du
    /// rendu, même si une mutation le remplace entre-temps.
    std::shared_ptr<const preset::PresetDef> CurrentPreset() const
    {
        return std::atomic_load(&preset_);
    }

    /// Version de l'état (incrémentée à chaque mutation réussie).
    uint64_t Version() const { return version_.load(std::memory_order_acquire); }

    // ---- lecture (thread interface)
    bool HasPreset() const;          ///< octets de preset présents (sinon transparent)
    bool PresetHasEffect() const;    ///< au moins une correction audible (sinon plat)
    std::vector<uint8_t> Bytes() const; ///< blob brut (JSON après édition) — état de session

    // ---- chargement
    /// Applique les octets d'un preset .json/.ffp (détection par magie).
    /// Échec : false + message (jamais d'exception, l'état reste inchangé).
    /// preampOut : préampli du preset appliqué (l'appelant recale son
    /// paramètre s'il s'agit d'un chargement interactif).
    bool ApplyBytes(const uint8_t* data, size_t size, double& preampOut,
        std::string& error);

    /// Applique la correction du i-ème modèle de la banque embarquée
    /// (roomeq::bank — miroir de HeadphonePresetLibrary.ToEqPreset).
    bool LoadHeadphone(size_t index, double& preampOut);

    /// Charge le preset le plus récent AVEC effet audible du répertoire
    /// (auto-chargement à l'insertion — invariant 9 : les presets plats sont
    /// sautés, l'état du projet DAW reste maître car il est restauré APRÈS).
    /// summary : une ligne de journal (motif d'échec ou preset choisi).
    bool AutoLoadFromDir(const std::string& dir, double& preampOut,
        std::string& summary);

    // ---- édition directe (bornes de l'application appliquées ici)
    size_t BandCount() const;
    bool BandAt(size_t index, preset::EqBandDef& out) const;
    /// Modifie une bande (pointeur nul = champ inchangé) — index édité ou −1.
    int EditBand(size_t index, const double* frequency, const double* gainDb,
        const double* q);
    /// Ajoute une bande peaking (Q 1.1, active) — index créé ou −1. La
    /// première bande d'un greffon transparent renomme le preset « Manuel ».
    int AddBand(double frequency, double gainDb);
    bool RemoveBand(size_t index);
    bool SetBandEnabled(size_t index, bool enabled);
    bool SetBandType(size_t index, dsp::FilterType type);

    void SetNotify(NotifyFn fn) { notify_ = std::move(fn); }

private:
    /// Sérialise le preset édité (JSON application), remplace l'état,
    /// incrémente la version. APPELÉ SOUS mutex_.
    void CommitLocked(std::shared_ptr<const preset::PresetDef> edited);

    mutable std::mutex mutex_;                              // preset_ + bytes_
    std::shared_ptr<const preset::PresetDef> preset_;       // lecture audio : atomique
    std::vector<uint8_t> bytes_;                            // blob (chunk/état)
    std::atomic<uint64_t> version_{ 1 };
    NotifyFn notify_;                                       // appelé hors verrou
};

/// Répertoire des presets de l'application pour CE greffon : variable
/// ROOMEQ_PRESETS_DIR en priorité (tests, installation portable), sinon
/// $HOME/.config/RoomEq/presets (magasin de l'application sur macOS —
/// Environment.SpecialFolder.ApplicationData de .NET sur Unix). Chaîne vide
/// si indéterminable (pas de HOME) : l'auto-chargement devient inactif.
std::string DefaultPresetsDir();

/// Libellé résumant un preset pour le journal (« nom », bandes actives,
/// gain max, préampli) — miroir des lignes du journal VST.
std::string PresetSummary(const preset::PresetDef& preset);

} // namespace roomeq::edit
