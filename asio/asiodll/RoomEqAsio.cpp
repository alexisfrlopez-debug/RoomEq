/* -*- coding: utf-8 -*-
   RoomEq ASIO bridge driver — implementation. See RoomEqAsio.h.
*/
#include "RoomEqAsio.h"

#include <mmsystem.h>
#include <malloc.h>
#include <stdio.h>
#include <string.h>

/* RoomEq v1 : 48 kHz uniquement — le pont et l'application partagent ce taux
   fixe, l'hôte rééchantillonne côté mixeur. Le DAW resample son projet. */
static const ASIOSampleRate kSupportedRates[] = { 48000.0 };
static const int32_t kMinBuffer = 128;
static const int32_t kMaxBuffer = 2048;
static const int32_t kPrefBuffer = 256;
static const int32_t kGranularity = 128;
static const int32_t kInputChannels = 2;   /* silence (v1) */
static const int32_t kOutputChannels = 2;  /* DAW playback -> RoomEq */

RoomEqAsioDriver::RoomEqAsioDriver()
    : m_refs(1)
    , m_map(NULL), m_dataEvent(NULL), m_hdr(NULL), m_ring(NULL)
    , m_rate(48000.0), m_bufferSize(kPrefBuffer)
    , m_initialized(false), m_running(false), m_buffersCreated(false)
    , m_infos(NULL), m_infoCount(0)
    , m_callbacksSet(false), m_useTimeInfo(false)
    , m_thread(NULL), m_stopEvent(NULL)
    , m_qpcFreq(0.0), m_localFrames(0), m_lastHostClock(0)
    , m_scratch(NULL)
{
    memset(&m_callbacks, 0, sizeof(m_callbacks));
    strcpy_s(m_error, "Aucune erreur.");
    LARGE_INTEGER f;
    QueryPerformanceFrequency(&f);
    m_qpcFreq = (double)f.QuadPart;
    roomeq_asio_pll_init(&m_pll);
}

RoomEqAsioDriver::~RoomEqAsioDriver()
{
    if (m_running) ASIOstop();
    if (m_buffersCreated) ASIOdisposeBuffers();
    DetachShared();
}

/* ---- IUnknown ---- */

ULONG STDMETHODCALLTYPE RoomEqAsioDriver::AddRef()
{
    return (ULONG)InterlockedIncrement(&m_refs);
}

ULONG STDMETHODCALLTYPE RoomEqAsioDriver::Release()
{
    ULONG r = (ULONG)InterlockedDecrement(&m_refs);
    if (r == 0) delete this;
    return r;
}

HRESULT STDMETHODCALLTYPE RoomEqAsioDriver::QueryInterface(REFIID riid, void** ppv)
{
    if (ppv == NULL) return E_POINTER;
    if (IsEqualIID(riid, IID_IUnknown) || IsEqualIID(riid, IID_IASIO))
    {
        *ppv = static_cast<IASIO*>(this);
        AddRef();
        return S_OK;
    }
    *ppv = NULL;
    return E_NOINTERFACE;
}

/* ---- transport ---- */

bool RoomEqAsioDriver::AttachShared()
{
    if (m_hdr != NULL) return true;   /* déjà attaché */

    const SIZE_T bytes = (SIZE_T)ROOMEQ_ASIO_HEADER_BYTES
        + (SIZE_T)ROOMEQ_ASIO_RING_FRAMES * ROOMEQ_ASIO_CHANNELS * sizeof(float);

    /* open first (normal case: the RoomEq app hosts), else create so the
       DAW still works when the app is closed (degraded, audio dropped). */
    m_map = OpenFileMappingW(FILE_MAP_ALL_ACCESS, FALSE, L"Local\\RoomEq.Asio.Bridge.v1");
    if (m_map == NULL)
        m_map = CreateFileMappingW(INVALID_HANDLE_VALUE, NULL,
                                   PAGE_READWRITE, 0, (DWORD)bytes,
                                   L"Local\\RoomEq.Asio.Bridge.v1");
    if (m_map == NULL) return false;

    m_hdr = (RoomeqAsioHeader*)MapViewOfFile(m_map, FILE_MAP_ALL_ACCESS, 0, 0, bytes);
    if (m_hdr == NULL)
    {
        CloseHandle(m_map); m_map = NULL;
        return false;
    }

    m_ring = roomeq_asio_ring(m_hdr);

    /* init when freshly created (magic absent) */
    if (m_hdr->magic != ROOMEQ_ASIO_MAGIC)
    {
        memset(m_hdr, 0, bytes);
        m_hdr->magic = ROOMEQ_ASIO_MAGIC;
        m_hdr->version = ROOMEQ_ASIO_VERSION;
        m_hdr->sampleRate = (uint32_t)m_rate;
        m_hdr->bufferFrames = (uint32_t)m_bufferSize;
        m_hdr->ringFrames = ROOMEQ_ASIO_RING_FRAMES;
        m_hdr->channels = ROOMEQ_ASIO_CHANNELS;
        m_hdr->session = 1;
    }
    m_dataEvent = CreateEventW(NULL, FALSE, FALSE, L"Local\\RoomEq.Asio.Data.v1");
    return true;
}

