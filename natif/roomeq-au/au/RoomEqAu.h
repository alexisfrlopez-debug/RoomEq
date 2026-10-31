// RoomEq AU — greffon AudioUnit « RoomEq Correction » (spec I-AUD-07).
// AUv2 'aufx' / 'Rmeq' / 'Rmeq' : applique un preset RoomEq (JSON application
// ou .ffp Pro-Q 3) sur un flux stéréo ou mono. Paramètre 0 « Préampli » (dB),
// bypass standard, état de session (blob preset + paramètres).
#pragma once

#include "roomeq/dsp/eq_chain.h"
#include "roomeq/preset/preset.h"

#include <AudioUnitSDK/AUEffectBase.h>

#include <atomic>
#include <cstdint>
#include <memory>
#include <mutex>
#include <vector>

/// Propriétés custom du greffon (vues / état).
enum : AudioUnitPropertyID {
    /// (set) CFData = octets d'un fichier preset .json ou .ffp.
    kRoomEqPropertyPresetData = 0x52504454, // 'RPDT'
    /// (get) CFDictionary { name, bands, preampDb, version }.
    kRoomEqPropertyStatus = 0x52505354, // 'RPST'
    /// (get) CFData = 97 paires double (fréquence Hz, réponse dB), 20 Hz–20 kHz.
    kRoomEqPropertyCurve = 0x52506376, // 'RPcv'
};

class RoomEqAu final : public ausdk::AUEffectBase {
public:
    static constexpr AudioUnitParameterID kParamPreampDb = 0;

    explicit RoomEqAu(AudioComponentInstance inInstance);

    // ---- identification / format
    UInt32 SupportedNumChannels(const AUChannelInfo** outInfo) override;
    bool ValidFormat(AudioUnitScope inScope, AudioUnitElement inElement,
        const AudioStreamBasicDescription& inNewFormat) override;
    bool StreamFormatWritable(AudioUnitScope, AudioUnitElement) override { return true; }

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

    // ---- preset courant
    /// Lecture temps réel sans verrou (l'ancien preset reste valide le temps
    /// du rendu en cours : la référence partagée le maintient en vie).
    std::shared_ptr<const roomeq::preset::PresetDef> CurrentPreset() const
    {
        return std::atomic_load(&preset_);
    }

    /// Version du preset (incrémentée à chaque chargement).
    uint64_t CurrentPresetVersion() const
    {
        return presetVersion_.load(std::memory_order_acquire);
    }

protected:
    std::unique_ptr<ausdk::AUKernelBase> NewKernel() override;

private:
    friend class RoomEqKernel;

    /// Analyse et applique les octets d'un preset ; `updateParameter` règle le
    /// paramètre Préampli sur le préampli du preset (chargement utilisateur)
    /// ou le laisse intact (restauration de session : le paramètre sauvegardé
    /// par le DAW fait foi).
    /// Retourne noErr ou une erreur kAudioUnitErr_*.
    OSStatus ApplyPresetBytes(const void* data, size_t size, bool updateParameter);

    // état protégé par verrou (écrit par l'UI / la restauration, jamais lu au rendu)
    std::shared_ptr<const roomeq::preset::PresetDef> preset_;
    std::atomic<uint64_t> presetVersion_{ 1 };
    std::mutex stateMutex_;
    std::vector<uint8_t> presetBytes_; // blob brut pour SaveState
};
