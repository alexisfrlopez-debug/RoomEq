// RoomEq APO — objet COM « EQ système » (spec I-AUD-09, N-APO-01).
//
// Position EFX (endpoint) : l'objet est instancié par audiodg.exe pour chaque
// sortie Windows où l'application a inscrit notre CLSID dans FxProperties.
// Le traitement s'applique aux voies L/R (voix 0 et 1), les autres canaux
// passent intacts ; mono = voie 0. Fondu enchaîné linéaire (~20 ms) à chaque
// changement de courbe : le son ne s'interrompt jamais.
//
// RÈGLE DE SURVIE (Microsoft Learn, Implementing Audio Processing Objects) :
// après 10 échecs (CoCreateInstance, QueryInterface, LockForProcess…), le
// moteur DÉSACTIVE les effets système du périphérique (PKEY_Endpoint_
// Disable_SysFx=1) — y compris ceux du fabricant. Conséquence : aucune
// méthode ne retourne jamais un code d'échec sur une entrée inattendue ; le
// traitement se dégrade en pass-through et tout est consigné dans apo.log.
#pragma once

#include "apo_abi.h"
#include "apo_config.h"

#include <atomic>

namespace roomeq::apo {

/// L'objet APO RoomEq. Héritage COM multiple standard : le compilateur
/// génère une vtable par interface de base, identique au layout MIDL/MSVC.
class RoomEqApo final
    : public IAudioProcessingObject
    , public IAudioProcessingObjectRT
    , public IAudioProcessingObjectConfiguration
    , public IAudioSystemEffects {
public:
    RoomEqApo();
    ~RoomEqApo(); // libéré via Release() — pas de destructeur virtuel COM

    // ---- IUnknown (commun aux quatre bases) ----
    HRESULT STDMETHODCALLTYPE QueryInterface(const GUID& riid, void** ppv) override;
    ULONG STDMETHODCALLTYPE AddRef() override;
    ULONG STDMETHODCALLTYPE Release() override;
    // ---- IAudioProcessingObject ----
    HRESULT STDMETHODCALLTYPE Reset() override;
    HRESULT STDMETHODCALLTYPE GetLatency(HNSTIME* pTime) override;
    HRESULT STDMETHODCALLTYPE GetRegistrationProperties(APO_REG_PROPERTIES** ppRegProps) override;
    HRESULT STDMETHODCALLTYPE Initialize(UINT32 cbDataSize, BYTE* pbyData) override;
    HRESULT STDMETHODCALLTYPE IsInputFormatSupported(IAudioMediaType* pOppositeFormat,
        IAudioMediaType* pRequestedInputFormat, IAudioMediaType** ppSupportedInputFormat) override;
    HRESULT STDMETHODCALLTYPE IsOutputFormatSupported(IAudioMediaType* pOppositeFormat,
        IAudioMediaType* pRequestedOutputFormat, IAudioMediaType** ppSupportedOutputFormat) override;
    HRESULT STDMETHODCALLTYPE GetInputChannelCount(UINT32* pu32ChannelCount) override;

    // ---- IAudioProcessingObjectRT (thread temps réel : aucun verrou,
    //      aucune allocation, aucune E/O — uniquement atomiques + calcul) ----
    void STDMETHODCALLTYPE APOProcess(UINT32 u32NumInputConnections,
        APO_CONNECTION_PROPERTY** ppInputConnections, UINT32 u32NumOutputConnections,
        APO_CONNECTION_PROPERTY** ppOutputConnections) override;
    UINT32 STDMETHODCALLTYPE CalcInputFrames(UINT32 u32OutputFrameCount) override;
    UINT32 STDMETHODCALLTYPE CalcOutputFrames(UINT32 u32InputFrameCount) override;

    // ---- IAudioProcessingObjectConfiguration ----
    HRESULT STDMETHODCALLTYPE LockForProcess(UINT32 u32NumInputConnections,
        APO_CONNECTION_DESCRIPTOR** ppInputConnections, UINT32 u32NumOutputConnections,
        APO_CONNECTION_DESCRIPTOR** ppOutputConnections) override;
    HRESULT STDMETHODCALLTYPE UnlockForProcess() override;

    // ---- coutures de diagnostic (harnais de tests uniquement) ----
    uint64_t TestRevision() const { return watcher_.CurrentRevision(); }
    bool TestIsPassthrough() const { return !locked_ || channels_ == 0; }
    uint32_t TestChannels() const { return channels_; }
    uint32_t TestRate() const { return rate_; }
    /// Période de sondage (ms) utilisée au prochain LockForProcess — les
    /// tests la réduisent pour attendre les transitions sans dormir 100 ms.
    void TestSetPollPeriod(int pollMs) { testPollMs_ = pollMs; }
    /// Changements de fichier vus par le surveillant (attente déterministe).
    uint64_t TestFileChangesSeen() const
    {
        return watcher_.changesSeen_.load(std::memory_order_acquire);
    }
    /// Attend qu'au moins n changements de fichier aient été vus (timeout ms).
    bool TestWaitFileChanges(uint64_t n, int timeoutMs) const;

private:
    /// Prend en compte une éventuelle nouvelle chaîne publiée (atomiques).
    void AdoptPublishedChainLocked();
    void ReleaseChains();

    std::atomic<long> refs_{1};

    ConfigWatcher watcher_;
    bool locked_ = false;          // entre LockForProcess et UnlockForProcess
    uint32_t rate_ = 0;            // taux réel du flux (0 = inconnu)
    uint32_t channels_ = 0;        // canaux du flux (0 = inconnu)
    bool floatFormat_ = false;     // format float 32 bits confirmé
    std::atomic<bool> resetRequested_{false};

    // Chaînes détenues par le thread audio (acquises via watcher_.Acquire()).
    Chain* current_ = nullptr;     // chaîne active (null = pass-through)
    Chain* retiring_ = nullptr;    // chaîne en fondu sortant (null possible)
    long fadeRemaining_ = 0;       // trames restantes du fondu (0 = aucun)
    long fadeLength_ = 0;          // durée totale du fondu (trames)
    int testPollMs_ = 0;           // >0 : période de sondage imposée (tests)
};

/// Fabrique de classes COM de l'APO (retournée par DllGetClassObject).
class RoomEqApoClassFactory final : public IClassFactory {
public:
    HRESULT STDMETHODCALLTYPE QueryInterface(const GUID& riid, void** ppv) override;
    ULONG STDMETHODCALLTYPE AddRef() override;
    ULONG STDMETHODCALLTYPE Release() override;
    HRESULT STDMETHODCALLTYPE CreateInstance(IUnknown* pUnkOuter, const GUID& riid,
        void** ppv) override;
    HRESULT STDMETHODCALLTYPE LockServer(BOOL fLock) override;

private:
    std::atomic<long> refs_{1};
};

} // namespace roomeq::apo

// Points d'entrée DLL (portables — dllmain.cpp ne fait que les exporter).
extern "C" {
HRESULT WINAPI RoomeqDllGetClassObject(const GUID& rclsid, const GUID& riid, void** ppv);
HRESULT WINAPI RoomeqDllCanUnloadNow();
}
