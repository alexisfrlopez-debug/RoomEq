// RoomEq AU — greffon AudioUnit « RoomEq Correction » (spec I-AUD-07) :
// implémentation. Le cœur DSP est le port exact de RoomEq.Core (parité
// vérifiée par les tests natifs) ; cette couche ne fait que l'intégration
// AudioUnit (paramètre, propriétés, état, rendu par noyau et par canal).
#include "RoomEqAu.h"

#include "roomeq/preset/preset_io.h"

#include <AudioUnit/AudioUnit.h> // AudioUnitCocoaViewInfo (kAudioUnitProperty_CocoaUI)
#include <AudioUnitSDK/AUPlugInDispatch.h> // AUBaseFactory (entrée usine)
#include <CoreFoundation/CoreFoundation.h>

#include <algorithm>
#include <cmath>
#include <cstring>
#include <string>

namespace {

/// Bornes du paramètre Préampli (dB) — plage utile d'une correction.
constexpr float kPreampMinDb = -36.0f;
constexpr float kPreampMaxDb = 36.0f;
constexpr float kPreampDefaultDb = 0.0f;

/// Nombre de points de la courbe de réponse exposée à la vue.
constexpr int kCurvePoints = 97;

/// Clé du blob preset dans l'état de session.
const CFStringRef kPresetBlobKey = CFSTR("roomeq.preset");

/// Un noyau de traitement : UNE voie, SA chaîne d'égalisation (états propres).
class RoomEqKernel final : public ausdk::AUKernelBase {
public:
    explicit RoomEqKernel(RoomEqAu& owner) : AUKernelBase(owner), owner_(owner)
    {
        chain_.Configure(owner.GetSampleRate(), 1);
    }

    void Reset() override { chain_.Reset(); }

    void Process(const Float32* inSourceP, Float32* inDestP, UInt32 inFramesToProcess,
        bool& ioSilence) noexcept override
    {
        // reconfiguration paresseuse : le chargement d'un preset publie un
        // nouveau shared_ptr + version ; la reconstruction se fait ici, au
        // début d'un bloc (action utilisateur rare, documentée spec I-AUD-07)
        const uint64_t version = owner_.CurrentPresetVersion();
        if (seenVersion_ != version) {
            if (auto preset = owner_.CurrentPreset())
                chain_.SetPreset(*preset);
            seenVersion_ = version;
        }

        // le paramètre Préampli est la seule source de vérité du gain (l'accès
        // GetParameter du noyau est temps réel sûr)
        chain_.SetPreampDb(static_cast<double>(GetParameter(RoomEqAu::kParamPreampDb)));

        if (inFramesToProcess == 0)
            return;
        if (inSourceP != inDestP)
            std::memcpy(inDestP, inSourceP,
                static_cast<size_t>(inFramesToProcess) * sizeof(Float32));

        chain_.Process(inDestP, inFramesToProcess);

        // lever le drapeau silence si la sortie n'est pas muette (queue de
        // filtre après une entrée silencieuse)
        if (ioSilence) {
            for (UInt32 i = 0; i < inFramesToProcess; ++i)
                if (inDestP[i] != 0.0f) {
                    ioSilence = false;
                    break;
                }
        }
    }

private:
    RoomEqAu& owner_;
    roomeq::dsp::EqChain chain_;
    uint64_t seenVersion_ = 0;
};

} // namespace

// ------------------------------------------------------------------ constructeur

RoomEqAu::RoomEqAu(AudioComponentInstance inInstance)
    : AUEffectBase(inInstance, true),
      preset_(std::make_shared<const roomeq::preset::PresetDef>(
          roomeq::preset::MakeIdentityPreset()))
{
    CreateElements();
    SetParameter(kParamPreampDb, kPreampDefaultDb);
}

