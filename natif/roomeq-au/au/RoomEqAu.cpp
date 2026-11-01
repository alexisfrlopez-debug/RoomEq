// RoomEq AU — greffon AudioUnit « RoomEq Correction » (spec I-AUD-07 v2) :
// implémentation. Le cœur DSP est le port exact de RoomEq.Core (parité
// vérifiée par les tests natifs) ; l'état de preset éditable vit dans le
// noyau portable roomeq::edit::PresetEditor (testé sous Linux) ; cette couche
// ne fait que l'intégration AudioUnit (paramètre, propriétés, état, rendu par
// noyau et par canal, VU-mètres, journal de diagnostic).
#include "RoomEqAu.h"

#include "roomeq/preset/preset_io.h"

#include <AudioUnit/AudioUnit.h> // AudioUnitCocoaViewInfo (kAudioUnitProperty_CocoaUI)
#include <AudioUnitSDK/AUPlugInDispatch.h> // AUBaseFactory (entrée usine)
#include <CoreFoundation/CoreFoundation.h>

#include <algorithm>
#include <cmath>
#include <cstdarg>
#include <cstdio>
#include <cstdlib>
#include <cstring>
#include <string>
#include <vector>

#if !defined(_WIN32)
#include <sys/stat.h> // mkdir (journal)
#endif

#ifndef ROOMEQ_AU_VERSION
#define ROOMEQ_AU_VERSION "dev"
#endif

