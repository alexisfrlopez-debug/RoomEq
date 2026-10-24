/* -*- coding: utf-8 -*-
   Minimal ASIO 2 interface declarations for the RoomEq ASIO bridge driver.

   This is an independent re-declaration of the public, published ASIO
   protocol (interface GUID, vtable order, structures and error codes are
   public knowledge used by every ASIO host and open-source drivers such
   as ASIO2WASAPI / FlexASIO / Synchronous Audio Router). The Steinberg
   ASIO SDK itself is NOT required and is NOT included.

   64-bit only (modern DAWs). The DAW loads this DLL in-process and talks
   through the IASIO COM interface below.
*/
#ifndef ROOMEQ_ASIO_H
#define ROOMEQ_ASIO_H

#include <stdint.h>

typedef int32_t  ASIOBool;      /* 0 = false, else true */
typedef double   ASIOSampleRate;
typedef uint32_t ASIOUlong;

/* ASIOError codes */
enum ASIOError
{
    ASE_OK = 0,               /* success */
    ASE_SUCCESS = 0x3f4847a0, /* rare success (64 bit return) */
    ASE_NotPresent = 1,       /* hardware missing */
    ASE_HWMalfunction = 2,
    ASE_InvalidParameter = 3,
    ASE_InvalidMode = 4,
    ASE_SPNotAdvancing = 5,
    ASE_NoClock = 6,
    ASE_NoMemory = 7
};

/* ASIOSampleType — we expose Float32LSB (native RoomEq bridge format) */
enum ASIOSampleType
{
    ASIOSTInt16MSB    = 0,
    ASIOSTInt24MSB    = 1,
    ASIOSTInt32MSB    = 2,
    ASIOSTFloat32MSB  = 3,
    ASIOSTFloat64MSB  = 4,
    ASIOSTInt32MSB16  = 5,
    ASIOSTInt32MSB18  = 6,
    ASIOSTInt32MSB20  = 7,
    ASIOSTInt32MSB24  = 8,
    ASIOSTInt16LSB    = 9,
    ASIOSTInt24LSB    = 10,
    ASIOSTInt32LSB    = 11,
    ASIOSTFloat32LSB  = 12,
    ASIOSTFloat64LSB  = 13,
    ASIOSTInt32LSB16  = 14,
    ASIOSTInt32LSB18  = 15,
    ASIOSTInt32LSB20  = 16,
    ASIOSTInt32LSB24  = 17,
    ASIOSTDSDInt8LSB1 = 18,
    ASIOSTDSDInt8MSB1 = 19,
    ASIOSTDSDInt8LSB8 = 20,
    ASIOSTDSDInt8MSB8 = 21
};

typedef struct ASIO64bit
{
    uint32_t hi;
    uint32_t lo;
} ASIO64bit;

typedef ASIO64bit ASIOSamples;
typedef ASIO64bit ASIOTimeStamp;

typedef struct ASIOTime
{
    int32_t        reserved[4];
    ASIOTimeStamp  timeInfo;
    ASIOSamples    samplePosition;
    ASIOSampleRate sampleRate;
    uint32_t       flags;
    char           future[64];
} ASIOTime;

typedef struct ASIOClockSource
{
    int32_t  index;
    int32_t  associatedChannel;
    int32_t  associatedGroup;
    ASIOBool isCurrent;
    char     name[32];
} ASIOClockSource;

typedef struct ASIOChannelInfo
{
    int32_t       channel;
    ASIOBool      isInput;
    ASIOBool      isActive;
    int32_t       channelGroup;
    int32_t       type;        /* ASIOSampleType */
    char          name[32];
} ASIOChannelInfo;

typedef struct ASIOBufferInfo
{
    ASIOBool isInput;
    int32_t  channelNum;
    void*    buffers[2];
} ASIOBufferInfo;

