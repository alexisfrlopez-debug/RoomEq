// RoomEq AU — greffon AudioUnit « RoomEq Correction » (spec I-AUD-07 v2).
// AUv2 'aufx' / 'Rmeq' / 'Rmeq' : applique un preset RoomEq (JSON application
// ou .ffp Pro-Q 3) sur un flux stéréo ou mono, ÉDITABLE directement dans le
// greffon (v3.6.0 — parité VST 3.5.x). Paramètre 0 « Préampli » (dB), bypass
// standard, état de session (blob preset + paramètres). L'état de preset
// vivant (chargement, édition, banque casques, auto-chargement) est le noyau
// portable roomeq::edit::PresetEditor — cette couche ne fait que l'intégration
// AudioUnit (propriétés, paramètre, état, rendu par noyau et par canal).
#pragma once

#include "roomeq/dsp/eq_chain.h"
#include "roomeq/edit/preset_editor.h"
#include "roomeq/preset/preset.h"

#include <AudioUnitSDK/AUEffectBase.h>

#include <atomic>
#include <cstdint>
#include <memory>
#include <vector>

/// Propriétés custom du greffon (vues / état / édition — spec I-AUD-07 v2).
enum : AudioUnitPropertyID {
    /// (set) CFData = octets d'un fichier preset .json ou .ffp.
    kRoomEqPropertyPresetData = 0x52504454, // 'RPDT'
    /// (get) CFDictionary { name, bands, preampDb, version }.
    kRoomEqPropertyStatus = 0x52505354, // 'RPST'
    /// (get) CFData = 97 paires double (fréquence Hz, réponse dB), 20 Hz–20 kHz.
    kRoomEqPropertyCurve = 0x52506376, // 'RPcv'
    /// (get) CFData = instantané binaire « RBP1 » du preset éditable :
    /// préampli, nom, bandes complètes (types compris) — dessin + édition
    /// de la vue (les points de bandes ne se déduisent pas de la courbe).
    kRoomEqPropertyBands = 0x52506264, // 'RPbd'
    /// (set) CFData { u32 index, u8 masque (1=fréq 2=gain 4=Q), f64, f64, f64 }.
    kRoomEqPropertyEditBand = 0x52506562, // 'RPeb'
    /// (set) CFData { f64 fréquence, f64 gain } → nouvelle bande peaking.
    kRoomEqPropertyAddBand = 0x52506162, // 'RPab'
    /// (set) CFData { u32 index, u32 opération (0 suppr., 1 act/dés, 2 type),
    ///                u32 type FilterType }.
    kRoomEqPropertyBandOp = 0x5250626f, // 'RPbo'
    /// (set) CFData { u32 index } → correction de la banque embarquée.
    kRoomEqPropertyLoadHeadphone = 0x52506c68, // 'RPlh'
    /// (get) CFData { f32 peak entrée, f32 peak sortie } — VU-mètres.
    kRoomEqPropertyLevels = 0x52506c76, // 'RPlv'
};

class RoomEqAu final : public ausdk::AUEffectBase {
public:
    static constexpr AudioUnitParameterID kParamPreampDb = 0;

    explicit RoomEqAu(AudioComponentInstance inInstance);
    ~RoomEqAu() override;

    // ---- identification / format
    UInt32 SupportedNumChannels(const AUChannelInfo** outInfo) override;
    bool ValidFormat(AudioUnitScope inScope, AudioUnitElement inElement,
        const AudioStreamBasicDescription& inNewFormat) override;
    bool StreamFormatWritable(AudioUnitScope, AudioUnitElement) override { return true; }
    OSStatus Initialize() override; // journal du format réel

    // ---- paramètres
    OSStatus GetParameterInfo(AudioUnitScope inScope, AudioUnitParameterID inParameterID,
        AudioUnitParameterInfo& outParameterInfo) override;

