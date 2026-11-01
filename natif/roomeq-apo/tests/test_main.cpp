// Tests natifs de l'APO RoomEq (spec I-AUD-09, N-APO-01) — harnais
// roomeq_apo_tests : la classe COM est liée DIRECTEMENT (le chargement COM
// réel est celui d'audiodg.exe, non reproductible sous Linux) et toutes les
// invocations passent par les pointeurs d'interface obtenus via
// QueryInterface — exactement ce que fait le moteur audio Windows (les
// thunks d'héritage multiple COM sont ainsi exercés).
//
// Couverture : ABI (IIDs littérales, fabrique, QI), cycle de vie, parité de
// courbe bit à bit avec le cœur EqChain, mono/multi-canaux (L/R traités,
// autres intacts), drapeaux de tampon, in-place vs out-of-place, continuité
// échantillon par échantillon à la réécriture identique, fondu enchaîné au
// changement de courbe (mélange exact puis parité stricte), bypass mesure,
// config corrompue/supprimée (courbe conservée), RT-safety (0 allocation et
// 0 libération en régime établi — compteurs globaux new/delete), Reset,
// format non float, déverrouillage.
//
// Usage : roomeq_apo_tests   (répertoire de config isolé créé automatiquement)
#include "apo/RoomEqApo.h"
#include "apo/apo_abi.h"

#include "roomeq/dsp/eq_chain.h"
#include "roomeq/preset/preset_io.h"

#include <atomic>
#include <cmath>
#include <cstdio>
#include <cstdlib>
#include <cstring>
#include <fstream>
#include <memory>
#include <new>
#include <string>
#include <thread>
#include <vector>

#if !defined(_WIN32)
#include <sys/stat.h>
#include <sys/types.h>
#include <unistd.h>
#endif

using namespace roomeq;
using roomeq::apo::RoomEqApo;

// ---------------------------------------------------------------- vérifs

namespace {
int g_failures = 0;
int g_checks = 0;

#define CHECK(cond)                                                                   \
    do {                                                                              \
        ++g_checks;                                                                   \
        if (!(cond)) {                                                                \
            ++g_failures;                                                             \
            std::printf("ECHEC %s:%d : %s\n", __FILE__, __LINE__, #cond);             \
        }                                                                             \
    } while (0)

#define CHECK_NEAR(a, b, tol)                                                         \
    do {                                                                              \
        ++g_checks;                                                                   \
        const double _d = std::fabs(static_cast<double>(a) - static_cast<double>(b)); \
        if (!(_d <= (tol))) {                                                         \
            ++g_failures;                                                             \
            std::printf("ECHEC %s:%d : |%.9g - %.9g| = %.3g > %.3g (%s)\n",           \
                __FILE__, __LINE__, static_cast<double>(a), static_cast<double>(b),   \
                _d, static_cast<double>(tol), #a " ~= " #b);                          \
        }                                                                             \
    } while (0)

// --------------------------------------------------- compteurs d'allocations
// (RT-safety : APOProcess ne doit NI allouer NI libérer en régime établi)

std::atomic<long long> g_news{0};
std::atomic<long long> g_frees{0};

} // namespace

// ---------------------------------------------------------- allocation COM
// (le harnais Linux remplace ole32 ; la DLL Windows redirige vers ole32 —
// définitions des déclarations extern "C" de apo_abi.h)

extern "C" void* WINAPI RoomeqCoTaskMemAlloc(size_t cb) { return std::malloc(cb); }
extern "C" void WINAPI RoomeqCoTaskMemFree(void* pv) { std::free(pv); }

void* operator new(std::size_t s)
{
    g_news.fetch_add(1, std::memory_order_relaxed);
    if (void* p = std::malloc(s))
        return p;
    throw std::bad_alloc();
}
void* operator new[](std::size_t s)
{
    g_news.fetch_add(1, std::memory_order_relaxed);
    if (void* p = std::malloc(s))
        return p;
    throw std::bad_alloc();
}
void operator delete(void* p) noexcept
{
    g_frees.fetch_add(1, std::memory_order_relaxed);
    std::free(p);
}
void operator delete(void* p, std::size_t) noexcept
{
    g_frees.fetch_add(1, std::memory_order_relaxed);
    std::free(p);
}
void operator delete[](void* p) noexcept
{
    g_frees.fetch_add(1, std::memory_order_relaxed);
    std::free(p);
}
void operator delete[](void* p, std::size_t) noexcept
{
    g_frees.fetch_add(1, std::memory_order_relaxed);
    std::free(p);
}
void* operator new(std::size_t s, std::align_val_t a)
{
    g_news.fetch_add(1, std::memory_order_relaxed);
    const std::size_t al = static_cast<std::size_t>(a);
    if (al <= alignof(std::max_align_t)) {
        if (void* p = std::malloc(s))
            return p;
    } else if (void* p = std::aligned_alloc(al, (s + al - 1) / al * al)) {
        return p;
    }
    throw std::bad_alloc();
}
void operator delete(void* p, std::align_val_t) noexcept
{
    g_frees.fetch_add(1, std::memory_order_relaxed);
    std::free(p);
}
void operator delete(void* p, std::size_t, std::align_val_t) noexcept
{
    g_frees.fetch_add(1, std::memory_order_relaxed);
    std::free(p);
}

namespace {

// ---------------------------------------------------------------- outils

std::string g_dir; // répertoire de config isolé (ROOMEQ_APO_DIR)

bool WriteText(const std::string& path, const std::string& text)
{
    std::ofstream f(path, std::ios::binary | std::ios::trunc);
    if (!f)
        return false;
    f << text;
    return f.good();
}

const char* kCurveA = R"JSON({
  "Name": "Courbe A",
  "PreampDb": -1.5,
  "Bands": [
    { "Type": "Peaking", "Frequency": 120.0, "GainDb": 5.5, "Q": 1.2, "Enabled": true },
    { "Type": "LowShelf", "Frequency": 80.0, "GainDb": 3.0, "Q": 0.9, "Enabled": true },
    { "Type": "HighPass", "Frequency": 30.0, "GainDb": 0.0, "Q": 0.7, "Enabled": true }
  ],
  "Bypass": false,
  "Revision": 101
}
)JSON";

const char* kCurveB = R"JSON({
  "Name": "Courbe B",
  "PreampDb": 2.0,
  "Bands": [
    { "Type": "Peaking", "Frequency": 900.0, "GainDb": -6.0, "Q": 2.0, "Enabled": true },
    { "Type": "Notch", "Frequency": 7860.0, "GainDb": 0.0, "Q": 8.0, "Enabled": true }
  ],
  "Bypass": false,
  "Revision": 202
}
)JSON";

