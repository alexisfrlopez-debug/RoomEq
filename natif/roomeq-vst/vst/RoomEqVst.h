// RoomEq VST — greffon VST 2 « RoomEq Correction » (spec I-AUD-08) :
// interface. Le cœur DSP est celui du greffon AU (natif/roomeq-au/core,
// parité bit à bit avec RoomEq.Core vérifiée par les tests natifs) ; cette
// couche ne fait que l'intégration VST 2 (paramètre, chunk, bypass,
// éditeur Win32, rendu par canal).
#pragma once

#include "vst2_abi.h"

#include "roomeq/dsp/eq_chain.h"
#include "roomeq/preset/preset.h"

#include <atomic>
#include <memory>
#include <mutex>
#include <string>
#include <vector>

/// Le greffon. AEffect est la PREMIÈRE chose dans l'objet : l'adresse de
/// l'objet et celle de sa structure AEffect coïncident, ce qui permet aux
/// callbacks statiques de retrouver l'instance (usage standard VST 2 —
/// la structure n'a pas de champ utilisateur).
class RoomEqVst : public vst::AEffect {
public:
    // Paramètre unique (index 0) : préamplification en dB.
    static constexpr int kParamPreampDb = 0;
    static constexpr int kNumParams = 1;
    static constexpr float kPreampMinDb = -36.0f;
    static constexpr float kPreampMaxDb = 36.0f;
    static constexpr float kPreampDefaultDb = 0.0f;

    static constexpr int32_t kUniqueId = vst::CConst('R', 'm', 'e', 'q');
    static constexpr int32_t kVersion = 3402; // 3.4.2

    // Canaux audio (stéréo, comme le master d'un DAW).
    static constexpr int kChannels = 2;

    explicit RoomEqVst(vst::HostCallback host);
    ~RoomEqVst();

    // ---- callbacks statiques de l'ABI (C, sans contexte utilisateur)
    static vst::IntPtr Dispatcher(vst::AEffect* effect, int32_t opcode,
        int32_t index, vst::IntPtr value, void* ptr, float opt);
    static void ProcessAccumulate(vst::AEffect* effect, float** inputs,
        float** outputs, int32_t sampleFrames);
    static void ProcessReplace(vst::AEffect* effect, float** inputs,
        float** outputs, int32_t sampleFrames);
    static void SetParameter(vst::AEffect* effect, int32_t index, float value);
    static float GetParameter(vst::AEffect* effect, int32_t index);

    // ---- API directe (tests natifs : sans passer par l'ABI)
    /// Applique les octets d'un preset .json/.ffp (détection par magie).
    /// updateParameter : recaler « Preamp » sur le préampli du preset
    /// (chargement interactif) — faux pour la restauration de chunk, l'hôte
    /// réapplique lui-même les paramètres sauvegardés.
    bool ApplyPresetBytes(const uint8_t* data, size_t size, bool updateParameter,
        std::string& error);

    /// Preset courant (copie). Thread interface uniquement.
    roomeq::preset::PresetDef CurrentPreset() const;

    /// Version du preset (incrémentée à chaque chargement réussi).
    uint64_t PresetVersion() const
    {
        return presetVersion_.load(std::memory_order_acquire);
    }

    /// Octets bruts du preset chargé (chunk), vides si aucun.
    std::vector<uint8_t> PresetBytes() const;

    /// Vrai si un preset est actif (fichier, chunk restauré ou
    /// auto-chargement) — faux = greffon transparent (identité).
    bool HasPreset() const;

    /// Vrai si le preset courant a un effet AUDIBLE : au moins une bande
    /// active non transparente (gain non nul, ou type filtrant — coupe/
    /// notch) ou un préampli non nul. Un preset « plat » (bandes à 0 dB,
    /// préampli 0) ne corrige rien — v3.4.2 : l'auto-chargement l'ignore et
    /// l'éditeur l'affiche explicitement (retour utilisateur : « 10 bandes
    /// affichées, courbe plate, aucune différence de son »).
    bool PresetHasEffect() const;