typedef struct ASIOCallbacks
{
    void (*bufferSwitch)(int32_t doubleBufferIndex, ASIOBool directProcess);
    void (*sampleRateDidChange)(ASIOSampleRate sRate);
    int32_t (*asioMessage)(int32_t selector, int32_t value, void* message, double* opt);
    ASIOTime* (*bufferSwitchTimeInfo)(ASIOTime* params, int32_t doubleBufferIndex, ASIOBool directProcess);
} ASIOCallbacks;

/* asioMessage selectors */
enum
{
    kAsioSelectorSupported   = 1,
    kAsioEngineVersion       = 2,
    kAsioResetRequest        = 3,
    kAsioBufferSizeChange    = 4,
    kAsioResyncRequest       = 5,
    kAsioLatenciesChanged    = 6,
    kAsioSupportsTimeInfo    = 7,
    kAsioSupportsTimeCode    = 8,
    kAsioMMC                 = 9,
    kAsioSupportsInputMonitor = 10,
    kAsioSupportsInputGain   = 11,
    kAsioSupportsInputMeter  = 12,
    kAsioSupportsOutputGain  = 13,
    kAsioSupportsOutputMeter = 14,
    kAsioOverload            = 15
};

/* The one and only IASIO interface GUID: {A91EABA1-CF4B-11D3-B96A-00A0C9A618D8} */
static const GUID CLSID_RoomEqAsio =
{ 0x8f5e3a91, 0x4b2c, 0x4d6e, { 0x9a, 0x1f, 0x2e, 0x7c, 0x5b, 0x8d, 0x9a, 0x03 } };
static const GUID IID_IASIO =
{ 0xa91eaba1, 0xcf4b, 0x11d3, { 0xb9, 0x6a, 0x0, 0xa0, 0xc9, 0xa6, 0x18, 0xd8 } };

/* IASIO vtable order — do NOT reorder. */
struct IASIO : public IUnknown
{
    virtual ASIOBool     ASIOInit(void* sysHandle) = 0;
    virtual void         ASIOgetDriverName(char* name) = 0;
    virtual int32_t      ASIOgetDriverVersion() = 0;
    virtual void         ASIOgetErrorMessage(char* string) = 0;
    virtual ASIOError    ASIOstart() = 0;
    virtual ASIOError    ASIOstop() = 0;
    virtual ASIOError    ASIOgetChannels(int32_t* numInputChannels, int32_t* numOutputChannels) = 0;
    virtual ASIOError    ASIOgetLatencies(int32_t* inputLatency, int32_t* outputLatency) = 0;
    virtual ASIOError    ASIOgetBufferSize(int32_t* minSize, int32_t* maxSize,
                                           int32_t* preferredSize, int32_t* granularity) = 0;
    virtual ASIOError    ASIOcanSampleRate(ASIOSampleRate sampleRate) = 0;
    virtual ASIOError    ASIOsetSampleRate(ASIOSampleRate sampleRate) = 0;
    virtual ASIOError    ASIOgetClockSources(ASIOClockSource* clocks, int32_t* numSources) = 0;
    virtual ASIOError    ASIOsetClockSource(int32_t reference) = 0;
    virtual ASIOError    ASIOgetSamplePosition(ASIOSamples* sPos, ASIOTimeStamp* tStamp) = 0;
    virtual ASIOError    ASIOgetChannelInfo(ASIOChannelInfo* info) = 0;
    virtual ASIOError    ASIOcreateBuffers(ASIOBufferInfo* bufferInfos, int32_t numChannels,
                                          int32_t bufferSize, void* callbacks)  = 0;
    virtual ASIOError    ASIOdisposeBuffers() = 0;
    virtual ASIOError    ASIOcontrolPanel() = 0;
    virtual ASIOError    ASIOfuture(int32_t selector, void* opt) = 0;
    virtual ASIOError    ASIOoutputReady() = 0;
    virtual int32_t      ASIOMessage(int32_t selector, int32_t value, void* message, double* opt) = 0;
};

#endif /* ROOMEQ_ASIO_H */