UInt32 RoomEqAu::SupportedNumChannels(const AUChannelInfo** outInfo)
{
    static const AUChannelInfo channels[] = { { 1, 1 }, { 2, 2 } };
    if (outInfo != nullptr)
        *outInfo = const_cast<AUChannelInfo*>(channels);
    return 2; // mono et stéréo
}

bool RoomEqAu::ValidFormat(AudioUnitScope /*inScope*/, AudioUnitElement,
    const AudioStreamBasicDescription& inNewFormat)
{
    // mono ou stéréo, float 32 non entrelacé, tout taux (invariant 5) —
    // l'implémentation par défaut d'AUBase accepte N'IMPORTE QUEL nombre de
    // canaux, ce qu'auval rejette (test d'initialisation 3 entrées / 1 sortie)
    return ausdk::ASBD::IsCommonFloat32(inNewFormat)
        && (inNewFormat.mChannelsPerFrame == 1 || inNewFormat.mChannelsPerFrame == 2);
}

// ------------------------------------------------------------------ paramètres

OSStatus RoomEqAu::GetParameterInfo(AudioUnitScope inScope,
    AudioUnitParameterID inParameterID, AudioUnitParameterInfo& outParameterInfo)
{
    if (inScope != kAudioUnitScope_Global)
        return kAudioUnitErr_InvalidScope;
    if (inParameterID != kParamPreampDb)
        return kAudioUnitErr_InvalidParameter;

    // drapeaux D'ABORD : FillInParameterName ajoute HasCFNameString —
    // affecter flags après l'écraserait (warning auval « SHOULD PUBLISH NAME
    // AS CFString »)
    outParameterInfo.flags = kAudioUnitParameterFlag_IsWritable
                             | kAudioUnitParameterFlag_IsReadable;
    FillInParameterName(outParameterInfo, CFSTR("Préampli"), true);
    outParameterInfo.unit = kAudioUnitParameterUnit_Decibels;
    outParameterInfo.minValue = kPreampMinDb;
    outParameterInfo.maxValue = kPreampMaxDb;
    outParameterInfo.defaultValue = kPreampDefaultDb;
    return noErr;
}

// ------------------------------------------------------------------ propriétés

OSStatus RoomEqAu::GetPropertyInfo(AudioUnitPropertyID inID, AudioUnitScope inScope,
    AudioUnitElement inElement, UInt32& outDataSize, bool& outWritable)
{
    if (inScope == kAudioUnitScope_Global && inElement == 0) {
        switch (inID) {
        case kRoomEqPropertyPresetData:
            outDataSize = sizeof(CFDataRef);
            outWritable = true;
            return noErr;
        case kRoomEqPropertyStatus:
            outDataSize = sizeof(CFDictionaryRef);
            outWritable = false;
            return noErr;
        case kRoomEqPropertyCurve:
            outDataSize = sizeof(CFDataRef);
            outWritable = false;
            return noErr;
        case kAudioUnitProperty_CocoaUI:
            outDataSize = sizeof(AudioUnitCocoaViewInfo);
            outWritable = false;
            return noErr;
        default:
            break;
        }
    }
    return ausdk::AUEffectBase::GetPropertyInfo(inID, inScope, inElement, outDataSize,
        outWritable);
}