const char* kCurveABypass = R"JSON({
  "Name": "Courbe A (bypass)",
  "PreampDb": -1.5,
  "Bands": [
    { "Type": "Peaking", "Frequency": 120.0, "GainDb": 5.5, "Q": 1.2, "Enabled": true },
    { "Type": "LowShelf", "Frequency": 80.0, "GainDb": 3.0, "Q": 0.9, "Enabled": true }
  ],
  "Bypass": true,
  "Revision": 103
}
)JSON";

/// Signal déterministe : balayage log 20 Hz → 20 kHz, stéréo asymétrique.
std::vector<float> MakeInput(size_t frames)
{
    std::vector<float> v(frames * 2, 0.0f);
    const double f0 = 20.0, f1 = 20000.0;
    const double log0 = std::log10(f0), log1 = std::log10(f1);
    for (size_t i = 0; i < frames; ++i) {
        const double f = std::pow(10.0, log0 + (log1 - log0) * i / (frames > 1 ? frames - 1 : 1));
        const double phase = 6.283185307179586476925286766559 * f * i / 48000.0;
        v[i * 2] = static_cast<float>(0.5 * std::sin(phase));
        v[i * 2 + 1] = static_cast<float>(0.45 * std::sin(phase * 0.999));
    }
    return v;
}

/// Référence : chaîne EqChain directe, configurée comme l'APO le fait
/// (taux du flux, 2 canaux — ou 1 en mono).
struct ReferenceEq {
    dsp::EqChain eq;
    ReferenceEq(const char* json, double rate = 48000.0, int channels = 2)
    {
        preset::PresetDef p;
        std::string err;
        if (!preset::FromJson(json, p, err)) {
            std::printf("ECHEC reference : %s\n", err.c_str());
            ++g_failures;
        }
        eq.Configure(rate, channels);
        eq.SetPreset(p);
    }
    std::vector<float> Process(const std::vector<float>& in)
    {
        std::vector<float> out = in;
        eq.Process(out.data(), out.size());
        return out;
    }
};

// ------------------------------------------------- IAudioMediaType factice

struct FakeMediaType final : IAudioMediaType {
    UNCOMPRESSEDAUDIOFORMAT fmt{};

    FakeMediaType()
    {
        fmt.guidFormatType = RoomeqGuid(0x00000003, 0x0000, 0x0010, 0x80, 0x00, 0x00, 0xAA,
                                        0x00, 0x38, 0x9B, 0x71); // IEEE float
        fmt.dwSamplesPerFrame = 2;
        fmt.dwBytesPerSampleContainer = 4;
        fmt.dwValidBitsPerSample = 32;
        fmt.fFramesPerSecond = 48000.0f;
        fmt.dwChannelMask = 0x3;
    }