namespace {

// ------------------------------------------------------------- constantes

/// Bornes du paramètre Préampli (dB) — plage utile d'une correction.
constexpr float kPreampMinDb = -36.0f;
constexpr float kPreampMaxDb = 36.0f;
constexpr float kPreampDefaultDb = 0.0f;

/// Nombre de points de la courbe de réponse exposée à la vue.
constexpr int kCurvePoints = 97;

/// Clé du blob preset dans l'état de session.
const CFStringRef kPresetBlobKey = CFSTR("roomeq.preset");

// ------------------------------------------------------------- journalisme

/// Journal best-effort (miroir de roomeq-vst.log côté Windows — le greffon
/// est instancié puis fermé par le scanner du DAW, impossible à instrumenter
/// autrement côté utilisateur). Une entrée = un fopen/append/fclose : sûr
/// même si plusieurs hôtes chargent le greffon en parallèle.
/// macOS : ~/Library/Logs/RoomEq/roomeq-au.log (créé au besoin).
void Log(const char* fmt, ...) noexcept
{
#ifdef ROOMEQ_AU_DISABLE_LOG
    (void)fmt;
#else
    char path[512];
#if defined(__APPLE__)
    const char* home = std::getenv("HOME");
    if (home == nullptr || *home == '\0')
        return;
    std::snprintf(path, sizeof(path), "%s/Library/Logs/RoomEq", home);
    mkdir(path, 0755); // best effort (Library/Logs existe sur tout macOS)
    std::snprintf(path, sizeof(path), "%s/Library/Logs/RoomEq/roomeq-au.log", home);
#else
    std::snprintf(path, sizeof(path), "/tmp/roomeq-au.log");
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

// ------------------------------------------------------ payloads binaires
// Les propriétés d'édition échangent de petits blobs binaires (petit boutien
// natif, même processus greffon/vue) : lecture bornée à chaque champ, jamais
// d'accès hors zone, payload trop court = refus.

/// Lecteur borné d'un payload de propriété.
struct PayloadReader {
    const uint8_t* data;
    size_t size;
    size_t pos = 0;

    bool Read(void* out, size_t count)
    {
        if (count > size - pos)
            return false;
        std::memcpy(out, data + pos, count);
        pos += count;
        return true;
    }
    bool U32(uint32_t& v) { return Read(&v, sizeof(v)); }
    bool F64(double& v) { return Read(&v, sizeof(v)); }
};

void AppendU32(std::vector<uint8_t>& out, uint32_t v)
{
    const auto* p = reinterpret_cast<const uint8_t*>(&v);
    out.insert(out.end(), p, p + sizeof(v));
}

void AppendF64(std::vector<uint8_t>& out, double v)
{
    const auto* p = reinterpret_cast<const uint8_t*>(&v);
    out.insert(out.end(), p, p + sizeof(v));
}

void AppendStr(std::vector<uint8_t>& out, const std::string& text)
{
    const size_t len = text.size() > 200 ? 200 : text.size(); // borné
    out.push_back(static_cast<uint8_t>(len));
    out.insert(out.end(), text.begin(), text.begin() + static_cast<ptrdiff_t>(len));
}

/// Valide un argument de SetProperty : pointeur vers un CFData non vide.
CFDataRef GetCfData(const void* inData, UInt32 inDataSize)
{
    if (inData == nullptr || inDataSize != sizeof(CFDataRef))
        return nullptr;
    const auto blob = *static_cast<const CFDataRef*>(inData);
    if (blob == nullptr || CFGetTypeID(blob) != CFDataGetTypeID())
        return nullptr;
    return blob;
}

/// Un noyau de traitement : UNE voie, SA chaîne d'égalisation (états propres).
/// Le noyau du canal 0 alimente en plus le diagnostic : VU-mètres entrée/
/// sortie, compteur de blocs, ligne « premier bloc » du journal (miroir du
/// greffon VST — elle tranche à distance « l'audio ne passe pas » vs « passe
/// sans effet audible »).
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
        // reconfiguration paresseuse : une mutation de preset publie un
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

        const bool meter = GetChannelNum() == 0; // diagnostic : canal 0

        // VU-mètre : peak d'entrée avant traitement
        if (meter) {
            float peak = 0.0f;
            for (UInt32 i = 0; i < inFramesToProcess; ++i) {
                const float a = std::fabs(inSourceP[i]);
                if (a > peak)
                    peak = a;
            }
            owner_.inPeak_.store(peak, std::memory_order_relaxed);

            // UNE ligne au premier bloc rend compte de l'état réel du rendu
            if (!owner_.loggedFirstBlock_.exchange(true, std::memory_order_relaxed)) {
                const auto preset = owner_.CurrentPreset();
                size_t active = 0;
                double maxGain = 0.0;
                for (const auto& b : preset->bands) {
                    if (!b.enabled)
                        continue;
                    ++active;
                    maxGain = std::max(maxGain, std::fabs(b.gainDb));
                }
                Log("process : %.0f Hz, preset « %s », %zu bandes actives, gain max "
                    "%.1f dB, preamp %+.1f dB, effet audible %d",
                    GetSampleRate(), preset->name.c_str(), active, maxGain,
                    static_cast<double>(GetParameter(RoomEqAu::kParamPreampDb)),
                    owner_.PresetHasEffect() ? 1 : 0);
            }
        }

        if (inSourceP != inDestP)
            std::memcpy(inDestP, inSourceP,
                static_cast<size_t>(inFramesToProcess) * sizeof(Float32));

        chain_.Process(inDestP, inFramesToProcess);

        // VU-mètre : peak de sortie après traitement + compteur de blocs
        if (meter) {
            float peak = 0.0f;
            for (UInt32 i = 0; i < inFramesToProcess; ++i) {
                const float a = std::fabs(inDestP[i]);
                if (a > peak)
                    peak = a;
            }
            owner_.outPeak_.store(peak, std::memory_order_relaxed);
            owner_.blocks_.fetch_add(1, std::memory_order_relaxed);
        }

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
    : AUEffectBase(inInstance, true)
{
    CreateElements();
    SetParameter(kParamPreampDb, 0.0f);

    // les mutations de l'éditeur (chargement interactif, édition, banque)
    // notifient les vues : elles écoutent notre propriété d'état (il n'existe
    // pas de propriété Apple « changement de valeur de paramètre » en AUv2).
    // La restauration de session lève le drapeau restoring_ : l'état du
    // projet s'applique silencieusement, le DAW rafraîchit de lui-même.
    editor_.SetNotify([this] {
        if (!restoring_)
            PropertyChanged(kRoomEqPropertyStatus, kAudioUnitScope_Global, 0);
    });

    Log("open (AU %s)", ROOMEQ_AU_VERSION);

    // auto-chargement du magasin de l'application (spec I-AUD-07 invariant 9)
    // : le preset le plus récent AVEC effet audible est appliqué pour que le
    // greffon corrige dès l'insertion. L'état du projet DAW (restauré APRÈS
    // la construction) reste maître — il écrase toujours l'auto-chargement.
    double preamp = 0.0;
    std::string summary;
    if (editor_.AutoLoadFromDir(roomeq::edit::DefaultPresetsDir(), preamp, summary)) {
        const auto value = static_cast<Float32>(std::clamp(preamp,
            static_cast<double>(kPreampMinDb), static_cast<double>(kPreampMaxDb)));
        SetParameter(kParamPreampDb, value);
    }
    Log("%s", summary.c_str());
}

RoomEqAu::~RoomEqAu()
{
    // journal de fermeture enrichi (miroir VST v3.5.0) : le nombre de blocs
    // traités tranche à distance « l'audio ne passe pas » (0 bloc) vs « passe
    // sans effet audible » (preset plat) vs « corrige ».
    Log("close : %llu blocs traites",
        static_cast<unsigned long long>(BlocksProcessed()));
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

OSStatus RoomEqAu::Initialize()
{
    const OSStatus status = ausdk::AUEffectBase::Initialize();
    if (status == noErr)
        Log("initialisation : %.0f Hz, %u canal(aux)",
            GetSampleRate(), GetNumberOfChannels());
    return status;
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
        case kRoomEqPropertyBands:
        case kRoomEqPropertyLevels:
            outDataSize = sizeof(CFDataRef);
            outWritable = false;
            return noErr;
        case kRoomEqPropertyEditBand:
        case kRoomEqPropertyAddBand:
        case kRoomEqPropertyBandOp:
        case kRoomEqPropertyLoadHeadphone:
            outDataSize = sizeof(CFDataRef);
            outWritable = true;
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
            const bool hasPreset = HasPreset();

            const CFStringRef nameKeys[] = { CFSTR("name"), CFSTR("bands"),
                CFSTR("preampDb"), CFSTR("version"), CFSTR("hasPreset") };
            // CFTypeRef = const void* : tableau SANS const supplémentaire pour
            // décayer vers const void** (exigence de CFDictionaryCreate)
            CFTypeRef values[] = {
                CFStringCreateWithCString(nullptr, preset->name.c_str(),
                    kCFStringEncodingUTF8),
                CFNumberCreate(nullptr, kCFNumberIntType, &bands),
                CFNumberCreate(nullptr, kCFNumberDoubleType, &preamp),
                CFNumberCreate(nullptr, kCFNumberSInt64Type, &version),
                hasPreset ? kCFBooleanTrue : kCFBooleanFalse,
            };
            const void* keyPointers[] = {
                static_cast<const void*>(nameKeys[0]),
                static_cast<const void*>(nameKeys[1]),
                static_cast<const void*>(nameKeys[2]),
                static_cast<const void*>(nameKeys[3]),
                static_cast<const void*>(nameKeys[4]),
            };
            const auto dict = CFDictionaryCreate(nullptr, keyPointers, values, 5,
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
        case kRoomEqPropertyBands: {
            // instantané binaire « RBP1 » du preset éditable : la vue dessine
            // les points de bandes et fait ses tests de saisie dessus (la
            // courbe seule ne suffit pas — types et activation compris).
            const auto preset = CurrentPreset();
            std::vector<uint8_t> blob;
            blob.reserve(16 + preset->name.size() + preset->bands.size() * 32);
            const uint8_t magic[4] = { 'R', 'B', 'P', '1' };
            blob.insert(blob.end(), magic, magic + 4);
            AppendU32(blob, static_cast<uint32_t>(CurrentPresetVersion()));
            AppendF64(blob, preset->preampDb);
            AppendStr(blob, preset->name);
            AppendU32(blob, static_cast<uint32_t>(preset->bands.size()));
            for (const auto& b : preset->bands) {
                AppendU32(blob, static_cast<uint32_t>(b.type));
                AppendF64(blob, b.frequency);
                AppendF64(blob, b.gainDb);
                AppendF64(blob, b.q);
                blob.push_back(b.enabled ? 1 : 0);
            }
            *static_cast<CFDataRef*>(outData) = CFDataCreate(nullptr, blob.data(),
                static_cast<CFIndex>(blob.size()));
            return noErr;
        }
        case kRoomEqPropertyLevels: {
            // VU-mètres : lecture temps réel sans verrou (atomiques des noyaux)
            float levels[2] = { InputLevel(), OutputLevel() };
            *static_cast<CFDataRef*>(outData) = CFDataCreate(nullptr,
                reinterpret_cast<const UInt8*>(levels), sizeof(levels));
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
    if (inScope != kAudioUnitScope_Global || inElement != 0)
        return ausdk::AUEffectBase::SetProperty(inID, inScope, inElement, inData,
            inDataSize);

    switch (inID) {
    case kRoomEqPropertyPresetData: {
        if (inData == nullptr || inDataSize != sizeof(CFDataRef))
            return kAudioUnitErr_InvalidPropertyValue;
        const auto blob = *static_cast<const CFDataRef*>(inData);
        if (blob == nullptr || CFGetTypeID(blob) != CFDataGetTypeID())
            return kAudioUnitErr_InvalidPropertyValue;
        return ApplyPresetBytes(CFDataGetBytePtr(blob),
            static_cast<size_t>(CFDataGetLength(blob)), true);
    }
    case kRoomEqPropertyEditBand: {
        // { u32 index, u8 masque (1=fréq 2=gain 4=Q), f64, f64, f64 }
        const auto payload = GetCfData(inData, inDataSize);
        if (payload == nullptr)
            return kAudioUnitErr_InvalidPropertyValue;
        PayloadReader r{ CFDataGetBytePtr(payload),
            static_cast<size_t>(CFDataGetLength(payload)) };
        uint32_t index = 0, mask = 0;
        double freq = 0, gain = 0, q = 0;
        if (!r.U32(index) || !r.Read(&mask, 1) || !r.F64(freq) || !r.F64(gain)
            || !r.F64(q))
            return kAudioUnitErr_InvalidPropertyValue;
        const double* pFreq = (mask & 1) != 0 ? &freq : nullptr;
        const double* pGain = (mask & 2) != 0 ? &gain : nullptr;
        const double* pQ = (mask & 4) != 0 ? &q : nullptr;
        if (pFreq == nullptr && pGain == nullptr && pQ == nullptr)
            return kAudioUnitErr_InvalidPropertyValue; // rien à éditer
        return editor_.EditBand(index, pFreq, pGain, pQ) >= 0
            ? noErr
            : kAudioUnitErr_InvalidPropertyValue;
    }
    case kRoomEqPropertyAddBand: {
        const auto payload = GetCfData(inData, inDataSize);
        if (payload == nullptr)
            return kAudioUnitErr_InvalidPropertyValue;
        PayloadReader r{ CFDataGetBytePtr(payload),
            static_cast<size_t>(CFDataGetLength(payload)) };
        double freq = 0, gain = 0;
        if (!r.F64(freq) || !r.F64(gain))
            return kAudioUnitErr_InvalidPropertyValue;
        return editor_.AddBand(freq, gain) >= 0
            ? noErr
            : kAudioUnitErr_InvalidPropertyValue;
    }
    case kRoomEqPropertyBandOp: {
        // { u32 index, u32 opération (0 suppr., 1 act/dés, 2 type), u32 type }
        const auto payload = GetCfData(inData, inDataSize);
        if (payload == nullptr)
            return kAudioUnitErr_InvalidPropertyValue;
        PayloadReader r{ CFDataGetBytePtr(payload),
            static_cast<size_t>(CFDataGetLength(payload)) };
        uint32_t index = 0, op = 0, type = 0;
        if (!r.U32(index) || !r.U32(op) || !r.U32(type))
            return kAudioUnitErr_InvalidPropertyValue;
        switch (op) {
        case 0:
            return editor_.RemoveBand(index)
                ? noErr
                : kAudioUnitErr_InvalidPropertyValue;
        case 1: {
            roomeq::preset::EqBandDef band;
            if (!editor_.BandAt(index, band))
                return kAudioUnitErr_InvalidPropertyValue;
            return editor_.SetBandEnabled(index, !band.enabled)
                ? noErr
                : kAudioUnitErr_InvalidPropertyValue;
        }
        case 2:
            if (type > static_cast<uint32_t>(roomeq::dsp::FilterType::Notch))
                return kAudioUnitErr_InvalidPropertyValue;
            return editor_.SetBandType(index,
                       static_cast<roomeq::dsp::FilterType>(type))
                ? noErr
                : kAudioUnitErr_InvalidPropertyValue;
        default:
            return kAudioUnitErr_InvalidPropertyValue;
        }
    }
    case kRoomEqPropertyLoadHeadphone: {
        const auto payload = GetCfData(inData, inDataSize);
        if (payload == nullptr)
            return kAudioUnitErr_InvalidPropertyValue;
        PayloadReader r{ CFDataGetBytePtr(payload),
            static_cast<size_t>(CFDataGetLength(payload)) };
        uint32_t index = 0;
        if (!r.U32(index))
            return kAudioUnitErr_InvalidPropertyValue;
        double preamp = 0.0;
        if (!editor_.LoadHeadphone(index, preamp))
            return kAudioUnitErr_InvalidPropertyValue;
        const auto value = static_cast<Float32>(std::clamp(preamp,
            static_cast<double>(kPreampMinDb), static_cast<double>(kPreampMaxDb)));
        SetParameter(kParamPreampDb, value);
        Log("preset casque charge : « %s » (preamp %+.1f dB)",
            CurrentPreset()->name.c_str(), static_cast<double>(value));
        PropertyChanged(kRoomEqPropertyStatus, kAudioUnitScope_Global, 0);
        return noErr;
    }
    default:
        return ausdk::AUEffectBase::SetProperty(inID, inScope, inElement, inData,
            inDataSize);
    }
}

// ------------------------------------------------------------------ état

OSStatus RoomEqAu::SaveState(CFPropertyListRef* outData)
{
    const OSStatus status = ausdk::AUEffectBase::SaveState(outData);
    if (status != noErr)
        return status;

    const auto bytes = editor_.Bytes();
    if (bytes.empty())
        return noErr;

    const auto dict = static_cast<CFMutableDictionaryRef>(
        const_cast<void*>(*outData));
    // mutable et immuable partagent le même identifiant de type CoreFoundation
    if (dict == nullptr || CFGetTypeID(dict) != CFDictionaryGetTypeID())
        return noErr;

    const CFDataRef blob = CFDataCreate(nullptr, bytes.data(),
        static_cast<CFIndex>(bytes.size()));
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
    // paramètre — silencieusement : l'état du projet devient la référence,
    // les vues se rafraîchissent par le cycle normal de l'hôte.
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

    restoring_ = true;
    const OSStatus applied = ApplyPresetBytes(CFDataGetBytePtr(blob),
        static_cast<size_t>(CFDataGetLength(blob)), false);
    restoring_ = false;
    return applied;
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
    double preamp = 0.0;
    std::string error;
    if (!editor_.ApplyBytes(bytes, size, preamp, error)) {
        Log("preset refuse : %s", error.c_str());
        return kAudioUnitErr_InvalidPropertyValue;
    }
    Log("preset charge (%zu octets)", size);

    if (updateParameter) {
        const auto value = static_cast<Float32>(std::clamp(preamp,
            static_cast<double>(kPreampMinDb), static_cast<double>(kPreampMaxDb)));
        SetParameter(kParamPreampDb, value);
        // notifier vues et hôte (l'éditeur a déjà notifié via son callback :
        // cette seconde notification couvre le changement de paramètre)
        PropertyChanged(kRoomEqPropertyStatus, kAudioUnitScope_Global, 0);
    }
    return noErr;
}

// ------------------------------------------------------------------ entrée usine

AUSDK_COMPONENT_ENTRY(ausdk::AUBaseFactory, RoomEqAu)