    float PreampParam() const
    {
        return preampParam_.load(std::memory_order_relaxed);
    }

    void SetSampleRate(double sampleRate) { sampleRate_ = sampleRate; }
    void SetBlockSize(int32_t blockSize) { blockSize_ = blockSize; }
    void SetBypassed(bool bypass) { bypass_.store(bypass, std::memory_order_relaxed); }
    bool Bypassed() const { return bypass_.load(std::memory_order_relaxed); }

    // Nom affiché du programme (≤ kMaxProgNameLen caractères).
    std::string ProgramName() const;
    void SetProgramName(const char* name);

    /// Courbe de réponse composite (préampli du paramètre inclus), 97 points
    /// log-répartis de 20 Hz à 20 kHz — paires (Hz, dB).
    std::vector<double> ResponseCurve() const;

private:
    vst::IntPtr HandleOpen();
    vst::IntPtr HandleDispatcher(int32_t opcode, int32_t index, vst::IntPtr value,
        void* ptr, float opt);
    void ProcessChannels(float** inputs, float** outputs, int32_t sampleFrames,
        bool accumulate);
    void SyncChainsFromFormat();

    /// v3.4.1 : charge automatiquement le preset le plus récent du magasin
    /// de l'application (%APPDATA%\RoomEq\presets) à l'ouverture, pour que
    /// le greffon corrige dès l'insertion sans manipulation. L'état du
    /// projet (chunk, appliqué après effOpen) reste maître.
    void TryAutoLoadFromApp();

    vst::HostCallback host_;

    // ---- état interface (mutex) / audio (atomiques)
    mutable std::mutex stateMutex_;               // preset_ + presetBytes_
    std::shared_ptr<const roomeq::preset::PresetDef> preset_;
    std::vector<uint8_t> presetBytes_;            // octets bruts du chunk
    std::string programName_ = "Init";
    std::atomic<uint64_t> presetVersion_{ 0 };
    std::atomic<float> preampParam_{ kPreampDefaultDb };
    std::atomic<bool> bypass_{ false };
    std::vector<uint8_t> chunkOut_;          // tampon stable pour effGetChunk

    // ---- configuration flux
    double sampleRate_ = 48000.0;
    int32_t blockSize_ = 512;
    int configuredChannels_ = 0;      // 0 = chaînes non encore construites
    double configuredRate_ = 0.0;

    // ---- rendu (une chaîne par canal, états propres — miroir des noyaux AU)
    roomeq::dsp::EqChain chains_[kChannels];
    uint64_t seenVersion_[kChannels] = { 0, 0 };
    std::vector<float> scratch_;      // mode accumulation uniquement
    void* editorState_ = nullptr;     // état de l'éditeur (EditorWin32.cpp)
    std::atomic<bool> loggedFirstBlock_ { false }; // diagnostic : 1er bloc

    // l'éditeur Win32 (fonctions libres) gère editorState_ lui-même
    friend bool VstEditorOpen(RoomEqVst& plugin, void* systemWindow);
    friend void VstEditorClose(RoomEqVst& plugin);
};

/// Fabrique de l'entrée usine VST 2 (définie dans RoomEqVst.cpp).
vst::AEffect* CreateRoomEqVst(vst::HostCallback host);

#if defined(_WIN32) && !defined(ROOMEQ_VST_NO_EDITOR)
/// L'éditeur Win32 existe uniquement pour le build Windows (EditorWin32.cpp).
bool VstEditorOpen(RoomEqVst& plugin, void* systemWindow);
void VstEditorClose(RoomEqVst& plugin);
void VstEditorIdle(RoomEqVst& plugin);
constexpr bool kHasEditor = true;
#else
inline bool VstEditorOpen(RoomEqVst&, void*) { return false; }
inline void VstEditorClose(RoomEqVst&) {}
inline void VstEditorIdle(RoomEqVst&) {}
constexpr bool kHasEditor = false;
#endif
