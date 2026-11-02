// RoomEq — ABI des Audio Processing Objects Windows (spec I-AUD-09, N-APO-01).
//
// Déclarations AUTO-CONTENUES des interfaces COM que le moteur audio Windows
// (audiodg.exe) exige d'un sAPO, d'après les en-têtes SDK officiels
// (audioenginebaseapo.h, audiomediatype.h, AudioAPOTypes.h — Windows SDK
// 10.0.10240, vérifiés le 01/11/2026) :
//
//   IAudioProcessingObject        {FD7F2B29-24D0-4b5c-B177-592C39F9CA10}
//   IAudioProcessingObjectRT      {9E1D6A6D-DDBC-4E95-A4C7-AD64BA37846C}
//   IAudioProcessingObjectConfiguration {0E5ED805-ABA6-49c3-8F9A-2B8C889C4FA8}
//   IAudioSystemEffects           {5FA00F27-ADD6-499a-8A9D-6B98521FA75B}
//
// Pourquoi ne pas inclure <windows.h> : le même code doit compiler à l'identique
// (1) en DLL Windows via Zig/MinGW et (2) dans le harnais de tests natif Linux
// (roomeq_apo_tests) — les types de base sont donc redéfinis ici avec des
// tailles exactitudes identiques au SDK. Les layouts de vtable produits par
// g++/clang pour cet héritage COM multiple sont identiques à ceux de MSVC
// (pattern COM standard, utilisé par Wine et tout le code MinGW COM).
#pragma once

#include <cstddef>
#include <cstdint>

// ============================================================ types de base
#if !defined(ROOMEQ_APO_ABI_TYPES)
#define ROOMEQ_APO_ABI_TYPES

using HRESULT = long;
using ULONG = unsigned long;
using UINT = unsigned int;
using UINT32 = unsigned int;
using DWORD = unsigned long;
using BOOL = int;
using BYTE = unsigned char;
using WCHAR = wchar_t;
using LONGLONG = long long;
using HNSTIME = LONGLONG;      // 100 ns units
using UINT_PTR = std::uintptr_t;

constexpr long S_OK_ = 0;
constexpr long S_FALSE_ = 1;
constexpr long E_NOINTERFACE_ = static_cast<long>(0x80004002L);
constexpr long E_POINTER_ = static_cast<long>(0x80004003L);
constexpr long E_INVALIDARG_ = static_cast<long>(0x80070057L);

struct GUID {
    uint32_t Data1;
    uint16_t Data2;
    uint16_t Data3;
    uint8_t Data4[8];
};
using CLSID = GUID;
using IID = GUID;

inline bool operator==(const GUID& a, const GUID& b) {
    return a.Data1 == b.Data1 && a.Data2 == b.Data2 && a.Data3 == b.Data3
        && a.Data4[0] == b.Data4[0] && a.Data4[1] == b.Data4[1]
        && a.Data4[2] == b.Data4[2] && a.Data4[3] == b.Data4[3]
        && a.Data4[4] == b.Data4[4] && a.Data4[5] == b.Data4[5]
        && a.Data4[6] == b.Data4[6] && a.Data4[7] == b.Data4[7];
}
inline bool operator!=(const GUID& a, const GUID& b) { return !(a == b); }

// Construction littérale d'un GUID (sans dépendre des helpers MIDL).
constexpr GUID RoomeqGuid(uint32_t d1, uint16_t d2, uint16_t d3,
                          uint8_t b0, uint8_t b1, uint8_t b2, uint8_t b3,
                          uint8_t b4, uint8_t b5, uint8_t b6, uint8_t b7) {
    return GUID{d1, d2, d3, {b0, b1, b2, b3, b4, b5, b6, b7}};
}

#ifndef STDMETHODCALLTYPE
#define STDMETHODCALLTYPE
#endif
#ifndef WINAPI
#define WINAPI
#endif

#endif // ROOMEQ_APO_ABI_TYPES

// ============================================================ IIDs officiels
// Valeurs du SDK — le harnais de tests les re-vérifie contre les littéraux
// pour garantir à jamais l'absence de faute de frappe (une IID fausse =
// l'engine refuse l'APO, puis désactive les effets système après 10 échecs).
inline const GUID IID_IUnknown_ =
    RoomeqGuid(0x00000000, 0x0000, 0x0000, 0xC0, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x46);
inline const GUID IID_IClassFactory_ =
    RoomeqGuid(0x00000001, 0x0000, 0x0000, 0xC0, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x46);
inline const GUID IID_IAudioProcessingObject =
    RoomeqGuid(0xFD7F2B29, 0x24D0, 0x4b5c, 0xB1, 0x77, 0x59, 0x2C, 0x39, 0xF9, 0xCA, 0x10);
inline const GUID IID_IAudioProcessingObjectRT =
    RoomeqGuid(0x9E1D6A6D, 0xDDBC, 0x4E95, 0xA4, 0xC7, 0xAD, 0x64, 0xBA, 0x37, 0x84, 0x6C);