OSStatus RoomEqAu::GetProperty(AudioUnitPropertyID inID, AudioUnitScope inScope,
    AudioUnitElement inElement, void* outData)
{
    if (inScope == kAudioUnitScope_Global && inElement == 0) {
        switch (inID) {
        case kAudioUnitProperty_CocoaUI: {
            // vue Cocoa : classe Obj-C du même bundle (RoomEqAuView.mm)
            const auto bundle = CFBundleGetBundleWithIdentifier(
                CFSTR("fr.roomeq.RoomEqAU"));
            if (bundle == nullptr)
                return kAudioUnitErr_InvalidProperty;
            const auto viewProperty = static_cast<AudioUnitCocoaViewInfo*>(outData);
            viewProperty->mCocoaAUViewClass[0] = CFSTR("RoomEqAuView");
            viewProperty->mCocoaAUViewBundleLocation = CFBundleCopyBundleURL(bundle);
            return noErr;
        }
        case kRoomEqPropertyStatus: {
            const auto preset = CurrentPreset();
            int bands = 0;
            for (const auto& b : preset->bands)
                if (b.enabled)
                    ++bands;
            const double preamp = preset->preampDb;
            const int64_t version = static_cast<int64_t>(CurrentPresetVersion());

            const CFStringRef nameKeys[] = { CFSTR("name"), CFSTR("bands"),
                CFSTR("preampDb"), CFSTR("version") };
            // CFTypeRef = const void* : tableau SANS const supplémentaire pour
            // décayer vers const void** (exigence de CFDictionaryCreate)
            CFTypeRef values[] = {
                CFStringCreateWithCString(nullptr, preset->name.c_str(),
                    kCFStringEncodingUTF8),
                CFNumberCreate(nullptr, kCFNumberIntType, &bands),
                CFNumberCreate(nullptr, kCFNumberDoubleType, &preamp),
                CFNumberCreate(nullptr, kCFNumberSInt64Type, &version),
            };
            const void* keyPointers[] = {
                static_cast<const void*>(nameKeys[0]),
                static_cast<const void*>(nameKeys[1]),
                static_cast<const void*>(nameKeys[2]),
                static_cast<const void*>(nameKeys[3]),
            };
            const auto dict = CFDictionaryCreate(nullptr, keyPointers, values, 4,
                &kCFTypeDictionaryKeyCallBacks, &kCFTypeDictionaryValueCallBacks);
            for (const CFTypeRef v : values)
                if (v != nullptr)
                    CFRelease(v);
            *static_cast<CFDictionaryRef*>(outData) = dict;
            return noErr;
        }
        case kRoomEqPropertyCurve: {
            // courbe composite (préampli du paramètre inclus), 97 points
            // log-répartis de 20 Hz à 20 kHz, paires double petit-boutiste
            const auto preset = CurrentPreset();
            const double sampleRate = GetSampleRate() > 0.0 ? GetSampleRate() : 48000.0;
            const double preampParam = static_cast<double>(
                Globals()->GetParameter(kParamPreampDb));

            roomeq::dsp::EqChain chain; // chaîne de calcul, pas de rendu
            chain.Configure(sampleRate, 1);
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
            *static_cast<CFDataRef*>(outData) = CFDataCreate(nullptr,
                reinterpret_cast<const UInt8*>(pairs.data()),
                static_cast<CFIndex>(pairs.size() * sizeof(double)));
            return noErr;
        }
        default:
            break;
        }
    }
    return ausdk::AUEffectBase::GetProperty(inID, inScope, inElement, outData);
}

OSStatus RoomEqAu::SetProperty(AudioUnitPropertyID inID, AudioUnitScope inScope,
    AudioUnitElement inElement, const void* inData, UInt32 inDataSize)
{
    if (inScope == kAudioUnitScope_Global && inElement == 0
        && inID == kRoomEqPropertyPresetData) {
        if (inData == nullptr || inDataSize != sizeof(CFDataRef))
            return kAudioUnitErr_InvalidPropertyValue;
        const auto blob = *static_cast<const CFDataRef*>(inData);
        if (blob == nullptr || CFGetTypeID(blob) != CFDataGetTypeID())
            return kAudioUnitErr_InvalidPropertyValue;
        return ApplyPresetBytes(CFDataGetBytePtr(blob),
            static_cast<size_t>(CFDataGetLength(blob)), true);
    }
    return ausdk::AUEffectBase::SetProperty(inID, inScope, inElement, inData, inDataSize);
}

// ------------------------------------------------------------------ état