    // ---- propriétés (CocoaUI + propriétés custom)
    OSStatus GetPropertyInfo(AudioUnitPropertyID inID, AudioUnitScope inScope,
        AudioUnitElement inElement, UInt32& outDataSize, bool& outWritable) override;
    OSStatus GetProperty(AudioUnitPropertyID inID, AudioUnitScope inScope,
        AudioUnitElement inElement, void* outData) override;
    OSStatus SetProperty(AudioUnitPropertyID inID, AudioUnitScope inScope,
        AudioUnitElement inElement, const void* inData, UInt32 inDataSize) override;

    // ---- état de session (projet DAW)
    OSStatus SaveState(CFPropertyListRef* outData) override;
    OSStatus RestoreState(CFPropertyListRef plist) override;

    // ---- preset courant (noyaux : lecture sans verrou via l'éditeur)
    /// Référence partagée : reste valide le temps du rendu en cours, même si
    /// une mutation la remplace entre-temps.
    std::shared_ptr<const roomeq::preset::PresetDef> CurrentPreset() const
    {
        return editor_.CurrentPreset();
    }

    /// Version du preset (incrémentée à chaque mutation réussie).
    uint64_t CurrentPresetVersion() const
    {
        return editor_.Version();
    }

    /// Vrai si un preset est actif (fichier, session ou auto-chargement).
    bool HasPreset() const { return editor_.HasPreset(); }

    /// Vrai si le preset courant a un effet AUDIBLE (sinon : plat).
    bool PresetHasEffect() const { return editor_.PresetHasEffect(); }

    // ---- diagnostic du flux audio (v3.6.0, miroir VST) : atomiques écrits
    // par les noyaux au rendu, lus par la vue — jamais de verrou.
    /// Peak récent de l'entrée (0…1, échantillon absolu maximal du bloc).
    float InputLevel() const { return inPeak_.load(std::memory_order_relaxed); }
    /// Peak récent de la sortie (0…1).
    float OutputLevel() const { return outPeak_.load(std::memory_order_relaxed); }
    /// Blocs audio traités depuis l'ouverture (journal de fermeture).
    uint64_t BlocksProcessed() const
    {
        return blocks_.load(std::memory_order_relaxed);
    }

    // ---- enregistrement du diagnostic (appelés par le noyau du canal 0 au
    // rendu — le noyau vit dans un namespace anonyme du .cpp, l'accès direct
    // aux membres privés est donc impossible : petits mutateurs atomiques)
    void RecordInputPeak(float peak) { inPeak_.store(peak, std::memory_order_relaxed); }
    void RecordOutputPeak(float peak) { outPeak_.store(peak, std::memory_order_relaxed); }
    void RecordBlockProcessed() { blocks_.fetch_add(1, std::memory_order_relaxed); }
    /// Vrai une seule fois (premier bloc — ligne de journal).
    bool ClaimFirstBlockLog()
    {
        return !loggedFirstBlock_.exchange(true, std::memory_order_relaxed);
    }

protected:
    std::unique_ptr<ausdk::AUKernelBase> NewKernel() override;

private:
    /// Analyse et applique les octets d'un preset ; `updateParameter` règle le
    /// paramètre Préampli sur le préampli du preset (chargement utilisateur)
    /// ou le laisse intact (restauration de session : le paramètre sauvegardé
    /// par le DAW fait foi). Retourne noErr ou kAudioUnitErr_*.
    OSStatus ApplyPresetBytes(const void* data, size_t size, bool updateParameter);

    roomeq::edit::PresetEditor editor_;

    /// Vrai pendant RestoreState : les notifications de mutation sont
    /// suspendues (l'état du projet s'applique silencieusement).
    bool restoring_ = false;

    // ---- diagnostic du flux audio (écrit par le noyau du canal 0)
    std::atomic<float> inPeak_{ 0.0f };
    std::atomic<float> outPeak_{ 0.0f };
    std::atomic<uint64_t> blocks_{ 0 };
    std::atomic<bool> loggedFirstBlock_{ false };
};
