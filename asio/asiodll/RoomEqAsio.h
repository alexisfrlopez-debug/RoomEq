/* -*- coding: utf-8 -*-
   RoomEq ASIO bridge driver — IASIO implementation.

   The DAW loads this DLL (COM in-proc) and calls IASIO. Playback audio is
   pushed into the shared ring read by the RoomEq application; inputs are
   silence in v1. A high priority pacing thread free-runs the buffer
   callbacks, glued to the host master clock through the bridge PLL.
*/
#ifndef ROOMEQ_ASIO_DRIVER_H
#define ROOMEQ_ASIO_DRIVER_H

#include <windows.h>
#include <objbase.h>

#include "asio_decl.h"
#include "../bridge_core/roomeq_asio_protocol.h"

class RoomEqAsioDriver : public IASIO
{
public:
    RoomEqAsioDriver();
    ~RoomEqAsioDriver();

    /* ---- IUnknown ---- */
    ULONG STDMETHODCALLTYPE AddRef() override;
    ULONG STDMETHODCALLTYPE Release() override;
    HRESULT STDMETHODCALLTYPE QueryInterface(REFIID riid, void** ppv) override;

    /* ---- IASIO ---- */
    ASIOBool  ASIOInit(void* sysHandle) override;
    void      ASIOgetDriverName(char* name) override;
    int32_t   ASIOgetDriverVersion() override;
    void      ASIOgetErrorMessage(char* string) override;
    ASIOError ASIOstart() override;
    ASIOError ASIOstop() override;
    ASIOError ASIOgetChannels(int32_t* numInputChannels, int32_t* numOutputChannels) override;
    ASIOError ASIOgetLatencies(int32_t* inputLatency, int32_t* outputLatency) override;
    ASIOError ASIOgetBufferSize(int32_t* minSize, int32_t* maxSize,
                                int32_t* preferredSize, int32_t* granularity) override;
    ASIOError ASIOcanSampleRate(ASIOSampleRate sampleRate) override;
    ASIOError ASIOsetSampleRate(ASIOSampleRate sampleRate) override;
    ASIOError ASIOgetClockSources(ASIOClockSource* clocks, int32_t* numSources) override;
    ASIOError ASIOsetClockSource(int32_t reference) override;
    ASIOError ASIOgetSamplePosition(ASIOSamples* sPos, ASIOTimeStamp* tStamp) override;
    ASIOError ASIOgetChannelInfo(ASIOChannelInfo* info) override;
    ASIOError ASIOcreateBuffers(ASIOBufferInfo* bufferInfos, int32_t numChannels,
                                int32_t bufferSize, void* callbacks) override;
    ASIOError ASIOdisposeBuffers() override;
    ASIOError ASIOcontrolPanel() override;
    ASIOError ASIOfuture(int32_t selector, void* opt) override;
    ASIOError ASIOoutputReady() override;
    int32_t   ASIOMessage(int32_t selector, int32_t value, void* message, double* opt) override;

private:
    /* shared transport */
    bool AttachShared();
    void DetachShared();

    /* pacing thread */
    static DWORD WINAPI PacingThreadProc(LPVOID param);
    void        RunPacing();
    void        DoBufferPass(int32_t doubleBufferIndex);

    LONG     m_refs;
    HANDLE   m_map;
    HANDLE   m_dataEvent;      /* signaled to wake the app reader */
    struct RoomeqAsioHeader* m_hdr;
    float*   m_ring;

    ASIOSampleRate m_rate;
    int32_t  m_bufferSize;
    bool     m_initialized;
    bool     m_running;
    bool     m_buffersCreated;

    ASIOBufferInfo* m_infos;
    int32_t  m_infoCount;
    ASIOCallbacks m_callbacks;
    bool     m_callbacksSet;
    bool     m_useTimeInfo;

    HANDLE   m_thread;
    HANDLE   m_stopEvent;
    double   m_qpcFreq;
    RoomeqAsioPll m_pll;
    int64_t  m_localFrames;    /* frames emitted by the pacing clock */
    int64_t  m_lastHostClock;  /* host clock snapshot for PLL updates */
    float*   m_scratch;        /* interleaved staging buffer (bufferSize*2) */
    char     m_error[160];
};

#endif /* ROOMEQ_ASIO_DRIVER_H */