void RoomEqAsioDriver::DetachShared()
{
    if (m_hdr != NULL)
    {
        m_hdr->clientActive = 0;
        UnmapViewOfFile(m_hdr);
        m_hdr = NULL; m_ring = NULL;
    }
    if (m_map != NULL) { CloseHandle(m_map); m_map = NULL; }
    if (m_dataEvent != NULL) { CloseHandle(m_dataEvent); m_dataEvent = NULL; }
}

/* ---- IASIO ---- */

ASIOBool RoomEqAsioDriver::ASIOInit(void* /*sysHandle*/)
{
    if (!AttachShared())
    {
        strcpy_s(m_error, "Memoire partagee RoomEq indisponible.");
        return 0;
    }
    m_hdr->clientActive = 1;
    if (m_hdr->sampleRate == 0) m_hdr->sampleRate = (uint32_t)m_rate;
    m_hdr->bufferFrames = (uint32_t)m_bufferSize;
    m_initialized = true;
    strcpy_s(m_error, "Aucune erreur.");
    return 1;
}

void RoomEqAsioDriver::ASIOgetDriverName(char* name)
{
    strcpy_s(name, 32, "RoomEq ASIO");
}

int32_t RoomEqAsioDriver::ASIOgetDriverVersion()
{
    return 2;   /* ASIO 2 */
}

void RoomEqAsioDriver::ASIOgetErrorMessage(char* string)
{
    strcpy_s(string, 160, m_error);
}

ASIOError RoomEqAsioDriver::ASIOstart()
{
    if (!m_initialized) { strcpy_s(m_error, "ASIOInit manquant."); return ASE_NotPresent; }
    if (m_running) return ASE_OK;

    if (!AttachShared()) { strcpy_s(m_error, "Transport absent."); return ASE_NotPresent; }
    m_hdr->clientActive = 1;

    m_stopEvent = CreateEventW(NULL, TRUE, FALSE, NULL);
    if (m_stopEvent == NULL) return ASE_NoMemory;

    timeBeginPeriod(1);
    m_thread = CreateThread(NULL, 0, PacingThreadProc, this, 0, NULL);
    if (m_thread == NULL)
    {
        timeEndPeriod(1);
        CloseHandle(m_stopEvent); m_stopEvent = NULL;
        return ASE_NoMemory;
    }
    SetThreadPriority(m_thread, THREAD_PRIORITY_TIME_CRITICAL);
    m_running = true;
    return ASE_OK;
}

ASIOError RoomEqAsioDriver::ASIOstop()
{
    if (!m_running) return ASE_OK;
    if (m_stopEvent != NULL) SetEvent(m_stopEvent);
    if (m_thread != NULL)
    {
        WaitForSingleObject(m_thread, 3000);
        CloseHandle(m_thread); m_thread = NULL;
    }
    if (m_stopEvent != NULL) { CloseHandle(m_stopEvent); m_stopEvent = NULL; }
    timeEndPeriod(1);
    m_running = false;
    return ASE_OK;
}

ASIOError RoomEqAsioDriver::ASIOgetChannels(int32_t* numInputChannels, int32_t* numOutputChannels)
{
    if (numInputChannels) *numInputChannels = kInputChannels;
    if (numOutputChannels) *numOutputChannels = kOutputChannels;
    return ASE_OK;
}

