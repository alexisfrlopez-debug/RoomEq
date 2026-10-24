/* -*- mode: c++; coding: utf-8 -*-
   RoomEqHAL — greffon AudioServerPlugIn macOS (périphérique virtuel « RoomEq Virtual »).

   Rôle (équivalent macOS du pilote noyau Windows roomeqvad.sys) :
     - crée UN périphérique virtuel stéréo 48 kHz (« RoomEq Virtual ») avec un flux
       de sortie (ce que macOS y joue) et un flux d'entrée (boucle du son joué —
       utilisable par toute application d'enregistrement) ;
     - publie le son rendu par macOS dans le pont intégré RoomEq (même protocole
       que le pont ASIO Windows : en-tête 256 octets + anneau SPSC 32768 trames,
       fichier /tmp/roomeq.device.bridge.v1.mmf — voir AsioBridgeTransport.cs) ;
     - l'application RoomEq lit l'anneau, applique l'égaliseur, rend le son sur
       la vraie sortie CoreAudio.

   Écrit d'après l'en-tête public d'Apple <CoreAudio/AudioServerPlugIn.h>
   (SDK Xcode). Aucune bibliothèque tierce : les maths d'anneau viennent du
   noyau C partagé du projet (asio/bridge_core).

   Contraintes temps réel : DoIOOperation tourne sur le thread audio de
   coreaudiod — aucune allocation, aucun verrou bloquant, aucun logging.
*/

#include <CoreAudio/AudioServerPlugIn.h>
#include <CoreFoundation/CoreFoundation.h>

#include <fcntl.h>
#include <sys/mman.h>
#include <sys/stat.h>
#include <unistd.h>

#include <atomic>
#include <cstdint>
#include <cstring>

#include "roomeq_asio_protocol.h"

extern "C" {
// noyau C du pont (asio/bridge_core/roomeq_asio_bridge.c) — réutilisé tel quel
int64_t roomeq_asio_available(const RoomeqAsioHeader* h);
int64_t roomeq_asio_free(const RoomeqAsioHeader* h);
int64_t roomeq_asio_read(const RoomeqAsioHeader* h, const float* ring,
                         int64_t wanted, float* dest);
int64_t roomeq_asio_write(RoomeqAsioHeader* h, float* ring,
                          int64_t n, const float* src);
}

namespace {

// ==================================================================== constantes

// codes 4CC (AudioHardwareBase.h / AudioHardware.h / CoreAudioTypes.h, SDK actuel)
constexpr AudioObjectPropertySelector kSelBaseClass       = 'bcls';
constexpr AudioObjectPropertySelector kSelClass           = 'clas';
constexpr AudioObjectPropertySelector kSelOwner           = 'stdv';
constexpr AudioObjectPropertySelector kSelName            = 'lnam';
constexpr AudioObjectPropertySelector kSelManufacturer    = 'lmak';
constexpr AudioObjectPropertySelector kSelElementName     = 'lchn';
constexpr AudioObjectPropertySelector kSelOwnedObjects    = 'ownd';
constexpr AudioObjectPropertySelector kSelPlugInBundleID  = 'piid';
constexpr AudioObjectPropertySelector kSelPlugInDevices   = 'dev#';
constexpr AudioObjectPropertySelector kSelTranslateUID    = 'uidd';
constexpr AudioObjectPropertySelector kSelDeviceUID       = 'uid ';
constexpr AudioObjectPropertySelector kSelModelUID        = 'muid';
constexpr AudioObjectPropertySelector kSelTransportType   = 'tran';
constexpr AudioObjectPropertySelector kSelRelatedDevices  = 'akin';
constexpr AudioObjectPropertySelector kSelClockDomain     = 'clkd';
constexpr AudioObjectPropertySelector kSelClockAlgorithm  = 'clok';
constexpr AudioObjectPropertySelector kSelAlive           = 'livn';
constexpr AudioObjectPropertySelector kSelRunning         = 'goin';
constexpr AudioObjectPropertySelector kSelRunningSomewhere= 'gone';
constexpr AudioObjectPropertySelector kSelCanBeDefault    = 'dflt';
constexpr AudioObjectPropertySelector kSelCanBeDefaultSys = 'sflt';
constexpr AudioObjectPropertySelector kSelLatency         = 'ltnc';
constexpr AudioObjectPropertySelector kSelStreams         = 'stm#';
constexpr AudioObjectPropertySelector kSelSafetyOffset    = 'saft';
constexpr AudioObjectPropertySelector kSelNominalRate     = 'nsrt';
constexpr AudioObjectPropertySelector kSelAvailableRates  = 'nsr#';
constexpr AudioObjectPropertySelector kSelIsHidden        = 'hidn';
constexpr AudioObjectPropertySelector kSelStereoChannels  = 'dch2';
constexpr AudioObjectPropertySelector kSelHogMode         = 'oink';
constexpr AudioObjectPropertySelector kSelBufferFrameSize = 'fsiz';
constexpr AudioObjectPropertySelector kSelBufferSizeRange = 'fsz#';
constexpr AudioObjectPropertySelector kSelVariableBuffers = 'vfsz';
constexpr AudioObjectPropertySelector kSelStreamActive    = 'sact';
constexpr AudioObjectPropertySelector kSelDirection       = 'sdir';
constexpr AudioObjectPropertySelector kSelTerminalType    = 'term';
constexpr AudioObjectPropertySelector kSelStartingChannel = 'schn';
constexpr AudioObjectPropertySelector kSelVirtualFormat   = 'sfmt';
constexpr AudioObjectPropertySelector kSelAvailableVFormats = 'sfma';
constexpr AudioObjectPropertySelector kSelPhysicalFormat  = 'pft ';
constexpr AudioObjectPropertySelector kSelAvailablePFormats = 'pfta';

constexpr AudioObjectPropertyScope kScopeGlobal = 'glob';
constexpr AudioObjectPropertyScope kScopeInput  = 'inpt';
constexpr AudioObjectPropertyScope kScopeOutput = 'outp';

constexpr AudioClassID       kClassObject  = 'aobj';
constexpr AudioClassID       kClassPlugIn  = 'aplg';
constexpr AudioClassID       kClassDevice  = 'adev';
constexpr AudioClassID       kClassStream  = 'astr';
constexpr uint32_t           kTransportVirtual = 'virt';
constexpr uint32_t           kTerminalSpeaker  = 'spkr';
constexpr uint32_t           kTerminalMicro    = 'micr';
constexpr uint32_t           kClockUnclocked   = 0;  // kAudioDeviceClockAlgorithmUnclocked

// IO ops (AudioServerPlugIn.h)
constexpr uint32_t kOpReadInput = 'read';
constexpr uint32_t kOpWriteMix  = 'rite';

// ==================================================================== identité

constexpr AudioObjectID kObjPlugIn        = 1;  // kAudioObjectPlugInObject
constexpr AudioObjectID kObjDevice        = 2;
constexpr AudioObjectID kObjOutputStream  = 3;
constexpr AudioObjectID kObjInputStream   = 4;

constexpr Float64 kDeviceRate = 48000.0;
constexpr UInt32  kChannelCount = 2;
constexpr UInt32  kBufferFrames = 512;      // taille annoncée (plage 16..4096)

constexpr const char* kDeviceName   = "RoomEq Virtual";
constexpr const char* kBundleIDText = "com.roomeq.RoomEqHAL";
constexpr const char* kDeviceUID    = "RoomEqVirtual";
constexpr const char* kManufacturer = "RoomEq";

// pont intégré — DOIT rester synchronisé avec AsioBridgeTransport.FallbackPath
constexpr const char* kBridgePath = "/tmp/roomeq.device.bridge.v1.mmf";
constexpr size_t kBridgeBytes =
    ROOMEQ_ASIO_HEADER_BYTES +
    (size_t)ROOMEQ_ASIO_RING_FRAMES * ROOMEQ_ASIO_CHANNELS * sizeof(float);

// ==================================================================== état

struct PluginState
{
    AudioServerPlugInHostRef host = nullptr;
    std::atomic<ULONG> refCount{ 1 };
    std::atomic<int32_t> ioClients{ 0 };