OSStatus RoomEqAu::SaveState(CFPropertyListRef* outData)
{
    const OSStatus status = ausdk::AUEffectBase::SaveState(outData);
    if (status != noErr)
        return status;

    std::lock_guard<std::mutex> lock(stateMutex_);
    if (presetBytes_.empty())
        return noErr;

    const auto dict = static_cast<CFMutableDictionaryRef>(
        const_cast<void*>(*outData));
    // mutable et immuable partagent le même identifiant de type CoreFoundation
    if (dict == nullptr || CFGetTypeID(dict) != CFDictionaryGetTypeID())
        return noErr;

    const CFDataRef blob = CFDataCreate(nullptr, presetBytes_.data(),
        static_cast<CFIndex>(presetBytes_.size()));
    if (blob == nullptr)
        return noErr;
    CFDictionarySetValue(dict, kPresetBlobKey, blob);
    CFRelease(blob);
    return noErr;
}

OSStatus RoomEqAu::RestoreState(CFPropertyListRef plist)
{
    // la base restaure d'abord les paramètres (le préampli sauvegardé par le
    // DAW fait foi), puis le blob preset est réappliqué SANS toucher au
    // paramètre
    const OSStatus status = ausdk::AUEffectBase::RestoreState(plist);
    if (status != noErr)
        return status;
    if (plist == nullptr || CFGetTypeID(plist) != CFDictionaryGetTypeID())
        return noErr;

    const auto blob = static_cast<CFDataRef>(
        CFDictionaryGetValue(static_cast<CFDictionaryRef>(plist), kPresetBlobKey));
    if (blob == nullptr)
        return noErr; // état d'avant tout chargement : preset transparent
    if (CFGetTypeID(blob) != CFDataGetTypeID())
        return kAudioUnitErr_InvalidPropertyValue;
    return ApplyPresetBytes(CFDataGetBytePtr(blob),
        static_cast<size_t>(CFDataGetLength(blob)), false);
}

// ------------------------------------------------------------------ noyaux

std::unique_ptr<ausdk::AUKernelBase> RoomEqAu::NewKernel()
{
    return std::make_unique<RoomEqKernel>(*this);
}

// ------------------------------------------------------------------ preset

OSStatus RoomEqAu::ApplyPresetBytes(const void* data, size_t size, bool updateParameter)
{
    if (data == nullptr || size == 0)
        return kAudioUnitErr_InvalidPropertyValue;

    const auto* bytes = static_cast<const uint8_t*>(data);
    std::string error;
    auto parsed = std::make_shared<roomeq::preset::PresetDef>();

    const auto format = roomeq::preset::SniffFormat(bytes, size);
    bool ok = false;
    if (format == "ffp") {
        ok = roomeq::preset::FromFfp(bytes, size, *parsed, error);
    } else {
        const std::string text(static_cast<const char*>(data), size);
        ok = roomeq::preset::FromJson(text, *parsed, error);
    }
    if (!ok)
        return kAudioUnitErr_InvalidPropertyValue;

    {
        std::lock_guard<std::mutex> lock(stateMutex_);
        preset_ = parsed;
        presetBytes_.assign(bytes, bytes + size);
    }
    presetVersion_.fetch_add(1, std::memory_order_release);

    if (updateParameter) {
        const auto value = static_cast<Float32>(std::clamp(parsed->preampDb,
            static_cast<double>(kPreampMinDb), static_cast<double>(kPreampMaxDb)));
        SetParameter(kParamPreampDb, value);
        // notifier vues et hôte : notre propriété d'état a changé (il n'existe
        // pas de propriété Apple « changement de valeur de paramètre » en AUv2
        // — les vues écoutent kRoomEqPropertyStatus)
        PropertyChanged(kRoomEqPropertyStatus, kAudioUnitScope_Global, 0);
    }
    return noErr;
}

// ------------------------------------------------------------------ entrée usine

AUSDK_COMPONENT_ENTRY(ausdk::AUBaseFactory, RoomEqAu)