ASIOError RoomEqAsioDriver::ASIOgetLatencies(int32_t* inputLatency, int32_t* outputLatency)
{
    int32_t lat = m_bufferSize;
    if (m_hdr != NULL && m_hdr->hostActive)
    {
        int64_t backlog = roomeq_asio_available(m_hdr);
        if (backlog > ROOMEQ_ASIO_RING_FRAMES / 4) backlog = ROOMEQ_ASIO_RING_FRAMES / 4;
        lat = m_bufferSize + (int32_t)backlog;
    }
    if (inputLatency) *inputLatency = m_bufferSize;
    if (outputLatency) *outputLatency = lat;
    return ASE_OK;
}

ASIOError RoomEqAsioDriver::ASIOgetBufferSize(int32_t* minSize, int32_t* maxSize,
                                              int32_t* preferredSize, int32_t* granularity)
{
    if (minSize) *minSize = kMinBuffer;
    if (maxSize) *maxSize = kMaxBuffer;
    if (preferredSize) *preferredSize = kPrefBuffer;
    if (granularity) *granularity = kGranularity;
    return ASE_OK;
}

ASIOError RoomEqAsioDriver::ASIOcanSampleRate(ASIOSampleRate sampleRate)
{
    for (size_t i = 0; i < sizeof(kSupportedRates) / sizeof(kSupportedRates[0]); i++)
        if (kSupportedRates[i] == sampleRate) return ASE_OK;
    return ASE_NoClock;
}

ASIOError RoomEqAsioDriver::ASIOsetSampleRate(ASIOSampleRate sampleRate)
{
    if (ASIOcanSampleRate(sampleRate) != ASE_OK)
    {
        strcpy_s(m_error, "Taux non supporte : 48 kHz uniquement.");
        return ASE_NoClock;
    }
    m_rate = sampleRate;
    if (m_hdr != NULL)
    {
        m_hdr->sampleRate = (uint32_t)sampleRate;
        m_hdr->session = m_hdr->session + 1;   /* le lecteur se réaligne */
    }
    return ASE_OK;
}

ASIOError RoomEqAsioDriver::ASIOgetClockSources(ASIOClockSource* clocks, int32_t* numSources)
{
    if (clocks == NULL || numSources == NULL) return ASE_InvalidParameter;
    if (*numSources < 1) return ASE_InvalidParameter;
    clocks[0].index = 0;
    clocks[0].associatedChannel = -1;
    clocks[0].associatedGroup = -1;
    clocks[0].isCurrent = 1;
    strcpy_s(clocks[0].name, 32, "Horloge interne RoomEq");
    *numSources = 1;
    return ASE_OK;
}

ASIOError RoomEqAsioDriver::ASIOsetClockSource(int32_t reference)
{
    return (reference == 0) ? ASE_OK : ASE_InvalidParameter;
}

ASIOError RoomEqAsioDriver::ASIOgetSamplePosition(ASIOSamples* sPos, ASIOTimeStamp* tStamp)
{
    if (sPos == NULL || tStamp == NULL) return ASE_InvalidParameter;
    int64_t p = m_localFrames;
    sPos->hi = (uint32_t)((uint64_t)p >> 32);
    sPos->lo = (uint32_t)((uint64_t)p & 0xffffffffu);
    LARGE_INTEGER c;
    QueryPerformanceCounter(&c);
    uint64_t t = (uint64_t)(c.QuadPart * 1000000.0 / m_qpcFreq);
    tStamp->hi = (uint32_t)(t >> 32);
    tStamp->lo = (uint32_t)(t & 0xffffffffu);
    return ASE_OK;
}

ASIOError RoomEqAsioDriver::ASIOgetChannelInfo(ASIOChannelInfo* info)
{
    if (info == NULL) return ASE_InvalidParameter;
    bool isIn = info->isInput != 0;
    if (info->channel < 0 || info->channel >= (isIn ? kInputChannels : kOutputChannels))
        return ASE_InvalidParameter;
    info->isActive = (m_running && m_buffersCreated) ? 1 : 0;
    info->channelGroup = 0;
    info->type = ASIOSTFloat32LSB;
    if (isIn)
        sprintf_s(info->name, 32, "Entree %s", info->channel == 0 ? "L" : "R");
    else
        sprintf_s(info->name, 32, "Lecture DAW %s", info->channel == 0 ? "L" : "R");
    return ASE_OK;
}