    // pont (mappé une fois, jamais démappé : le thread audio s'en sert à chaud)
    RoomeqAsioHeader*  bridge = nullptr;
    float*             ring = nullptr;

    // curseur PRIVÉ du flux d'entrée (la position readPos du protocole
    // appartient à l'application ; le greffon ne la touche jamais)
    int64_t loopPos = 0;
};

PluginState g;

// ==================================================================== helpers CF

CFStringRef MakeCFString(const char* s)
{
    return CFStringCreateWithCString(kCFAllocatorDefault, s, kCFStringEncodingUTF8);
}

// ==================================================================== pont

// Crée (ou rouvre) la section partagée. Appelé à l'initialisation et à
// StartIO — AVANT tout appel IO ; le thread audio ne lit que g.bridge/g.ring.
bool BridgeAttach()
{
    if (g.bridge != nullptr) return true;

    int fd = open(kBridgePath, O_RDWR | O_CREAT, 0666);
    if (fd < 0) return false;
    (void)fchmod(fd, 0666);  // l'application (autre utilisateur) doit pouvoir l'écrire

    struct stat st{};
    if (fstat(fd, &st) != 0 || st.st_size < (off_t)kBridgeBytes)
    {
        if (ftruncate(fd, (off_t)kBridgeBytes) != 0) { close(fd); return false; }
    }

    void* p = mmap(nullptr, kBridgeBytes, PROT_READ | PROT_WRITE, MAP_SHARED, fd, 0);
    close(fd);
    if (p == MAP_FAILED || p == nullptr) return false;

    auto* h = static_cast<RoomeqAsioHeader*>(p);
    if (h->magic != ROOMEQ_ASIO_MAGIC)
    {
        memset(p, 0, kBridgeBytes);
        h->magic        = ROOMEQ_ASIO_MAGIC;
        h->version      = ROOMEQ_ASIO_VERSION;
        h->sampleRate   = 48000;
        h->bufferFrames = kBufferFrames;
        h->ringFrames   = ROOMEQ_ASIO_RING_FRAMES;
        h->channels     = ROOMEQ_ASIO_CHANNELS;
        h->session      = 1;
    }
    else
    {
        // coreaudiod redémarre (ou le greffon est rechargé) : nouvelle session,
        // l'application réaligne son curseur de lecture dessus
        h->session = h->session + 1;
    }
    std::atomic_thread_fence(std::memory_order_release);

    g.bridge = h;
    g.ring   = roomeq_asio_ring(h);
    return true;
}

// Copie n trames depuis l'anneau à une position ABSOLUE (sans toucher readPos).
void RingCopyOut(int64_t pos, int64_t n, float* dst)
{
    const int64_t mask = ROOMEQ_ASIO_RING_FRAMES - 1;
    int64_t done = 0;
    while (done < n)
    {
        int64_t frame = pos + done;
        int64_t offset = frame & mask;
        int64_t contiguous = (n - done) < (ROOMEQ_ASIO_RING_FRAMES - offset)
                             ? (n - done) : (ROOMEQ_ASIO_RING_FRAMES - offset);
        memcpy(dst + done * ROOMEQ_ASIO_CHANNELS,
               g.ring + offset * ROOMEQ_ASIO_CHANNELS,
               (size_t)contiguous * ROOMEQ_ASIO_CHANNELS * sizeof(float));
        done += contiguous;
    }
}

// ==================================================================== prototypes COM

extern "C" __attribute__((visibility("default")))
void* RoomEqHALCreate(CFAllocatorRef inAllocator, CFUUIDRef inTypeUUID);

static HRESULT RoomEq_QueryInterface(void* inDriver, REFIID inUUID, LPVOID* outInterface);
static ULONG   RoomEq_AddRef(void* inDriver);
static ULONG   RoomEq_Release(void* inDriver);

static OSStatus RoomEq_Initialize(AudioServerPlugInDriverRef inDriver, AudioServerPlugInHostRef inHost);
static OSStatus RoomEq_CreateDevice(AudioServerPlugInDriverRef, CFDictionaryRef,
                                    const AudioServerPlugInClientInfo*, AudioObjectID*);
static OSStatus RoomEq_DestroyDevice(AudioServerPlugInDriverRef, AudioObjectID);
static OSStatus RoomEq_AddDeviceClient(AudioServerPlugInDriverRef, AudioObjectID,
                                       const AudioServerPlugInClientInfo*);
static OSStatus RoomEq_RemoveDeviceClient(AudioServerPlugInDriverRef, AudioObjectID,
                                          const AudioServerPlugInClientInfo*);
static OSStatus RoomEq_PerformDeviceConfigurationChange(AudioServerPlugInDriverRef, AudioObjectID,
                                                        UInt64, void*);
static OSStatus RoomEq_AbortDeviceConfigurationChange(AudioServerPlugInDriverRef, AudioObjectID,
                                                      UInt64, void*);

static Boolean   RoomEq_HasProperty(AudioServerPlugInDriverRef, AudioObjectID, pid_t,
                                    const AudioObjectPropertyAddress*);
static OSStatus  RoomEq_IsPropertySettable(AudioServerPlugInDriverRef, AudioObjectID, pid_t,
                                           const AudioObjectPropertyAddress*, Boolean*);
static OSStatus  RoomEq_GetPropertyDataSize(AudioServerPlugInDriverRef, AudioObjectID, pid_t,
                                            const AudioObjectPropertyAddress*, UInt32,
                                            const void*, UInt32*);
static OSStatus  RoomEq_GetPropertyData(AudioServerPlugInDriverRef, AudioObjectID, pid_t,
                                        const AudioObjectPropertyAddress*, UInt32, const void*,
                                        UInt32, UInt32*, void*);
static OSStatus  RoomEq_SetPropertyData(AudioServerPlugInDriverRef, AudioObjectID, pid_t,
                                        const AudioObjectPropertyAddress*, UInt32, const void*,
                                        UInt32, const void*);

static OSStatus RoomEq_StartIO(AudioServerPlugInDriverRef, AudioObjectID, UInt32);
static OSStatus RoomEq_StopIO(AudioServerPlugInDriverRef, AudioObjectID, UInt32);
static OSStatus RoomEq_GetZeroTimeStamp(AudioServerPlugInDriverRef, AudioObjectID, UInt32,
                                        Float64*, UInt64*, UInt64*);
static OSStatus RoomEq_WillDoIOOperation(AudioServerPlugInDriverRef, AudioObjectID, UInt32,
                                         UInt32, Boolean*, Boolean*);
static OSStatus RoomEq_BeginIOOperation(AudioServerPlugInDriverRef, AudioObjectID, UInt32, UInt32,
                                        UInt32, const AudioServerPlugInIOCycleInfo*);
static OSStatus RoomEq_DoIOOperation(AudioServerPlugInDriverRef, AudioObjectID, AudioObjectID,
                                     UInt32, UInt32, UInt32,
                                     const AudioServerPlugInIOCycleInfo*, void*, void*);
static OSStatus RoomEq_EndIOOperation(AudioServerPlugInDriverRef, AudioObjectID, UInt32, UInt32,
                                      UInt32, const AudioServerPlugInIOCycleInfo*);

// ==================================================================== interface

AudioServerPlugInDriverInterface gInterface = {
    nullptr,                                // _reserved
    RoomEq_QueryInterface,
    RoomEq_AddRef,
    RoomEq_Release,
    RoomEq_Initialize,
    RoomEq_CreateDevice,
    RoomEq_DestroyDevice,
    RoomEq_AddDeviceClient,
    RoomEq_RemoveDeviceClient,
    RoomEq_PerformDeviceConfigurationChange,
    RoomEq_AbortDeviceConfigurationChange,
    RoomEq_HasProperty,
    RoomEq_IsPropertySettable,
    RoomEq_GetPropertyDataSize,
    RoomEq_GetPropertyData,
    RoomEq_SetPropertyData,
    RoomEq_StartIO,
    RoomEq_StopIO,
    RoomEq_GetZeroTimeStamp,
    RoomEq_WillDoIOOperation,
    RoomEq_BeginIOOperation,
    RoomEq_DoIOOperation,
    RoomEq_EndIOOperation,
};

// L'instance CFPlugIn est un EMPLACEMENT contenant le pointeur d'interface :
// AudioServerPlugInDriverRef = AudioServerPlugInDriverInterface** → gInstance
// pointe sur gInterfaceStorage, qui pointe sur gInterface (la vtable).
AudioServerPlugInDriverInterface* gInterfaceStorage = &gInterface;
AudioServerPlugInDriverRef gInstance = &gInterfaceStorage;

const CFUUIDRef kIUnknownUUID = CFUUIDGetConstantUUIDWithBytes(
    nullptr, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00,
    0xC0, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x46);

}  // namespace