    HRESULT STDMETHODCALLTYPE QueryInterface(const GUID&, void**) override
    {
        return E_NOINTERFACE_;
    }
    ULONG STDMETHODCALLTYPE AddRef() override { return 2; }
    ULONG STDMETHODCALLTYPE Release() override { return 1; }
    HRESULT STDMETHODCALLTYPE IsCompressedFormat(BOOL* b) override
    {
        if (b)
            *b = 0;
        return S_OK_;
    }
    HRESULT STDMETHODCALLTYPE IsEqual(IAudioMediaType*, DWORD* f) override
    {
        if (f)
            *f = 0;
        return S_OK_;
    }
    const void* STDMETHODCALLTYPE GetAudioFormat() override { return nullptr; }
    HRESULT STDMETHODCALLTYPE GetUncompressedAudioFormat(UNCOMPRESSEDAUDIOFORMAT* out) override
    {
        if (out == nullptr)
            return E_POINTER_;
        *out = fmt;
        return S_OK_;
    }
};

// ------------------------------------------------- connexions factices
// Tampons prédimensionnés à Configure : les blocs successifs ne réallouent
// JAMAIS (prérequis du test RT-safety, qui compte les new/delete globaux).

struct Connections {
    FakeMediaType mt;
    APO_CONNECTION_DESCRIPTOR inDesc{};
    APO_CONNECTION_DESCRIPTOR outDesc{};
    APO_CONNECTION_DESCRIPTOR* inDescs[1]{};
    APO_CONNECTION_DESCRIPTOR* outDescs[1]{};
    APO_CONNECTION_PROPERTY inProp{};
    APO_CONNECTION_PROPERTY outProp{};
    APO_CONNECTION_PROPERTY* inProps[1]{};
    APO_CONNECTION_PROPERTY* outProps[1]{};
    std::vector<float> in;
    std::vector<float> out;

    void Configure(uint32_t rate, uint32_t channels, uint32_t maxFrames)
    {
        mt.fmt.fFramesPerSecond = static_cast<float>(rate);
        mt.fmt.dwSamplesPerFrame = channels;
        inDesc.Type = APO_CONNECTION_BUFFER_TYPE_EXTERNAL;
        inDesc.u32MaxFrameCount = maxFrames;
        inDesc.pFormat = &mt;
        outDesc = inDesc;
        inDescs[0] = &inDesc;
        outDescs[0] = &outDesc;
        const size_t samples = static_cast<size_t>(maxFrames) * channels;
        in.assign(samples, 0.0f);  // prédimensionnement définitif
        out.assign(samples, 0.0f);
    }

    /// Prépare un bloc (copie sans réallocation) et appelle APOProcess.
    void Process(IAudioProcessingObjectRT* rt, uint32_t frames,
                 const std::vector<float>& data, bool inPlace = false)
    {
        const size_t samples = static_cast<size_t>(frames) * mt.fmt.dwSamplesPerFrame;
        std::memcpy(in.data(), data.data(), samples * sizeof(float));
        if (inPlace) {
            // tampon unique simulé : entrée = sortie
            outProp.pBuffer = reinterpret_cast<UINT_PTR>(in.data());
        } else {
            for (size_t i = 0; i < samples; ++i)
                out[i] = -999.0f;
            outProp.pBuffer = reinterpret_cast<UINT_PTR>(out.data());
        }
        inProp.pBuffer = reinterpret_cast<UINT_PTR>(in.data());
        inProp.u32ValidFrameCount = frames;
        outProp.u32ValidFrameCount = 0;
        inProp.u32BufferFlag = BUFFER_VALID;
        outProp.u32BufferFlag = BUFFER_INVALID;
        inProps[0] = &inProp;
        outProps[0] = &outProp;
        rt->APOProcess(1, inProps, 1, outProps);
        if (inPlace)
            std::memcpy(out.data(), in.data(), samples * sizeof(float));
        // NB : pas de resize/shrink — les tampons restent prédimensionnés
        // (zéro réallocation, prérequis du test RT-safety) ; les comparaisons
        // lisent les `samples` premiers éléments.
    }

    void SetSilent()
    {
        inProp.u32BufferFlag = BUFFER_SILENT;
        outProp.u32BufferFlag = BUFFER_INVALID;
        inProps[0] = &inProp;
        outProps[0] = &outProp;
    }
    void SetInvalid()
    {
        inProp.u32BufferFlag = BUFFER_INVALID;
        outProp.u32BufferFlag = BUFFER_INVALID;
        inProps[0] = &inProp;
        outProps[0] = &outProp;
    }
};

double MaxAbsDiff(const std::vector<float>& a, const std::vector<float>& b)
{
    const size_t n = a.size() < b.size() ? a.size() : b.size();
    double worst = 0.0;
    for (size_t i = 0; i < n; ++i) {
        const double d = std::fabs(static_cast<double>(a[i]) - static_cast<double>(b[i]));
        if (d > worst)
            worst = d;
    }
    return worst;
}

} // namespace

// ---------------------------------------------------------------- main