ASIOError RoomEqAsioDriver::ASIOcreateBuffers(ASIOBufferInfo* bufferInfos, int32_t numChannels,
                                              int32_t bufferSize, void* callbacks)
{
    if (!m_initialized) return ASE_NotPresent;
    if (bufferInfos == NULL || callbacks == NULL) return ASE_InvalidParameter;
    if (bufferSize < kMinBuffer || bufferSize > kMaxBuffer ||
        (bufferSize % kGranularity) != 0)
    {
        strcpy_s(m_error, "Taille de buffer refusee (128..2048, pas 128).");
        return ASE_InvalidParameter;
    }

    /* validate channels */
    for (int32_t i = 0; i < numChannels; i++)
    {
        int32_t max = bufferInfos[i].isInput ? kInputChannels : kOutputChannels;
        if (bufferInfos[i].channelNum < 0 || bufferInfos[i].channelNum >= max)
            return ASE_InvalidParameter;
    }

    ASIOdisposeBuffers();

    m_infos = new ASIOBufferInfo[numChannels];
    memcpy(m_infos, bufferInfos, sizeof(ASIOBufferInfo) * numChannels);
    m_infoCount = numChannels;

    /* mémoire privée par canal : deux sous-tampons float de bufferSize. */
    for (int32_t i = 0; i < numChannels; i++)
    {
        float* block = new float[(SIZE_T)bufferSize * 2];
        m_infos[i].buffers[0] = block;
        m_infos[i].buffers[1] = block + bufferSize;
    }
    m_scratch = new float[(SIZE_T)bufferSize * 2];

    m_callbacks = *(ASIOCallbacks*)callbacks;
    m_callbacksSet = true;
    m_useTimeInfo = (m_callbacks.bufferSwitchTimeInfo != NULL);
    m_bufferSize = bufferSize;
    if (m_hdr != NULL) m_hdr->bufferFrames = (uint32_t)bufferSize;
    m_buffersCreated = true;
    return ASE_OK;
}

ASIOError RoomEqAsioDriver::ASIOdisposeBuffers()
{
    if (!m_buffersCreated) return ASE_OK;
    if (m_running) ASIOstop();
    for (int32_t i = 0; i < m_infoCount; i++)
    {
        float* block = (float*)m_infos[i].buffers[0];
        delete[] block;
        m_infos[i].buffers[0] = m_infos[i].buffers[1] = NULL;
    }
    delete[] m_infos;
    m_infos = NULL; m_infoCount = 0;
    delete[] m_scratch; m_scratch = NULL;
    m_buffersCreated = false;
    m_callbacksSet = false;
    return ASE_OK;
}

ASIOError RoomEqAsioDriver::ASIOcontrolPanel()
{
    MessageBoxW(NULL,
        L"RoomEq ASIO : lancez l'application RoomEq (onglet Mixage, piste « DAW (ASIO) »).\n\n"
        L"Le DAW joue ici ; RoomEq égalise puis sort vers la vraie carte son.",
        L"RoomEq ASIO", MB_OK | MB_ICONINFORMATION);
    return ASE_OK;
}

ASIOError RoomEqAsioDriver::ASIOfuture(int32_t /*selector*/, void* /*opt*/)
{
    return ASE_NotPresent;
}

ASIOError RoomEqAsioDriver::ASIOoutputReady()
{
    return ASE_OK;   /* the pacing thread consumes at its own cadence */
}

int32_t RoomEqAsioDriver::ASIOMessage(int32_t selector, int32_t /*value*/,
                                      void* /*message*/, double* /*opt*/)
{
    switch (selector)
    {
        case kAsioEngineVersion:
            return 2;                       /* ASIO 2 */
        case kAsioSelectorSupported:
        case kAsioSupportsTimeInfo:
        case kAsioResyncRequest:
        case kAsioLatenciesChanged:
            return 1;
        default:
            return 0;
    }
}

/* ---- pacing thread ---- */

DWORD WINAPI RoomEqAsioDriver::PacingThreadProc(LPVOID param)
{
    RoomEqAsioDriver* self = (RoomEqAsioDriver*)param;
    self->RunPacing();
    return 0;
}