extern "C" __attribute__((visibility("default")))
void* RoomEqHALCreate(CFAllocatorRef /*inAllocator*/, CFUUIDRef inTypeUUID)
{
    if (inTypeUUID == nullptr ||
        !CFEqual(inTypeUUID, kAudioServerPlugInTypeUUID))
    {
        return nullptr;
    }
    return &gInstance;
}

namespace {

// ==================================================================== IUnknown

HRESULT RoomEq_QueryInterface(void* /*inDriver*/, REFIID inUUID, LPVOID* outInterface)
{
    if (inUUID == nullptr || outInterface == nullptr)
        return static_cast<HRESULT>(0x80004003L);  // E_POINTER

    if (CFEqual(inUUID, kIUnknownUUID) ||
        CFEqual(inUUID, kAudioServerPlugInDriverInterfaceUUID))
    {
        *outInterface = gInstance;  // même instance, +1 référence
        g.refCount.fetch_add(1, std::memory_order_relaxed);
        return 0;
    }
    *outInterface = nullptr;
    return static_cast<HRESULT>(0x80004002L);  // E_NOINTERFACE
}

ULONG RoomEq_AddRef(void*) { return g.refCount.fetch_add(1, std::memory_order_relaxed) + 1; }

ULONG RoomEq_Release(void*)
{
    ULONG count = g.refCount.fetch_sub(1, std::memory_order_relaxed) - 1;
    // instance statique : jamais libérée (durée de vie = coreaudiod)
    return count;
}

// ==================================================================== cycle de vie

OSStatus RoomEq_Initialize(AudioServerPlugInDriverRef /*inDriver*/, AudioServerPlugInHostRef inHost)
{
    g.host = inHost;

    // le périphérique est publié via les propriétés du greffon (dev#/ownd) ;
    // pas de notification de structure nécessaire à l'initialisation.
    (void)BridgeAttach();  // échec non fatal : retenté à StartIO
    return kAudioHardwareNoError;
}

OSStatus RoomEq_CreateDevice(AudioServerPlugInDriverRef, CFDictionaryRef,
                             const AudioServerPlugInClientInfo*, AudioObjectID*)
{
    return kAudioHardwareUnsupportedOperationError;  // périphérique fixe, non créable
}

OSStatus RoomEq_DestroyDevice(AudioServerPlugInDriverRef, AudioObjectID inDevice)
{
    return inDevice == kObjDevice
               ? kAudioHardwareUnsupportedOperationError
               : kAudioHardwareBadObjectError;
}

OSStatus RoomEq_AddDeviceClient(AudioServerPlugInDriverRef, AudioObjectID, const AudioServerPlugInClientInfo*)
{
    return kAudioHardwareNoError;  // rien à faire : pas de périphériques privés
}

OSStatus RoomEq_RemoveDeviceClient(AudioServerPlugInDriverRef, AudioObjectID, const AudioServerPlugInClientInfo*)
{
    return kAudioHardwareNoError;
}

OSStatus RoomEq_PerformDeviceConfigurationChange(AudioServerPlugInDriverRef, AudioObjectID,
                                                 UInt64, void*)
{
    return kAudioHardwareNoError;  // configuration figée (48 kHz unique)
}

OSStatus RoomEq_AbortDeviceConfigurationChange(AudioServerPlugInDriverRef, AudioObjectID,
                                               UInt64, void*)
{
    return kAudioHardwareNoError;
}

// ==================================================================== propriétés : classification

bool IsPluginObject(AudioObjectID id) { return id == kObjPlugIn; }
bool IsDeviceObject(AudioObjectID id) { return id == kObjDevice; }
bool IsStreamObject(AudioObjectID id) { return id == kObjOutputStream || id == kObjInputStream; }

Boolean RoomEq_HasProperty(AudioServerPlugInDriverRef, AudioObjectID inObjectID, pid_t,
                           const AudioObjectPropertyAddress* inAddress)
{
    if (inAddress == nullptr) return false;
    const auto sel = inAddress->mSelector;

    if (IsPluginObject(inObjectID))
    {
        switch (sel)
        {
            case kSelBaseClass:
            case kSelClass:
            case kSelOwner:
            case kSelName:
            case kSelManufacturer:
            case kSelOwnedObjects:
            case kSelPlugInBundleID:
            case kSelPlugInDevices:
            case kSelTranslateUID:
                return true;
            default:
                return false;
        }
    }

    if (IsDeviceObject(inObjectID))
    {
        switch (sel)
        {
            case kSelBaseClass:
            case kSelClass:
            case kSelOwner:
            case kSelName:
            case kSelManufacturer:
            case kSelElementName:
            case kSelOwnedObjects:
            case kSelDeviceUID:
            case kSelModelUID:
            case kSelTransportType:
            case kSelRelatedDevices:
            case kSelClockDomain:
            case kSelClockAlgorithm:
            case kSelAlive:
            case kSelRunning:
            case kSelRunningSomewhere:
            case kSelCanBeDefault:
            case kSelCanBeDefaultSys:
            case kSelLatency:
            case kSelSafetyOffset:
            case kSelNominalRate:
            case kSelAvailableRates:
            case kSelIsHidden:
            case kSelStereoChannels:
            case kSelHogMode:
            case kSelBufferFrameSize:
            case kSelBufferSizeRange:
            case kSelVariableBuffers:
            case kSelStreams:
                return true;  // kSelStreams : réponse dépendante du scope
            default:
                return false;
        }
    }

    if (IsStreamObject(inObjectID))
    {
        switch (sel)
        {
            case kSelBaseClass:
            case kSelClass:
            case kSelOwner:
            case kSelName:
            case kSelStreamActive:
            case kSelDirection:
            case kSelTerminalType:
            case kSelStartingChannel:
            case kSelLatency:
            case kSelVirtualFormat:
            case kSelAvailableVFormats:
            case kSelPhysicalFormat:
            case kSelAvailablePFormats:
                return true;
            default:
                return false;
        }
    }

    return false;
}

OSStatus RoomEq_IsPropertySettable(AudioServerPlugInDriverRef inDriver, AudioObjectID inObjectID,
                                   pid_t inPid, const AudioObjectPropertyAddress* inAddress,
                                   Boolean* outIsSettable)
{
    if (inAddress == nullptr || outIsSettable == nullptr) return kAudioHardwareIllegalOperationError;
    if (!RoomEq_HasProperty(inDriver, inObjectID, inPid, inAddress))
        return kAudioHardwareUnknownPropertyError;

    *outIsSettable = false;  // périphérique à configuration figée (48 kHz / 2 canaux)
    return kAudioHardwareNoError;
}

// format de flux unique : 48 kHz, stéréo, float32 entrelacé
AudioStreamBasicDescription StreamFormat()
{
    AudioStreamBasicDescription asbd{};
    asbd.mSampleRate       = kDeviceRate;
    asbd.mFormatID         = kAudioFormatLinearPCM;
    asbd.mFormatFlags      = kAudioFormatFlagIsFloat | kAudioFormatFlagIsPacked;
    asbd.mBytesPerPacket   = 4 * kChannelCount;
    asbd.mFramesPerPacket  = 1;
    asbd.mBytesPerFrame    = 4 * kChannelCount;
    asbd.mChannelsPerFrame = kChannelCount;
    asbd.mBitsPerChannel   = 32;
    return asbd;
}

OSStatus RoomEq_GetPropertyDataSize(AudioServerPlugInDriverRef inDriver, AudioObjectID inObjectID,
                                    pid_t inPid, const AudioObjectPropertyAddress* inAddress,
                                    UInt32 /*inQualifierSize*/, const void* /*inQualifierData*/,
                                    UInt32* outDataSize)
{
    if (inAddress == nullptr || outDataSize == nullptr) return kAudioHardwareIllegalOperationError;
    if (!RoomEq_HasProperty(inDriver, inObjectID, inPid, inAddress))
        return kAudioHardwareUnknownPropertyError;

    switch (inAddress->mSelector)
    {
        case kSelBaseClass:
        case kSelClass:
        case kSelTransportType:
        case kSelClockDomain:
        case kSelClockAlgorithm:
        case kSelAlive:
        case kSelRunning:
        case kSelRunningSomewhere:
        case kSelCanBeDefault:
        case kSelCanBeDefaultSys:
        case kSelLatency:
        case kSelSafetyOffset:
        case kSelHogMode:
        case kSelBufferFrameSize:
        case kSelVariableBuffers:
        case kSelStreamActive:
        case kSelDirection:
        case kSelTerminalType:
        case kSelStartingChannel:
        case kSelIsHidden:
            *outDataSize = sizeof(UInt32);
            return kAudioHardwareNoError;

        case kSelNominalRate:
            *outDataSize = sizeof(Float64);
            return kAudioHardwareNoError;

        case kSelOwner:
        case kSelTranslateUID:
            *outDataSize = sizeof(AudioObjectID);
            return kAudioHardwareNoError;

        case kSelName:
        case kSelManufacturer:
        case kSelElementName:
        case kSelDeviceUID:
        case kSelModelUID:
        case kSelPlugInBundleID:
            *outDataSize = sizeof(CFStringRef);
            return kAudioHardwareNoError;

        case kSelStereoChannels:
            *outDataSize = sizeof(UInt32) * 2;
            return kAudioHardwareNoError;

        case kSelRelatedDevices:
        case kSelAvailableRates:
            *outDataSize = inAddress->mSelector == kSelAvailableRates
                               ? sizeof(Float64)
                               : sizeof(AudioObjectID);
            return kAudioHardwareNoError;

        case kSelOwnedObjects:
            *outDataSize = IsPluginObject(inObjectID)
                               ? sizeof(AudioObjectID)          // greffon → périphérique
                               : sizeof(AudioObjectID) * 2;     // périphérique → flux
            return kAudioHardwareNoError;

        case kSelPlugInDevices:
            *outDataSize = sizeof(AudioObjectID);
            return kAudioHardwareNoError;

        case kSelStreams:
            *outDataSize = (inAddress->mScope == kScopeInput ||
                            inAddress->mScope == kScopeOutput)
                               ? sizeof(AudioObjectID)
                               : sizeof(AudioObjectID) * 2;
            return kAudioHardwareNoError;

        case kSelBufferSizeRange:
            *outDataSize = sizeof(AudioValueRange);
            return kAudioHardwareNoError;

        case kSelVirtualFormat:
        case kSelPhysicalFormat:
        case kSelAvailableVFormats:
        case kSelAvailablePFormats:
            *outDataSize = sizeof(AudioStreamBasicDescription);
            return kAudioHardwareNoError;

        default:
            return kAudioHardwareUnknownPropertyError;
    }
}

OSStatus RoomEq_GetPropertyData(AudioServerPlugInDriverRef inDriver, AudioObjectID inObjectID,
                                pid_t inPid, const AudioObjectPropertyAddress* inAddress,
                                UInt32 inQualifierSize, const void* inQualifierData,
                                UInt32 inDataSize, UInt32* outDataSize, void* outData)
{
    if (inAddress == nullptr || outData == nullptr || outDataSize == nullptr)
        return kAudioHardwareIllegalOperationError;
    if (!RoomEq_HasProperty(inDriver, inObjectID, inPid, inAddress))
        return kAudioHardwareUnknownPropertyError;

    const auto sel = inAddress->mSelector;
    const auto scope = inAddress->mScope;
    const auto element = inAddress->mElement;

    // ---------------- chaînes (créées à chaque appel : l'hôte les libère)
    if (inDataSize >= sizeof(CFStringRef))
    {
        auto** outString = static_cast<CFStringRef*>(outData);
        switch (sel)
        {
            case kSelName:
                *outString = MakeCFString(IsPluginObject(inObjectID) ? "RoomEq HAL"
                                       : IsDeviceObject(inObjectID) ? kDeviceName
                                       : inObjectID == kObjOutputStream ? "Sortie RoomEq"
                                                                        : "Entrée RoomEq (boucle)");
                *outDataSize = sizeof(CFStringRef);
                return kAudioHardwareNoError;
            case kSelManufacturer:
                *outString = MakeCFString(kManufacturer);
                *outDataSize = sizeof(CFStringRef);
                return kAudioHardwareNoError;
            case kSelElementName:
                *outString = MakeCFString(element == 1 ? "Gauche"
                                       : element == 2 ? "Droite"
                                                      : "RoomEq");
                *outDataSize = sizeof(CFStringRef);
                return kAudioHardwareNoError;
            case kSelDeviceUID:
            case kSelModelUID:
                *outString = MakeCFString(kDeviceUID);
                *outDataSize = sizeof(CFStringRef);
                return kAudioHardwareNoError;
            case kSelPlugInBundleID:
                *outString = MakeCFString(kBundleIDText);
                *outDataSize = sizeof(CFStringRef);
                return kAudioHardwareNoError;
            default:
                break;  // pas une chaîne : continuer
        }
    }

    switch (sel)
    {
        // ---------------- classes & propriétaire
        case kSelBaseClass:
            if (inDataSize < sizeof(AudioClassID)) return kAudioHardwareIllegalOperationError;
            *static_cast<AudioClassID*>(outData) = kClassObject;
            *outDataSize = sizeof(AudioClassID);
            return kAudioHardwareNoError;

        case kSelClass:
        {
            if (inDataSize < sizeof(AudioClassID)) return kAudioHardwareIllegalOperationError;
            *static_cast<AudioClassID*>(outData) =
                IsPluginObject(inObjectID) ? kClassPlugIn
                : IsDeviceObject(inObjectID) ? kClassDevice
                                             : kClassStream;
            *outDataSize = sizeof(AudioClassID);
            return kAudioHardwareNoError;
        }
        case kSelOwner:
        {
            if (inDataSize < sizeof(AudioObjectID)) return kAudioHardwareIllegalOperationError;
            // propriétaire du périphérique = greffon ; des flux = périphérique
            *static_cast<AudioObjectID*>(outData) =
                IsPluginObject(inObjectID) ? 0  // le greffon n'a pas de propriétaire
                : IsDeviceObject(inObjectID) ? kObjPlugIn
                                             : kObjDevice;
            *outDataSize = sizeof(AudioObjectID);
            return kAudioHardwareNoError;
        }

        // ---------------- listes d'objets
        case kSelOwnedObjects:
        {
            if (IsPluginObject(inObjectID))
            {
                if (inDataSize < sizeof(AudioObjectID)) return kAudioHardwareIllegalOperationError;
                static_cast<AudioObjectID*>(outData)[0] = kObjDevice;
                *outDataSize = sizeof(AudioObjectID);
            }
            else
            {
                if (inDataSize < sizeof(AudioObjectID) * 2) return kAudioHardwareIllegalOperationError;
                auto* ids = static_cast<AudioObjectID*>(outData);
                ids[0] = kObjOutputStream;
                ids[1] = kObjInputStream;
                *outDataSize = sizeof(AudioObjectID) * 2;
            }
            return kAudioHardwareNoError;
        }
        case kSelPlugInDevices:
        {
            if (inDataSize < sizeof(AudioObjectID)) return kAudioHardwareIllegalOperationError;
            static_cast<AudioObjectID*>(outData)[0] = kObjDevice;
            *outDataSize = sizeof(AudioObjectID);
            return kAudioHardwareNoError;
        }
        case kSelTranslateUID:
        {
            if (inDataSize < sizeof(AudioObjectID)) return kAudioHardwareIllegalOperationError;
            AudioObjectID found = 0;  // kAudioObjectUnknown
            if (inQualifierSize == sizeof(CFStringRef) && inQualifierData != nullptr)
            {
                CFStringRef uid = static_cast<CFStringRef>(const_cast<void*>(inQualifierData));
                CFStringRef wanted = MakeCFString(kDeviceUID);
                if (uid != nullptr && wanted != nullptr &&
                    CFStringCompare(uid, wanted, 0) == kCFCompareEqualTo)
                {
                    found = kObjDevice;
                }
                if (wanted != nullptr) CFRelease(wanted);
            }
            *static_cast<AudioObjectID*>(outData) = found;
            *outDataSize = sizeof(AudioObjectID);
            return kAudioHardwareNoError;
        }
        case kSelRelatedDevices:
        {
            if (inDataSize < sizeof(AudioObjectID)) return kAudioHardwareIllegalOperationError;
            static_cast<AudioObjectID*>(outData)[0] = kObjDevice;
            *outDataSize = sizeof(AudioObjectID);
            return kAudioHardwareNoError;
        }
        case kSelStereoChannels:
        {
            if (inDataSize < sizeof(UInt32) * 2) return kAudioHardwareIllegalOperationError;
            auto* ch = static_cast<UInt32*>(outData);
            ch[0] = 1;
            ch[1] = 2;
            *outDataSize = sizeof(UInt32) * 2;
            return kAudioHardwareNoError;
        }
        case kSelStreams:
        {
            if (scope == kScopeInput)
            {
                if (inDataSize < sizeof(AudioObjectID)) return kAudioHardwareIllegalOperationError;
                static_cast<AudioObjectID*>(outData)[0] = kObjInputStream;
                *outDataSize = sizeof(AudioObjectID);
            }
            else if (scope == kScopeOutput)
            {
                if (inDataSize < sizeof(AudioObjectID)) return kAudioHardwareIllegalOperationError;
                static_cast<AudioObjectID*>(outData)[0] = kObjOutputStream;
                *outDataSize = sizeof(AudioObjectID);
            }
            else
            {
                if (inDataSize < sizeof(AudioObjectID) * 2) return kAudioHardwareIllegalOperationError;
                auto* ids = static_cast<AudioObjectID*>(outData);
                ids[0] = kObjOutputStream;
                ids[1] = kObjInputStream;
                *outDataSize = sizeof(AudioObjectID) * 2;
            }
            return kAudioHardwareNoError;
        }

        // ---------------- périphérique : identité & état
        case kSelTransportType:
            *static_cast<UInt32*>(outData) = kTransportVirtual;
            *outDataSize = sizeof(UInt32);
            return kAudioHardwareNoError;
        case kSelClockDomain:
            *static_cast<UInt32*>(outData) = 0;
            *outDataSize = sizeof(UInt32);
            return kAudioHardwareNoError;
        case kSelClockAlgorithm:
            *static_cast<UInt32*>(outData) = kClockUnclocked;  // horloge générée par l'hôte
            *outDataSize = sizeof(UInt32);
            return kAudioHardwareNoError;
        case kSelAlive:
        case kSelCanBeDefault:
        case kSelCanBeDefaultSys:
        case kSelStreamActive:
        case kSelVariableBuffers:
            *static_cast<UInt32*>(outData) = 1;
            *outDataSize = sizeof(UInt32);
            return kAudioHardwareNoError;
        case kSelIsHidden:
        case kSelLatency:
        case kSelSafetyOffset:
        case kSelHogMode:
        case kSelStartingChannel:
            *static_cast<UInt32*>(outData) = 0;
            *outDataSize = sizeof(UInt32);
            return kAudioHardwareNoError;
        case kSelRunning:
        case kSelRunningSomewhere:
            *static_cast<UInt32*>(outData) =
                g.ioClients.load(std::memory_order_relaxed) > 0 ? 1 : 0;
            *outDataSize = sizeof(UInt32);
            return kAudioHardwareNoError;
        case kSelBufferFrameSize:
            *static_cast<UInt32*>(outData) = kBufferFrames;
            *outDataSize = sizeof(UInt32);
            return kAudioHardwareNoError;
        case kSelBufferSizeRange:
        {
            if (inDataSize < sizeof(AudioValueRange)) return kAudioHardwareIllegalOperationError;
            auto* range = static_cast<AudioValueRange*>(outData);
            range->mMinimum = 16;
            range->mMaximum = 4096;
            *outDataSize = sizeof(AudioValueRange);
            return kAudioHardwareNoError;
        }
        case kSelNominalRate:
            if (inDataSize < sizeof(Float64)) return kAudioHardwareIllegalOperationError;
            *static_cast<Float64*>(outData) = kDeviceRate;
            *outDataSize = sizeof(Float64);
            return kAudioHardwareNoError;
        case kSelAvailableRates:
            if (inDataSize < sizeof(Float64)) return kAudioHardwareIllegalOperationError;
            *static_cast<Float64*>(outData) = kDeviceRate;  // 48 kHz unique (philosophie du pont)
            *outDataSize = sizeof(Float64);
            return kAudioHardwareNoError;

        // ---------------- flux
        case kSelDirection:
            *static_cast<UInt32*>(outData) = (inObjectID == kObjInputStream) ? 1 : 0;
            *outDataSize = sizeof(UInt32);
            return kAudioHardwareNoError;
        case kSelTerminalType:
            *static_cast<UInt32*>(outData) = (inObjectID == kObjInputStream) ? kTerminalMicro
                                                                             : kTerminalSpeaker;
            *outDataSize = sizeof(UInt32);
            return kAudioHardwareNoError;
        case kSelVirtualFormat:
        case kSelPhysicalFormat:
        case kSelAvailableVFormats:
        case kSelAvailablePFormats:
        {
            if (inDataSize < sizeof(AudioStreamBasicDescription)) return kAudioHardwareIllegalOperationError;
            *static_cast<AudioStreamBasicDescription*>(outData) = StreamFormat();
            *outDataSize = sizeof(AudioStreamBasicDescription);
            return kAudioHardwareNoError;
        }

        default:
            return kAudioHardwareUnknownPropertyError;
    }
}

OSStatus RoomEq_SetPropertyData(AudioServerPlugInDriverRef inDriver, AudioObjectID inObjectID,
                                pid_t inPid, const AudioObjectPropertyAddress* inAddress,
                                UInt32, const void*, UInt32, const void*)
{
    if (inAddress == nullptr) return kAudioHardwareIllegalOperationError;
    if (!RoomEq_HasProperty(inDriver, inObjectID, inPid, inAddress))
        return kAudioHardwareUnknownPropertyError;
    return kAudioHardwareIllegalOperationError;  // tout est en lecture seule
}

// ==================================================================== IO

OSStatus RoomEq_StartIO(AudioServerPlugInDriverRef, AudioObjectID inDevice, UInt32 /*inClient*/)
{
    if (inDevice != kObjDevice) return kAudioHardwareBadObjectError;

    if (!BridgeAttach())
        return kAudioHardwareUnspecifiedError;  // sans pont, le périphérique n'a pas de sens

    g.ioClients.fetch_add(1, std::memory_order_relaxed);
    std::atomic_thread_fence(std::memory_order_acquire);

    // curseur d'entrée : partir de « maintenant » (pas d'historique)
    g.loopPos = g.bridge->writePos;

    g.bridge->clientActive = 1;   // le greffon est « client » du pont (l'app = hôte)
    g.bridge->xruns = 0;
    return kAudioHardwareNoError;
}

OSStatus RoomEq_StopIO(AudioServerPlugInDriverRef, AudioObjectID inDevice, UInt32)
{
    if (inDevice != kObjDevice) return kAudioHardwareBadObjectError;

    if (g.ioClients.fetch_sub(1, std::memory_order_relaxed) == 1)
    {
        std::atomic_thread_fence(std::memory_order_release);
        if (g.bridge != nullptr) g.bridge->clientActive = 0;
    }
    return kAudioHardwareNoError;
}

OSStatus RoomEq_GetZeroTimeStamp(AudioServerPlugInDriverRef, AudioObjectID, UInt32,
                                 Float64*, UInt64*, UInt64*)
{
    // horloge non fournie (kAudioDeviceClockAlgorithmUnclocked) : ne sera pas appelé
    return kAudioHardwareIllegalOperationError;
}

OSStatus RoomEq_WillDoIOOperation(AudioServerPlugInDriverRef, AudioObjectID inDevice, UInt32,
                                  UInt32 inOperationID, Boolean* outWillDo, Boolean* outWillDoInPlace)
{
    if (outWillDo == nullptr || outWillDoInPlace == nullptr) return kAudioHardwareIllegalOperationError;
    if (inDevice != kObjDevice) return kAudioHardwareBadObjectError;

    switch (inOperationID)
    {
        case kOpReadInput:   // flux d'entrée : je remplis moi-même
        case kOpWriteMix:    // flux de sortie : je consomme le mix moi-même
            *outWillDo = true;
            *outWillDoInPlace = true;
            return kAudioHardwareNoError;
        default:
            *outWillDo = false;
            *outWillDoInPlace = false;
            return kAudioHardwareNoError;
    }
}

OSStatus RoomEq_BeginIOOperation(AudioServerPlugInDriverRef, AudioObjectID, UInt32, UInt32,
                                 UInt32, const AudioServerPlugInIOCycleInfo*)
{
    return kAudioHardwareNoError;
}

OSStatus RoomEq_DoIOOperation(AudioServerPlugInDriverRef, AudioObjectID inDevice,
                              AudioObjectID inStream, UInt32, UInt32 inOperationID,
                              UInt32 inIOBufferFrameSize,
                              const AudioServerPlugInIOCycleInfo*, void* ioMainBuffer, void*)
{
    if (inDevice != kObjDevice) return kAudioHardwareBadObjectError;
    if (g.bridge == nullptr || ioMainBuffer == nullptr || inIOBufferFrameSize == 0)
        return kAudioHardwareNoError;  // rien à faire : silence

    if (inOperationID == kOpWriteMix && inStream == kObjOutputStream)
    {
        // macOS a mixé ses clients : publier dans l'anneau du pont (drop-oldest)
        const int64_t n = static_cast<int64_t>(inIOBufferFrameSize);
        roomeq_asio_write(g.bridge, g.ring, n,
                          static_cast<const float*>(ioMainBuffer));
        return kAudioHardwareNoError;
    }

    if (inOperationID == kOpReadInput && inStream == kObjInputStream)
    {
        // boucle locale : rejouer ce qui vient d'être publié (curseur privé)
        const int64_t n = static_cast<int64_t>(inIOBufferFrameSize);
        int64_t writePos = g.bridge->writePos;

        // ne jamais prendre plus d'un anneau de retard
        if (writePos - g.loopPos > ROOMEQ_ASIO_RING_FRAMES)
            g.loopPos = writePos - ROOMEQ_ASIO_RING_FRAMES;
        if (g.loopPos > writePos) g.loopPos = writePos;  // session réinitialisée

        int64_t avail = writePos - g.loopPos;
        int64_t take = avail < n ? avail : n;

        if (take > 0)
            RingCopyOut(g.loopPos, take, static_cast<float*>(ioMainBuffer));

        if (take < n)
        {
            // rien de neuf : silence (fin de bloc)
            memset(static_cast<uint8_t*>(ioMainBuffer) +
                       (size_t)take * ROOMEQ_ASIO_CHANNELS * sizeof(float),
                   0,
                   (size_t)(n - take) * ROOMEQ_ASIO_CHANNELS * sizeof(float));
        }
        g.loopPos += n;  // avance toujours d'un bloc : la boucle reste synchrone de l'hôte
        return kAudioHardwareNoError;
    }

    return kAudioHardwareNoError;
}

OSStatus RoomEq_EndIOOperation(AudioServerPlugInDriverRef, AudioObjectID, UInt32, UInt32,
                               UInt32, const AudioServerPlugInIOCycleInfo*)
{
    return kAudioHardwareNoError;
}

}  // namespace