int main()
{
    // répertoire de config isolé : ROOMEQ_APO_DIR doit être posé AVANT la
    // première utilisation de ConfigDir() (cache statique)
    g_dir = "/tmp/roomeq-apo-tests";
    (void)system(("rm -rf " + g_dir + " && mkdir -p " + g_dir).c_str());
#if defined(_WIN32)
    _putenv(("ROOMEQ_APO_DIR=" + g_dir).c_str());
#else
    setenv("ROOMEQ_APO_DIR", g_dir.c_str(), 1);
#endif
    std::printf("- harnais roomeq_apo_tests (config : %s)\n", g_dir.c_str());

    // ================================================== 1. ABI littérale
    {
        CHECK(IID_IAudioProcessingObject == RoomeqGuid(0xFD7F2B29, 0x24D0, 0x4b5c, 0xB1, 0x77,
                                                       0x59, 0x2C, 0x39, 0xF9, 0xCA, 0x10));
        CHECK(IID_IAudioProcessingObjectRT == RoomeqGuid(0x9E1D6A6D, 0xDDBC, 0x4E95, 0xA4, 0xC7,
                                                          0xAD, 0x64, 0xBA, 0x37, 0x84, 0x6C));
        CHECK(IID_IAudioProcessingObjectConfiguration
              == RoomeqGuid(0x0E5ED805, 0xABA6, 0x49c3, 0x8F, 0x9A, 0x2B, 0x8C, 0x88, 0x9C,
                            0x4F, 0xA8));
        CHECK(IID_IAudioSystemEffects == RoomeqGuid(0x5FA00F27, 0xADD6, 0x499a, 0x8A, 0x9D,
                                                    0x6B, 0x98, 0x52, 0x1F, 0xA7, 0x5B));
        CHECK(IID_IUnknown_ == RoomeqGuid(0x00000000, 0x0000, 0x0000, 0xC0, 0x00, 0x00, 0x00,
                                          0x00, 0x00, 0x00, 0x46));
        CHECK(BUFFER_INVALID == 0 && BUFFER_VALID == 1 && BUFFER_SILENT == 2);
        CHECK(sizeof(APO_CONNECTION_PROPERTY) >= 3 * sizeof(UINT32) + sizeof(UINT_PTR));
    }

    // ================================================== 2. fabrique COM
    IClassFactory* factory = nullptr;
    {
        GUID wrong = CLSID_RoomEqApo;
        wrong.Data4[7] ^= 0xFF;
        void* pv = nullptr;
        CHECK(RoomeqDllGetClassObject(wrong, IID_IClassFactory_, &pv)
              == static_cast<HRESULT>(0x80040154L)); // REGDB_E_CLASSNOTREG
        CHECK(RoomeqDllGetClassObject(CLSID_RoomEqApo, IID_IClassFactory_,
                                      reinterpret_cast<void**>(&factory)) == S_OK_);
        CHECK(factory != nullptr);
        CHECK(RoomeqDllCanUnloadNow() == S_FALSE_);
    }

    // ================================================== 3. instance + QI (thunks)
    RoomEqApo* apo = nullptr;               // portée main (type dynamique connu)
    IAudioProcessingObjectRT* rt = nullptr;  // pointeur d'interface, comme audiodg
    {
        void* pv = nullptr;
        CHECK(factory->CreateInstance(nullptr, IID_IAudioProcessingObjectRT, &pv) == S_OK_);
        rt = static_cast<IAudioProcessingObjectRT*>(pv);
        CHECK(rt != nullptr);
        apo = static_cast<RoomEqApo*>(rt); // type dynamique connu du harnais

        IAudioProcessingObject* obj = nullptr;
        IAudioProcessingObjectConfiguration* cfg = nullptr;
        IAudioSystemEffects* fx = nullptr;
        IUnknown* unk = nullptr;
        CHECK(rt->QueryInterface(IID_IAudioProcessingObject,
                                 reinterpret_cast<void**>(&obj)) == S_OK_);
        CHECK(rt->QueryInterface(IID_IAudioProcessingObjectConfiguration,
                                 reinterpret_cast<void**>(&cfg)) == S_OK_);
        CHECK(rt->QueryInterface(IID_IAudioSystemEffects,
                                 reinterpret_cast<void**>(&fx)) == S_OK_);
        CHECK(rt->QueryInterface(IID_IUnknown_, reinterpret_cast<void**>(&unk)) == S_OK_);

        // QI depuis CHACUNE des bases (thunks d'héritage multiple COM)
        IAudioProcessingObjectRT* rt2 = nullptr;
        CHECK(obj->QueryInterface(IID_IAudioProcessingObjectRT,
                                  reinterpret_cast<void**>(&rt2)) == S_OK_);
        CHECK(cfg->QueryInterface(IID_IAudioProcessingObject,
                                  reinterpret_cast<void**>(&pv)) == S_OK_);
        static_cast<IUnknown*>(pv)->Release();
        CHECK(fx->QueryInterface(IID_IAudioSystemEffects,
                                 reinterpret_cast<void**>(&pv)) == S_OK_);
        static_cast<IUnknown*>(pv)->Release();
        CHECK(unk->QueryInterface(IID_IAudioProcessingObjectConfiguration,
                                  reinterpret_cast<void**>(&pv)) == S_OK_);
        static_cast<IUnknown*>(pv)->Release();
        rt2->Release();
        unk->Release();
        fx->Release();
        cfg->Release();
        obj->Release();

        // interface inconnue
        GUID nope = IID_IAudioSystemEffects;
        nope.Data1 ^= 1;
        void* bad = reinterpret_cast<void*>(0x1);
        CHECK(rt->QueryInterface(nope, &bad) == E_NOINTERFACE_);
        CHECK(bad == nullptr);

        // agrégation refusée
        CHECK(factory->CreateInstance(reinterpret_cast<IUnknown*>(0x1234),
                                      IID_IUnknown_, &pv)
              == static_cast<HRESULT>(0x80040110L)); // CLASS_E_NOAGGREGATION
    }

    // ================================================== 4. méthodes de contrôle
    Connections con;
    con.Configure(48000, 2, 4096);
    {
        IAudioProcessingObject* obj = nullptr;
        apo->QueryInterface(IID_IAudioProcessingObject, reinterpret_cast<void**>(&obj));

        CHECK(obj->Initialize(0, nullptr) == S_OK_); // jamais d'échec (budget moteur)
        BYTE blob[64] = {};
        CHECK(obj->Initialize(sizeof blob, blob) == S_OK_);

        HNSTIME latency = -1;
        CHECK(obj->GetLatency(&latency) == S_OK_);
        CHECK(latency == 0); // l'APO n'ajoute AUCUNE latence (synchro A/V)

        CHECK(rt->CalcInputFrames(0) == 0);
        CHECK(rt->CalcInputFrames(512) == 512);
        CHECK(rt->CalcOutputFrames(777) == 777);

        UINT32 ch = 0;
        CHECK(obj->GetInputChannelCount(&ch) == S_OK_);
        CHECK(ch == 2); // avant verrouillage : valeur sûre

        IAudioMediaType* supp = reinterpret_cast<IAudioMediaType*>(0x1);
        CHECK(obj->IsInputFormatSupported(nullptr, nullptr, &supp) == S_OK_);
        CHECK(supp == nullptr);
        CHECK(obj->IsOutputFormatSupported(&con.mt, &con.mt, &supp) == S_OK_);
        CHECK(supp == nullptr);

        APO_REG_PROPERTIES* props = nullptr;
        CHECK(obj->GetRegistrationProperties(&props) == S_OK_);
        if (props != nullptr) {
            CHECK(props->clsid == CLSID_RoomEqApo);
            CHECK(props->Flags == static_cast<APO_FLAG>(APO_FLAG_DEFAULT | APO_FLAG_INPLACE));
            CHECK(props->u32MinInputConnections == 1 && props->u32MaxInputConnections == 1);
            CHECK(props->u32MinOutputConnections == 1 && props->u32MaxOutputConnections == 1);
            CHECK(props->u32NumAPOInterfaces == 1);
            CHECK(props->iidAPOInterfaceList[0] == IID_IAudioProcessingObjectRT);
            CHECK(props->szFriendlyName[0] == L'R');
            RoomeqCoTaskMemFree(props);
        } else {
            CHECK(props != nullptr);
        }

        CHECK(obj->Reset() == S_OK_); // différé, sans effet immédiat
        obj->Release();
    }

    // ================================================== 5. verrouillage
    IAudioProcessingObjectConfiguration* cfg = nullptr;
    {
        apo->QueryInterface(IID_IAudioProcessingObjectConfiguration,
                            reinterpret_cast<void**>(&cfg));

        // descripteurs sans format : toléré (pass-through), jamais d'échec
        APO_CONNECTION_DESCRIPTOR nf{};
        nf.pFormat = nullptr;
        APO_CONNECTION_DESCRIPTOR* nfArr[1] = {&nf};
        CHECK(cfg->LockForProcess(1, nfArr, 1, nfArr) == S_OK_);
        CHECK(apo->TestIsPassthrough()); // format illisible → hors traitement
        CHECK(cfg->UnlockForProcess() == S_OK_);

        CHECK(cfg->LockForProcess(1, con.inDescs, 1, con.outDescs) == S_OK_);
        CHECK(apo->TestRate() == 48000);
        CHECK(apo->TestChannels() == 2);
        CHECK(!apo->TestIsPassthrough());
    }

    // ================================================== 6. pass-through sans courbe
    {
        const std::vector<float> sig = MakeInput(2000);
        con.Process(rt, 2000, sig);
        CHECK(con.outProp.u32BufferFlag == BUFFER_VALID);
        CHECK(con.outProp.u32ValidFrameCount == 2000);
        CHECK(MaxAbsDiff(con.out, sig) == 0.0); // bit à bit

        // BUFFER_SILENT : sortie silencieuse, drapeau préservé
        con.Process(rt, 64, std::vector<float>(128, 0.25f));
        con.SetSilent();
        rt->APOProcess(1, con.inProps, 1, con.outProps);
        bool allZero = true;
        for (size_t i = 0; i < 128; ++i) // seules les 128 valeurs du bloc comptent
            if (con.out[i] != 0.0f)
                allZero = false;
        CHECK(allZero);
        CHECK(con.outProp.u32BufferFlag == BUFFER_SILENT);

        // BUFFER_INVALID : l'APO ne touche à rien (comportement Equalizer APO)
        con.Process(rt, 64, std::vector<float>(128, 0.25f));
        con.SetInvalid();
        for (size_t i = 0; i < con.out.size(); ++i)
            con.out[i] = -7.0f;
        rt->APOProcess(1, con.inProps, 1, con.outProps);
        CHECK(con.out[0] == -7.0f && con.out[127] == -7.0f);
    }

    // miroir d'ère : référence EqChain nourrie des MÊMES blocs que l'APO
    // (les états de filtres avancent à l'identique des deux côtés).
    std::unique_ptr<ReferenceEq> mirror;

    // ================================================== 7. parité de courbe (bit à bit)
    {
        CHECK(WriteText(g_dir + "/active.json", kCurveA));
        CHECK(apo->TestWaitFileChanges(1, 2000));
        mirror = std::make_unique<ReferenceEq>(kCurveA);

        // bloc de consommation du fondu d'adoption (fadeLength = 48k x 20 ms = 960)
        const std::vector<float> burn = MakeInput(1000);
        con.Process(rt, 1000, burn);
        (void)mirror->Process(burn);

        const std::vector<float> sig = MakeInput(4096);
        con.Process(rt, 4096, sig);
        CHECK(MaxAbsDiff(con.out, mirror->Process(sig)) == 0.0); // parité 0.00e+00
        CHECK(apo->TestRevision() == 101);

        // in-place (tampon unique) : résultat identique, miroir continue
        con.Process(rt, 4096, sig, true);
        CHECK(MaxAbsDiff(con.out, mirror->Process(sig)) == 0.0);
    }

    // ================================================== 8. RT-safety (0 alloc / 0 free)
    {
        const std::vector<float> sig = MakeInput(2048); // AVANT la fenêtre de mesure
        std::fflush(stdout); // le tampon stdout ne doit plus allouer ici
        const long long news0 = g_news.load();
        const long long frees0 = g_frees.load();
        for (int i = 0; i < 8; ++i)
            con.Process(rt, 2048, sig);
        std::fflush(stdout);
        CHECK(g_news.load() - news0 == 0);   // aucune allocation en régime établi
        CHECK(g_frees.load() - frees0 == 0); // aucune libération non plus
        for (int i = 0; i < 8; ++i) // le miroir suit, HORS fenêtre de mesure
            (void)mirror->Process(sig);
    }

    // ================================================== 9. réécriture identique : continuité
    {
        const std::vector<float> sig1 = MakeInput(1500);
        const std::vector<float> sig2 = MakeInput(1500);
        con.Process(rt, 1500, sig1);
        CHECK(MaxAbsDiff(con.out, mirror->Process(sig1)) == 0.0);

        // réécriture du MÊME contenu (seule la révision change) : aucun swap,
        // états de filtres conservés — continuité échantillon par échantillon
        // (invariant 5 de la spec I-AUD-09)
        std::string same(kCurveA);
        const size_t pos = same.find("\"Revision\": 101");
        CHECK(pos != std::string::npos);
        same.replace(pos, 15, "\"Revision\": 999");
        CHECK(WriteText(g_dir + "/active.json", same));
        CHECK(apo->TestWaitFileChanges(2, 2000));

        con.Process(rt, 1500, sig2);
        CHECK(apo->TestRevision() == 101); // toujours la chaîne d'origine
        CHECK(MaxAbsDiff(con.out, mirror->Process(sig2)) == 0.0); // couture bit à bit
    }

    // ================================================== 10. changement de courbe : fondu enchaîné
    {
        const std::vector<float> sig1 = MakeInput(2000);
        con.Process(rt, 2000, sig1);
        CHECK(MaxAbsDiff(con.out, mirror->Process(sig1)) == 0.0);

        // publication de la courbe B (contenu différent)
        CHECK(WriteText(g_dir + "/active.json", kCurveB));
        CHECK(apo->TestWaitFileChanges(3, 2000));

        // B démarre d'un état NEUF à l'adoption ; A continue de son état
        const std::vector<float> sig2 = MakeInput(2000);
        const std::vector<float> sig3 = MakeInput(2000);
        std::vector<float> refAcont = mirror->Process(sig2); // A continue
        ReferenceEq refB(kCurveB);                            // B démarre neuf
        std::vector<float> refBcont = refB.Process(sig2);

        con.Process(rt, 2000, sig2); // adoption au début du bloc + fondu 960
        CHECK(apo->TestRevision() == 202);

        // pendant le fondu : mélange w*A + (1-w)*B (poids linéaires ; la
        // conversion float du mélange autorise un demi-ULP)
        {
            const long fade = 960;
            double worst = 0.0;
            for (long f = 0; f < fade; ++f) {
                const double w = static_cast<double>(fade - f)
                    / static_cast<double>(fade + 1);
                const double e0 = w * refAcont[f * 2] + (1.0 - w) * refBcont[f * 2];
                const double e1 = w * refAcont[f * 2 + 1] + (1.0 - w) * refBcont[f * 2 + 1];
                worst = std::max(worst,
                                 std::fabs(static_cast<double>(con.out[f * 2]) - e0));
                worst = std::max(worst,
                                 std::fabs(static_cast<double>(con.out[f * 2 + 1]) - e1));
            }
            CHECK_NEAR(worst, 0.0, 1e-7);
            // après le fondu (dans CE bloc) : B pur, bit à bit
            CHECK(con.out[1000 * 2] == refBcont[1000 * 2]);
            CHECK(con.out[1999 * 2 + 1] == refBcont[1999 * 2 + 1]);
        }

        // bloc suivant : parité stricte avec B qui a poursuivi son état
        mirror = std::make_unique<ReferenceEq>(kCurveB);
        (void)mirror->Process(sig2); // B a traité sig2 à l'adoption
        con.Process(rt, 2000, sig3);
        CHECK(MaxAbsDiff(con.out, mirror->Process(sig3)) == 0.0);
    }

    // ================================================== 11. bypass mesure
    {
        CHECK(WriteText(g_dir + "/active.json", kCurveABypass));
        CHECK(apo->TestWaitFileChanges(4, 2000));
        mirror.reset(); // l'ère bypass se compare à l'entrée brute

        const std::vector<float> sig = MakeInput(1000);
        con.Process(rt, 1000, sig); // fondu court (960)
        const std::vector<float> sig2 = MakeInput(1000);
        con.Process(rt, 1000, sig2);
        CHECK(MaxAbsDiff(con.out, sig2) == 0.0); // bypass = transparent, bit à bit
    }

    // ================================================== 12. JSON corrompu / supprimé
    {
        // retour à une courbe saine (contenu différent du bypass → nouvelle ère)
        CHECK(WriteText(g_dir + "/active.json", kCurveA));
        CHECK(apo->TestWaitFileChanges(5, 2000));
        mirror = std::make_unique<ReferenceEq>(kCurveA);

        const std::vector<float> burn = MakeInput(1000);
        con.Process(rt, 1000, burn);
        (void)mirror->Process(burn);

        const std::vector<float> sig = MakeInput(3000);
        con.Process(rt, 3000, sig);
        CHECK(MaxAbsDiff(con.out, mirror->Process(sig)) == 0.0);

        // JSON corrompu : la courbe courante est conservée (son jamais coupé)
        CHECK(WriteText(g_dir + "/active.json", "{ pas du tout du JSON"));
        CHECK(apo->TestWaitFileChanges(6, 2000));
        const std::vector<float> sig2 = MakeInput(1000);
        con.Process(rt, 1000, sig2);
        CHECK(MaxAbsDiff(con.out, mirror->Process(sig2)) == 0.0); // inchangé, bit à bit

        // fichier supprimé : la dernière courbe valide reste active
        std::remove((g_dir + "/active.json").c_str());
        CHECK(apo->TestWaitFileChanges(7, 2000));
        const std::vector<float> sig3 = MakeInput(1000);
        con.Process(rt, 1000, sig3);
        CHECK(MaxAbsDiff(con.out, mirror->Process(sig3)) == 0.0);
    }

    // ================================================== 13. Reset
    {
        // réécriture identique : pas de swap, la chaîne garde son historique
        CHECK(WriteText(g_dir + "/active.json", kCurveA));
        CHECK(apo->TestWaitFileChanges(8, 2000));
        const std::vector<float> sigX = MakeInput(2000);
        con.Process(rt, 2000, sigX); // états avancés
        (void)mirror->Process(sigX);

        IAudioProcessingObject* obj = nullptr;
        apo->QueryInterface(IID_IAudioProcessingObject, reinterpret_cast<void**>(&obj));
        CHECK(obj->Reset() == S_OK_);
        obj->Release();

        // la référence redémarre d'un état NEUF : les sorties coïncident
        mirror = std::make_unique<ReferenceEq>(kCurveA);
        const std::vector<float> sig = MakeInput(2000);
        con.Process(rt, 2000, sig);
        CHECK(MaxAbsDiff(con.out, mirror->Process(sig)) == 0.0);
    }

    // ================================================== 14. multi-canaux et mono
    {
        CHECK(cfg->UnlockForProcess() == S_OK_);

        IAudioProcessingObject* obj = nullptr;
        apo->QueryInterface(IID_IAudioProcessingObject, reinterpret_cast<void**>(&obj));

        // 6 canaux : L/R égalisés, les 4 autres intacts. Le format EQ est
        // inchangé (48 k, 2 voies) : la chaîne garde son historique — Reset
        // aligne les états sur une référence neuve.
        Connections con6;
        con6.Configure(48000, 6, 2048);
        CHECK(cfg->LockForProcess(1, con6.inDescs, 1, con6.outDescs) == S_OK_);
        CHECK(apo->TestChannels() == 6);
        CHECK(obj->Reset() == S_OK_);

        const size_t frames = 1500;
        std::vector<float> in6(frames * 6, 0.0f);
        std::vector<float> stereo(frames * 2, 0.0f);
        for (size_t i = 0; i < frames; ++i) {
            const double ph = 6.283185307179586 * 440.0 * i / 48000.0;
            in6[i * 6 + 0] = static_cast<float>(0.4 * std::sin(ph));
            in6[i * 6 + 1] = static_cast<float>(0.3 * std::cos(ph));
            in6[i * 6 + 2] = 0.11f;  // centre
            in6[i * 6 + 3] = -0.22f; // LFE
            in6[i * 6 + 4] = 0.33f;  // surround L
            in6[i * 6 + 5] = -0.44f; // surround R
            stereo[i * 2] = in6[i * 6 + 0];
            stereo[i * 2 + 1] = in6[i * 6 + 1];
        }

        ReferenceEq ref6(kCurveA); // stéréo : comparaison des voies L/R
        con6.Process(rt, static_cast<uint32_t>(frames), in6); // bloc de consommation
        (void)ref6.Process(stereo);
        con6.Process(rt, static_cast<uint32_t>(frames), in6); // bloc comparé
        std::vector<float> expect = ref6.Process(stereo);

        double lr = 0.0;
        for (size_t i = 0; i < frames; ++i) {
            lr = std::max(lr, std::fabs(static_cast<double>(con6.out[i * 6 + 0])
                - static_cast<double>(expect[i * 2])));
            lr = std::max(lr, std::fabs(static_cast<double>(con6.out[i * 6 + 1])
                - static_cast<double>(expect[i * 2 + 1])));
        }
        CHECK(lr == 0.0); // L/R : parité bit à bit
        bool intact = true;
        for (size_t i = 0; i < frames; ++i) {
            if (con6.out[i * 6 + 2] != 0.11f || con6.out[i * 6 + 3] != -0.22f
                || con6.out[i * 6 + 4] != 0.33f || con6.out[i * 6 + 5] != -0.44f)
                intact = false;
        }
        CHECK(intact); // canaux 2..5 intacts, bit à bit

        // mono : NOUVEAU format (44,1 k, 1 voie) — la chaîne doit être
        // reconstruite (sinon une chaîne stéréo n'y traiterait rien)
        CHECK(cfg->UnlockForProcess() == S_OK_);
        Connections con1;
        con1.Configure(44100, 1, 2048);
        CHECK(cfg->LockForProcess(1, con1.inDescs, 1, con1.outDescs) == S_OK_);
        CHECK(apo->TestChannels() == 1);
        CHECK(apo->TestRate() == 44100);

        const size_t mframes = 1000;
        std::vector<float> in1(mframes, 0.0f);
        for (size_t i = 0; i < mframes; ++i)
            in1[i] = static_cast<float>(0.5 * std::sin(6.283185307179586 * 250.0 * i / 44100.0));

        ReferenceEq refMono(kCurveA, 44100.0, 1);
        con1.Process(rt, static_cast<uint32_t>(mframes), in1); // bloc de consommation
        (void)refMono.Process(in1);
        con1.Process(rt, static_cast<uint32_t>(mframes), in1); // bloc comparé
        std::vector<float> expectMono = refMono.Process(in1);
        CHECK(MaxAbsDiff(con1.out, expectMono) == 0.0);

        CHECK(cfg->UnlockForProcess() == S_OK_);
        obj->Release();
    }

    // ================================================== 15. format non float
    {
        Connections conPcm;
        conPcm.mt.fmt.dwBytesPerSampleContainer = 2; // PCM 16 bits simulé
        conPcm.Configure(48000, 2, 1024);
        CHECK(cfg->LockForProcess(1, conPcm.inDescs, 1, conPcm.outDescs) == S_OK_);
        const std::vector<float> sig = MakeInput(500);
        conPcm.Process(rt, 500, sig);
        conPcm.Process(rt, 500, sig);
        CHECK(MaxAbsDiff(conPcm.out, sig) == 0.0); // prudent : pass-through
        CHECK(cfg->UnlockForProcess() == S_OK_);
    }

    // ================================================== 16. après Unlock : sans crash
    {
        con.Process(rt, 64, std::vector<float>(128, 0.1f)); // déverrouillé : no-op
        CHECK(con.outProp.u32BufferFlag == BUFFER_INVALID); // tampon intact
        CHECK(con.out[0] == -999.0f);                       // rien n'a été écrit
    }

    // ================================================== ménage
    rt->Release();
    cfg->Release();
    factory->Release();

    std::printf("- %d vérifications, %d échec(s)\n", g_checks, g_failures);
    std::printf(g_failures == 0 ? "ROOMEQ_APO_TESTS: OK\n" : "ROOMEQ_APO_TESTS: ECHEC\n");
    return g_failures == 0 ? 0 : 1;
}
