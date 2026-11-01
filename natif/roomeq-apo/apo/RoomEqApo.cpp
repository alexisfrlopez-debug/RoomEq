// RoomEq APO — objet COM « EQ système » : implémentation (spec I-AUD-09).
#include "RoomEqApo.h"

#include <cstdint>
#include <chrono>
#include <cstdio>
#include <cstring>
#include <cwchar>
#include <new>
#include <thread>

namespace roomeq::apo {

namespace {

// KSDATAFORMAT_SUBTYPE_IEEE_FLOAT (mmreg.h) : le format de mixage du moteur
// audio Windows. Tout autre format → pass-through prudent (jamais d'échec).
const GUID kFormatIeeeFloat =
    RoomeqGuid(0x00000003, 0x0000, 0x0010, 0x80, 0x00, 0x00, 0xAA, 0x00, 0x38, 0x9B, 0x71);

constexpr long kFadeMs = 20;      // durée du fondu enchaîné entre deux courbes
constexpr HRESULT kClassNoAggregation = static_cast<HRESULT>(0x80040110L);

void CopyWstr(WCHAR* dst, const wchar_t* src, size_t cap)
{
    size_t i = 0;
    for (; i + 1 < cap && src[i] != L'\0'; ++i)
        dst[i] = src[i];
    for (; i < cap; ++i)
        dst[i] = L'\0';
}

} // namespace

// ================================================================ IUnknown

RoomEqApo::RoomEqApo()
{
    Log("APO instancie (RoomEqApo)");
}

RoomEqApo::~RoomEqApo()
{
    ReleaseChains();
    watcher_.Stop();
    Log("APO detruit");
}

HRESULT STDMETHODCALLTYPE RoomEqApo::QueryInterface(const GUID& riid, void** ppv)
{
    if (ppv == nullptr)
        return E_POINTER_;
    if (riid == IID_IUnknown_) {
        *ppv = static_cast<IUnknown*>(static_cast<IAudioProcessingObject*>(this));
    } else if (riid == IID_IAudioProcessingObject) {
        *ppv = static_cast<IAudioProcessingObject*>(this);
    } else if (riid == IID_IAudioProcessingObjectRT) {
        *ppv = static_cast<IAudioProcessingObjectRT*>(this);
    } else if (riid == IID_IAudioProcessingObjectConfiguration) {
        *ppv = static_cast<IAudioProcessingObjectConfiguration*>(this);
    } else if (riid == IID_IAudioSystemEffects) {
        *ppv = static_cast<IAudioSystemEffects*>(this);
    } else {
        *ppv = nullptr;
        return E_NOINTERFACE_;
    }
    static_cast<IUnknown*>(*ppv)->AddRef();
    return S_OK_;
}

ULONG STDMETHODCALLTYPE RoomEqApo::AddRef()
{
    return static_cast<ULONG>(refs_.fetch_add(1, std::memory_order_relaxed) + 1);
}

ULONG STDMETHODCALLTYPE RoomEqApo::Release()
{
    long n = refs_.fetch_sub(1, std::memory_order_acq_rel);
    if (n == 1) {
        delete this;
        return 0;
    }
    return static_cast<ULONG>(n - 1);
}

// ==================================================== IAudioProcessingObject

HRESULT STDMETHODCALLTYPE RoomEqApo::Reset()
{
    // Ne touche jamais les états depuis un thread de contrôle concurrent au
    // thread audio : demande différée, honorée au prochain bloc.
    resetRequested_.store(true, std::memory_order_release);
    return S_OK_;
}

HRESULT STDMETHODCALLTYPE RoomEqApo::GetLatency(HNSTIME* pTime)
{
    if (pTime == nullptr)
        return E_POINTER_;
    *pTime = 0; // l'APO traite en place dans le moteur : latence ajoutée nulle
    return S_OK_;
}

HRESULT STDMETHODCALLTYPE RoomEqApo::GetRegistrationProperties(APO_REG_PROPERTIES** ppRegProps)
{
    if (ppRegProps == nullptr)
        return E_POINTER_;
    auto* props = static_cast<APO_REG_PROPERTIES*>(
        RoomeqCoTaskMemAlloc(sizeof(APO_REG_PROPERTIES)));
    if (props == nullptr)
        return E_POINTER_;
    std::memset(props, 0, sizeof *props);
    props->clsid = CLSID_RoomEqApo;
    props->Flags = static_cast<APO_FLAG>(APO_FLAG_DEFAULT | APO_FLAG_INPLACE);
    CopyWstr(props->szFriendlyName, L"RoomEq - egaliseur systeme", 256);
    CopyWstr(props->szCopyrightInfo, L"RoomEq", 256);
    props->u32MajorVersion = 1;
    props->u32MinorVersion = 0;
    props->u32MinInputConnections = 1;
    props->u32MaxInputConnections = 1;
    props->u32MinOutputConnections = 1;
    props->u32MaxOutputConnections = 1;
    props->u32MaxInstances = 64;
    props->u32NumAPOInterfaces = 1;
    props->iidAPOInterfaceList[0] = IID_IAudioProcessingObjectRT;
    *ppRegProps = props;
    return S_OK_;
}

HRESULT STDMETHODCALLTYPE RoomEqApo::Initialize(UINT32 cbDataSize, BYTE* /*pbyData*/)
{
    // Données d'initialisation (APOInitSystemEffects) traitées comme opaques :
    // un refus compterait dans le budget d'échecs du moteur audio (10 échecs
    // = effets système désactivés pour ce périphérique) — jamais d'échec.
    char buf[96];
    std::snprintf(buf, sizeof buf, "Initialize(cbDataSize=%u) accepte", cbDataSize);
    Log(buf);
    return S_OK_;
}

HRESULT STDMETHODCALLTYPE RoomEqApo::IsInputFormatSupported(IAudioMediaType* /*pOpposite*/,
    IAudioMediaType* /*pRequested*/, IAudioMediaType** ppSupported)
{
    if (ppSupported != nullptr)
        *ppSupported = nullptr;
    return S_OK_; // format-agnostique : le moteur feed le format de mixage
}

HRESULT STDMETHODCALLTYPE RoomEqApo::IsOutputFormatSupported(IAudioMediaType* /*pOpposite*/,
    IAudioMediaType* /*pRequested*/, IAudioMediaType** ppSupported)
{
    if (ppSupported != nullptr)
        *ppSupported = nullptr;
    return S_OK_;
}

HRESULT STDMETHODCALLTYPE RoomEqApo::GetInputChannelCount(UINT32* pu32ChannelCount)
{
    if (pu32ChannelCount == nullptr)
        return E_POINTER_;
    *pu32ChannelCount = channels_ != 0 ? channels_ : 2;
    return S_OK_;
}

// ============================================================ IAudioProcessingObjectRT

UINT32 STDMETHODCALLTYPE RoomEqApo::CalcInputFrames(UINT32 u32OutputFrameCount)
{
    return u32OutputFrameCount; // identité : ni lookahead ni tampon ajouté
}

UINT32 STDMETHODCALLTYPE RoomEqApo::CalcOutputFrames(UINT32 u32InputFrameCount)
{
    return u32InputFrameCount;
}

void STDMETHODCALLTYPE RoomEqApo::APOProcess(UINT32 u32NumInputConnections,
    APO_CONNECTION_PROPERTY** ppInputConnections, UINT32 u32NumOutputConnections,
    APO_CONNECTION_PROPERTY** ppOutputConnections)
{
    if (u32NumInputConnections == 0 || u32NumOutputConnections == 0)
        return;
    APO_CONNECTION_PROPERTY* in = ppInputConnections[0];
    APO_CONNECTION_PROPERTY* out = ppOutputConnections[0];
    if (in == nullptr || out == nullptr)
        return;

    // hors verrouillage : pas de traitement du tout (le moteur n'appelle
    // jamais ici, défense en profondeur — le tampon reste intact)
    if (!locked_)
        return;

    // BUFFER_INVALID : comportement Equalizer APO — l'APO ne touche à rien.
    if (in->u32BufferFlag != BUFFER_VALID && in->u32BufferFlag != BUFFER_SILENT)
        return;

    float* inBuf = reinterpret_cast<float*>(in->pBuffer);
    float* outBuf = reinterpret_cast<float*>(out->pBuffer);
    const UINT32 frames = in->u32ValidFrameCount;
    const UINT32 ch = channels_;

    // Éventuelle nouvelle courbe publiée par le thread de fond : adoption par
    // atomiques uniquement (aucune allocation, aucun verrou).
    AdoptPublishedChainLocked();

    // Reset demandé (contrôle) : honoré ici, sur le thread audio.
    if (resetRequested_.exchange(false, std::memory_order_acq_rel)) {
        if (current_ != nullptr)
            current_->eq.Reset();
        if (retiring_ != nullptr)
            retiring_->eq.Reset();
    }

    const bool usable = locked_ && ch != 0 && floatFormat_ && inBuf != nullptr
        && outBuf != nullptr && current_ != nullptr && !current_->bypass;

    if (!usable) {
        // pass-through transparent (aucune courbe, bypass, format inattendu)
        if (frames != 0 && ch != 0 && inBuf != nullptr && outBuf != nullptr) {
            if (in->u32BufferFlag == BUFFER_SILENT) {
                std::memset(outBuf, 0, static_cast<size_t>(frames) * ch * sizeof(float));
            } else {
                std::memcpy(outBuf, inBuf, static_cast<size_t>(frames) * ch * sizeof(float));
            }
        }
        out->u32ValidFrameCount = frames;
        out->u32BufferFlag = in->u32BufferFlag;
        return;
    }

    // ---- traitement : voies 0/L et 1/R égalisées, canaux suivants intacts.
    // Par trame (2 échantillons max) : sûr en place (in == out possible),
    // et compatible avec le fondu qui doit relire l'entrée d'origine.
    const UINT32 eqCh = (ch == 1) ? 1u : 2u;
    float frame[2] = {0.0f, 0.0f};

    for (UINT32 f = 0; f < frames; ++f) {
        float* ip = inBuf + static_cast<size_t>(f) * ch;
        float* op = outBuf + static_cast<size_t>(f) * ch;

        frame[0] = ip[0];
        if (eqCh == 2)
            frame[1] = ip[1];

        current_->eq.Process(frame, eqCh);
        const float n0 = frame[0];
        const float n1 = frame[1];

        if (fadeRemaining_ > 0) {
            // fondu enchaîné : l'ancien rendu (chaîne sortante ou entrée
            // brute au démarrage) perd du poids linéairement, trame par trame
            float o0, o1;
            if (retiring_ != nullptr) {
                float old[2] = {ip[0], (eqCh == 2) ? ip[1] : 0.0f};
                retiring_->eq.Process(old, eqCh);
                o0 = old[0];
                o1 = old[1];
            } else {
                o0 = ip[0];
                o1 = (eqCh == 2) ? ip[1] : 0.0f;
            }
            const double wOld = static_cast<double>(fadeRemaining_)
                / static_cast<double>(fadeLength_ + 1);
            --fadeRemaining_;
            op[0] = static_cast<float>(wOld * o0 + (1.0 - wOld) * n0);
            if (eqCh == 2)
                op[1] = static_cast<float>(wOld * o1 + (1.0 - wOld) * n1);
        } else {
            op[0] = n0;
            if (eqCh == 2)
                op[1] = n1;
        }

        for (UINT32 c = eqCh; c < ch; ++c)
            op[c] = ip[c];
    }

    if (retiring_ != nullptr && fadeRemaining_ <= 0) {
        // La libération mémoire se fait sur le thread de fond (cimetière) :
        // le chemin temps réel ne fait NI allocation NI libération.
        watcher_.RetireOnBackgroundThread(retiring_);
        retiring_ = nullptr;
    }

    // Drapeaux de sortie — comportement conservateur d'Equalizer APO : le
    // BUFFER_SILENT est important pour certains pilotes, on le préserve.
    out->u32ValidFrameCount = frames;
    if (in->u32BufferFlag == BUFFER_SILENT) {
        std::memset(outBuf, 0, static_cast<size_t>(frames) * ch * sizeof(float));
        out->u32BufferFlag = BUFFER_SILENT;
    } else {
        out->u32BufferFlag = BUFFER_VALID;
    }
}

// ================================================ IAudioProcessingObjectConfiguration

HRESULT STDMETHODCALLTYPE RoomEqApo::LockForProcess(UINT32 u32NumInputConnections,
    APO_CONNECTION_DESCRIPTOR** ppInputConnections, UINT32 u32NumOutputConnections,
    APO_CONNECTION_DESCRIPTOR** ppOutputConnections)
{
    // Jamais d'échec (budget d'échecs du moteur audio, cf. en-tête) : toute
    // anomalie dégrade en pass-through et se consigne.
    do {
        if (u32NumInputConnections == 0 || u32NumOutputConnections == 0)
            break;
        // le format de référence est celui de l'entrée ; à défaut, la sortie
        APO_CONNECTION_DESCRIPTOR* desc = ppInputConnections[0];
        if (desc == nullptr || desc->pFormat == nullptr)
            desc = ppOutputConnections[0];
        if (desc == nullptr || desc->pFormat == nullptr)
            break;

        UNCOMPRESSEDAUDIOFORMAT fmt{};
        if (desc->pFormat->GetUncompressedAudioFormat(&fmt) != S_OK_)
            break;

        floatFormat_ = fmt.dwBytesPerSampleContainer == 4 && fmt.guidFormatType == kFormatIeeeFloat;
        rate_ = static_cast<uint32_t>(fmt.fFramesPerSecond + 0.5);
        channels_ = fmt.dwSamplesPerFrame;

        char buf[160];
        std::snprintf(buf, sizeof buf,
                      "LockForProcess : %u Hz, %u canaux, conteneur %u octets, float=%d",
                      rate_, channels_, static_cast<unsigned>(fmt.dwBytesPerSampleContainer),
                      floatFormat_ ? 1 : 0);
        Log(buf);
    } while (false);

    if (locked_)
        return S_OK_; // re-verrouillage sans déverrouillage : toléré

    locked_ = true;
    fadeLength_ = static_cast<long>(static_cast<long>(rate_) * kFadeMs / 1000);
    if (fadeLength_ < 1)
        fadeLength_ = 1;
    fadeRemaining_ = 0;

    watcher_.ConfigureForProcess(rate_ != 0 ? rate_ : 48000,
                                 channels_ <= 1 ? 1 : 2); // l'EQ traite L/R (ou mono)
    watcher_.RebuildForFormat(); // nouveau format ? la chaîne doit être reconstruite
    watcher_.Start(testPollMs_);
    current_ = watcher_.Acquire();
    if (current_ == nullptr)
        Log("aucune courbe active : pass-through jusqu'a premiere publication");
    return S_OK_;
}

HRESULT STDMETHODCALLTYPE RoomEqApo::UnlockForProcess()
{
    ReleaseChains();
    watcher_.Stop();
    locked_ = false;
    Log("UnlockForProcess");
    return S_OK_;
}

// ================================================================ internes

void RoomEqApo::AdoptPublishedChainLocked()
{
    Chain* latest = watcher_.Acquire(); // peut être null (aucune courbe)
    if (latest == nullptr)
        return;
    if (latest == current_) {
        latest->Release(); // déjà active
        return;
    }

    // Nouvelle courbe : l'ancienne part en fondu sortant (si le fondu
    // précédent n'était pas fini, coupure franche — bornée à 20 ms, rare).
    // Aucun Log ici : APOProcess est temps réel (le thread de fond consigne
    // déjà la publication).
    if (retiring_ != nullptr) {
        watcher_.RetireOnBackgroundThread(retiring_);
        retiring_ = nullptr;
    }
    retiring_ = current_;      // peut être null : fondu depuis le pass-through
    current_ = latest;        // conserve la référence acquise
    fadeRemaining_ = fadeLength_;
}

void RoomEqApo::ReleaseChains()
{
    if (current_ != nullptr) {
        current_->Release();
        current_ = nullptr;
    }
    if (retiring_ != nullptr) {
        retiring_->Release();
        retiring_ = nullptr;
    }
    fadeRemaining_ = 0;
}

bool RoomEqApo::TestWaitFileChanges(uint64_t n, int timeoutMs) const
{
    for (int waited = 0; waited < timeoutMs; waited += 2) {
        if (TestFileChangesSeen() >= n)
            return true;
        std::this_thread::sleep_for(std::chrono::milliseconds(2));
    }
    return TestFileChangesSeen() >= n;
}

// ================================================================ fabrique

HRESULT STDMETHODCALLTYPE RoomEqApoClassFactory::QueryInterface(const GUID& riid, void** ppv)
{
    if (ppv == nullptr)
        return E_POINTER_;
    if (riid == IID_IUnknown_ || riid == IID_IClassFactory_) {
        *ppv = static_cast<IClassFactory*>(this);
    } else {
        *ppv = nullptr;
        return E_NOINTERFACE_;
    }
    static_cast<IUnknown*>(*ppv)->AddRef();
    return S_OK_;
}

ULONG STDMETHODCALLTYPE RoomEqApoClassFactory::AddRef()
{
    return static_cast<ULONG>(refs_.fetch_add(1, std::memory_order_relaxed) + 1);
}

ULONG STDMETHODCALLTYPE RoomEqApoClassFactory::Release()
{
    long n = refs_.fetch_sub(1, std::memory_order_acq_rel);
    if (n == 1) {
        delete this;
        return 0;
    }
    return static_cast<ULONG>(n - 1);
}

HRESULT STDMETHODCALLTYPE RoomEqApoClassFactory::CreateInstance(IUnknown* pUnkOuter,
    const GUID& riid, void** ppv)
{
    if (ppv == nullptr)
        return E_POINTER_;
    *ppv = nullptr;
    if (pUnkOuter != nullptr)
        return kClassNoAggregation; // pas d'agrégation COM

    auto* apo = new (std::nothrow) RoomEqApo();
    if (apo == nullptr)
        return E_POINTER_;
    const HRESULT hr = apo->QueryInterface(riid, ppv);
    apo->Release();
    return hr;
}

HRESULT STDMETHODCALLTYPE RoomEqApoClassFactory::LockServer(BOOL /*fLock*/)
{
    return S_OK_;
}

// ================================================================ entrée DLL

HRESULT WINAPI RoomeqDllGetClassObject(const GUID& rclsid, const GUID& riid, void** ppv)
{
    if (ppv == nullptr)
        return E_POINTER_;
    *ppv = nullptr;
    if (rclsid != CLSID_RoomEqApo)
        return static_cast<HRESULT>(0x80040154L); // REGDB_E_CLASSNOTREG
    auto* factory = new (std::nothrow) RoomEqApoClassFactory();
    if (factory == nullptr)
        return E_POINTER_;
    const HRESULT hr = factory->QueryInterface(riid, ppv);
    factory->Release();
    return hr;
}

HRESULT WINAPI RoomeqDllCanUnloadNow()
{
    return S_FALSE_; // jamais déchargée : la vie de l'APO est celle d'audiodg
}

} // namespace roomeq::apo

// ================================================================ entrée DLL
// (déclarées extern "C" dans RoomEqApo.h — liaison C, hors namespace)

extern "C" HRESULT WINAPI RoomeqDllGetClassObject(const GUID& rclsid, const GUID& riid,
                                                  void** ppv)
{
    return roomeq::apo::RoomeqDllGetClassObject(rclsid, riid, ppv);
}

extern "C" HRESULT WINAPI RoomeqDllCanUnloadNow()
{
    return roomeq::apo::RoomeqDllCanUnloadNow();
}