inline const GUID IID_IAudioProcessingObjectConfiguration =
    RoomeqGuid(0x0E5ED805, 0xABA6, 0x49c3, 0x8F, 0x9A, 0x2B, 0x8C, 0x88, 0x9C, 0x4F, 0xA8);
inline const GUID IID_IAudioSystemEffects =
    RoomeqGuid(0x5FA00F27, 0xADD6, 0x499a, 0x8A, 0x9D, 0x6B, 0x98, 0x52, 0x1F, 0xA7, 0x5B);

// CLSID public de l'APO RoomEq — identique à celui inscrit au registre par
// RoomEq.ApoSetup (src/RoomEq.Audio/SystemApo/ApoConstants.cs).
inline const GUID CLSID_RoomEqApo =
    RoomeqGuid(0xEAA24610, 0x59C6, 0x4591, 0xA2, 0x3E, 0x9D, 0x0A, 0x2C, 0x4E, 0x1A, 0x15);

// ============================================================ structures APO

enum APO_CONNECTION_BUFFER_FLAGS {
    BUFFER_INVALID = 0,
    BUFFER_VALID = 1,
    BUFFER_SILENT = 2,
};

// AudioAPOTypes.h — tampon d'une connexion au moment du traitement.
struct APO_CONNECTION_PROPERTY {
    UINT_PTR pBuffer;              // float* entrelacé
    UINT32 u32ValidFrameCount;     // trames valides
    APO_CONNECTION_BUFFER_FLAGS u32BufferFlag;
    UINT32 u32Signature;           // 'ACPS' ; V2 pour l'AEC uniquement
};

enum APO_CONNECTION_BUFFER_TYPE {
    APO_CONNECTION_BUFFER_TYPE_ALLOCATED = 0,
    APO_CONNECTION_BUFFER_TYPE_EXTERNAL = 1,
    APO_CONNECTION_BUFFER_TYPE_DEPENDANT = 2,
};

enum APO_FLAG {
    APO_FLAG_NONE = 0,
    APO_FLAG_INPLACE = 0x1,
    APO_FLAG_SAMPLESPERFRAME_MUST_MATCH = 0x2,
    APO_FLAG_FRAMESPERSECOND_MUST_MATCH = 0x4,
    APO_FLAG_BITSPERSAMPLE_MUST_MATCH = 0x8,
    APO_FLAG_MIXER = 0x10,
    APO_FLAG_DEFAULT =
        (APO_FLAG_SAMPLESPERFRAME_MUST_MATCH | APO_FLAG_FRAMESPERSECOND_MUST_MATCH
         | APO_FLAG_BITSPERSAMPLE_MUST_MATCH),
};

// audiomediatype.h — format non compressé exposé par IAudioMediaType.
struct UNCOMPRESSEDAUDIOFORMAT {
    GUID guidFormatType;
    DWORD dwSamplesPerFrame;         // canaux
    DWORD dwBytesPerSampleContainer;
    DWORD dwValidBitsPerSample;
    float fFramesPerSecond;
    DWORD dwChannelMask;
};

// APO_REG_PROPERTIES (rendu par GetRegistrationProperties, tampon
// CoTaskMemAlloc — l'appelant le libère par CoTaskMemFree).
struct APO_REG_PROPERTIES {
    CLSID clsid;
    APO_FLAG Flags;
    WCHAR szFriendlyName[256];
    WCHAR szCopyrightInfo[256];
    UINT32 u32MajorVersion;
    UINT32 u32MinorVersion;
    UINT32 u32MinInputConnections;
    UINT32 u32MaxInputConnections;
    UINT32 u32MinOutputConnections;
    UINT32 u32MaxOutputConnections;
    UINT32 u32MaxInstances;
    UINT32 u32NumAPOInterfaces;
    IID iidAPOInterfaceList[1];
};

// audioenginebaseapo.h — déclaration de connexion (LockForProcess).
struct APO_CONNECTION_DESCRIPTOR {
    APO_CONNECTION_BUFFER_TYPE Type;
    UINT_PTR pBuffer;
    UINT32 u32MaxFrameCount;
    struct IAudioMediaType* pFormat;
    UINT32 u32Signature;
};

struct APOInitBaseStruct {
    UINT32 cbSize;
    CLSID clsid;
};
// APOInitSystemEffects : { APOInitBaseStruct; IPropertyStore*; GUID } — traité
// comme opaque par l'APO (aucun accès aux membres : Initialize ne doit jamais
// échouer, cf. compteur d'échecs du moteur audio).

// ============================================================ interfaces

struct IUnknown {
    virtual HRESULT STDMETHODCALLTYPE QueryInterface(const GUID& riid, void** ppv) = 0;
    virtual ULONG STDMETHODCALLTYPE AddRef() = 0;
    virtual ULONG STDMETHODCALLTYPE Release() = 0;
};