void RoomEqAsioDriver::RunPacing()
{
    double intervalQpc = m_qpcFreq * (double)m_bufferSize / m_rate;   /* nominal */
    int32_t idx = 0;
    LARGE_INTEGER next, now;
    QueryPerformanceCounter(&next);
    m_lastHostClock = (m_hdr != NULL) ? m_hdr->hostClock : 0;

    for (;;)
    {
        /* 1) dormir jusqu'à l'échéance (ou arrêt) */
        QueryPerformanceCounter(&now);
        LONGLONG remain = next.QuadPart - now.QuadPart;
        if (remain < 0) remain = 0;
        DWORD ms = (DWORD)(remain * 1000.0 / m_qpcFreq);
        if (ms > 0)
        {
            DWORD w = WaitForSingleObject(m_stopEvent, ms);
            if (w == WAIT_OBJECT_0) return;           /* arrêt demandé */
        }
        /* 2) attente active de précision (dernière fraction de ms) */
        for (;;)
        {
            if (WaitForSingleObject(m_stopEvent, 0) == WAIT_OBJECT_0) return;
            QueryPerformanceCounter(&now);
            if (now.QuadPart >= next.QuadPart) break;
        }

        /* 3) passage de tampon */
        DoBufferPass(idx);
        idx ^= 1;

        /* 4) échéance suivante, corrigée par la PLL (collage horloge hôte) */
        intervalQpc = m_qpcFreq * ((double)m_bufferSize / m_pll.ratio) / m_rate;
        next.QuadPart += (LONGLONG)intervalQpc;
        QueryPerformanceCounter(&now);
        if (next.QuadPart - now.QuadPart > (LONGLONG)m_qpcFreq)
            next.QuadPart = now.QuadPart + (LONGLONG)intervalQpc;   /* recale */
    }
}

void RoomEqAsioDriver::DoBufferPass(int32_t doubleBufferIndex)
{
    if (!m_buffersCreated || !m_callbacksSet) return;

    /* 1) pousser la lecture DAW vers l'anneau (déentrelacé -> entrelacé) */
    if (m_hdr != NULL && m_scratch != NULL)
    {
        float* frame = m_scratch;
        float* l = NULL; float* r = NULL;
        for (int32_t i = 0; i < m_infoCount; i++)
        {
            if (m_infos[i].isInput) continue;
            if (m_infos[i].channelNum == 0) l = (float*)m_infos[i].buffers[doubleBufferIndex];
            else if (m_infos[i].channelNum == 1) r = (float*)m_infos[i].buffers[doubleBufferIndex];
        }
        for (int32_t n = 0; n < m_bufferSize; n++)
        {
            frame[n * 2 + 0] = l ? l[n] : 0.0f;
            frame[n * 2 + 1] = r ? r[n] : 0.0f;
        }
        roomeq_asio_write(m_hdr, m_ring, m_bufferSize, frame);
        if (m_dataEvent != NULL) SetEvent(m_dataEvent);

        /* 2) PLL : combien l'hôte a-t-il consommé depuis le dernier passage ? */
        int64_t hostNow = m_hdr->hostClock;
        int64_t hostDelta = hostNow - m_lastHostClock;
        if (hostDelta > 0)
            roomeq_asio_pll_update(&m_pll, (double)m_bufferSize, (double)hostDelta);
        m_lastHostClock = hostNow;
    }

    /* 3) entrées DAW : silence (v1) */
    for (int32_t i = 0; i < m_infoCount; i++)
    {
        if (!m_infos[i].isInput) continue;
        float* b = (float*)m_infos[i].buffers[doubleBufferIndex];
        memset(b, 0, sizeof(float) * (SIZE_T)m_bufferSize);
    }

    m_localFrames += m_bufferSize;

    /* 4) prévenir le DAW */
    if (m_useTimeInfo && m_callbacks.bufferSwitchTimeInfo != NULL)
    {
        ASIOTime t;
        memset(&t, 0, sizeof(t));
        t.samplePosition.hi = (uint32_t)((uint64_t)m_localFrames >> 32);
        t.samplePosition.lo = (uint32_t)((uint64_t)m_localFrames & 0xffffffffu);
        LARGE_INTEGER c;
        QueryPerformanceCounter(&c);
        uint64_t usec = (uint64_t)(c.QuadPart * 1000000.0 / m_qpcFreq);
        t.timeInfo.hi = (uint32_t)(usec >> 32);
        t.timeInfo.lo = (uint32_t)(usec & 0xffffffffu);
        t.sampleRate = m_rate;
        m_callbacks.bufferSwitchTimeInfo(&t, doubleBufferIndex, 0);
    }
    else if (m_callbacks.bufferSwitch != NULL)
    {
        m_callbacks.bufferSwitch(doubleBufferIndex, 0);
    }
}