struct IAudioMediaType : IUnknown {
    virtual HRESULT STDMETHODCALLTYPE IsCompressedFormat(BOOL* pfCompressed) = 0;
    virtual HRESULT STDMETHODCALLTYPE IsEqual(IAudioMediaType* pIAudioType, DWORD* pdwFlags) = 0;
    virtual const void* STDMETHODCALLTYPE GetAudioFormat() = 0; // WAVEFORMATEX*
    virtual HRESULT STDMETHODCALLTYPE GetUncompressedAudioFormat(
        UNCOMPRESSEDAUDIOFORMAT* pUncompressedAudioFormat) = 0;
};

struct IAudioProcessingObject : IUnknown {
    virtual HRESULT STDMETHODCALLTYPE Reset() = 0;
    virtual HRESULT STDMETHODCALLTYPE GetLatency(HNSTIME* pTime) = 0;
    virtual HRESULT STDMETHODCALLTYPE GetRegistrationProperties(APO_REG_PROPERTIES** ppRegProps) = 0;
    virtual HRESULT STDMETHODCALLTYPE Initialize(UINT32 cbDataSize, BYTE* pbyData) = 0;
    virtual HRESULT STDMETHODCALLTYPE IsInputFormatSupported(
        IAudioMediaType* pOppositeFormat, IAudioMediaType* pRequestedInputFormat,
        IAudioMediaType** ppSupportedInputFormat) = 0;
    virtual HRESULT STDMETHODCALLTYPE IsOutputFormatSupported(
        IAudioMediaType* pOppositeFormat, IAudioMediaType* pRequestedOutputFormat,
        IAudioMediaType** ppSupportedOutputFormat) = 0;
    virtual HRESULT STDMETHODCALLTYPE GetInputChannelCount(UINT32* pu32ChannelCount) = 0;
};

struct IAudioProcessingObjectRT : IUnknown {
    virtual void STDMETHODCALLTYPE APOProcess(
        UINT32 u32NumInputConnections, APO_CONNECTION_PROPERTY** ppInputConnections,
        UINT32 u32NumOutputConnections, APO_CONNECTION_PROPERTY** ppOutputConnections) = 0;
    virtual UINT32 STDMETHODCALLTYPE CalcInputFrames(UINT32 u32OutputFrameCount) = 0;
    virtual UINT32 STDMETHODCALLTYPE CalcOutputFrames(UINT32 u32InputFrameCount) = 0;
};

struct IAudioProcessingObjectConfiguration : IUnknown {
    virtual HRESULT STDMETHODCALLTYPE LockForProcess(
        UINT32 u32NumInputConnections, APO_CONNECTION_DESCRIPTOR** ppInputConnections,
        UINT32 u32NumOutputConnections, APO_CONNECTION_DESCRIPTOR** ppOutputConnections) = 0;
    virtual HRESULT STDMETHODCALLTYPE UnlockForProcess() = 0;
};

struct IAudioSystemEffects : IUnknown {
};

// ============================================================ agrégation COM
// Le moteur audio (audiodg) instancie les APO en AGRÉGATION COM : la
// fabrique reçoit un pUnkOuter NON NUL et l'objet doit exposer un IUnknown
// NON DÉLÉGUANT (identité interne). Refuser l'agrégation — CLASS_E_
// NOAGGREGATION — fait abandonner l'APO par le moteur : le graphe de la
// sortie ne se construit pas, la sortie devient MUETTE (retour terrain
// v3.9.8 : la sonde WASAPI a remonté exactement 0x80040110).
//
// Technique Equalizer APO (EqualizerAPO.cpp) : une interface « non
// déléguante » dont la vtable est BINAIREMENT IDENTIQUE à IUnknown (trois
// méthodes, mêmes signatures, même ordre — seuls les noms diffèrent). Le
// pointeur du sous-objet peut donc être lu comme un IUnknown* par le
// contrôleur : reinterpret_cast sûr, pattern COM classique (ATL, Wine).
// v3.9.9.
struct INonDelegatingUnknown {
    virtual HRESULT STDMETHODCALLTYPE NDQueryInterface(const GUID& riid, void** ppv) = 0;
    virtual ULONG STDMETHODCALLTYPE NDAddRef() = 0;
    virtual ULONG STDMETHODCALLTYPE NDRelease() = 0;
};

struct IClassFactory : IUnknown {
    virtual HRESULT STDMETHODCALLTYPE CreateInstance(
        IUnknown* pUnkOuter, const GUID& riid, void** ppv) = 0;
    virtual HRESULT STDMETHODCALLTYPE LockServer(BOOL fLock) = 0;
};

// ============================================================ allocation COM
// Sur Windows : redirection vers ole32 (apo/win_compat.cpp). Dans le harnais
// Linux : implémentées par tests/test_main.cpp (malloc/free).
extern "C" {
void* WINAPI RoomeqCoTaskMemAlloc(size_t cb);
void WINAPI RoomeqCoTaskMemFree(void* pv);
}
