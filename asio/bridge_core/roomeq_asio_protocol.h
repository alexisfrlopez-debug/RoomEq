/* -*- coding: utf-8 -*-
   RoomEq ASIO bridge — shared protocol (portable C, used by BOTH sides).

   - Windows ASIO driver (RoomEqAsio64.dll, loaded inside the DAW process)
     writes the audio the DAW produces into a lock-free ring living in a
     named shared memory section ("Local\RoomEq.Asio.Bridge.v1").
   - RoomEq application (C#, AsioBridgeHost) reads the ring and feeds the
     mixer channel "DAW (ASIO)", which goes through the main EQ chain and
     out to the real sound card.

   This header contains NO Windows dependency so the ring math, the pacing
   PLL and the header layout can be unit-tested on any platform (g++ on
   Linux, MSVC on Windows). The C# mirror is AsioBridgeInterop.cs — the two
   must stay in sync (same layout, same constants).

   Concurrency contract:
     - writePos / readPos / hostBeat / hostClock are 64-bit signed integers
       accessed with relaxed atomic semantics (single-writer single-reader
       per counter). Monotonic within a session; on (re)start a session id
       changes and the counters may reset.
     - audio ring frames are interleaved float stereo, frame index = pos & (ringFrames-1).
*/
#ifndef ROOMEQ_ASIO_PROTOCOL_H
#define ROOMEQ_ASIO_PROTOCOL_H

#include <stdint.h>

#ifdef __cplusplus
extern "C" {
#endif

/* ---- constants (keep in sync with AsioBridgeInterop.cs) ---- */

#define ROOMEQ_ASIO_MAGIC   0x52454153u          /* 'READ' -> "ROOMEQ ASIO" sentinel */
#define ROOMEQ_ASIO_VERSION 1u

/* Name of the shared section + event (Local\ namespace: same session). */
#define ROOMEQ_ASIO_MAP_NAME    "Local\\RoomEq.Asio.Bridge.v1"
#define ROOMEQ_ASIO_EVENT_NAME  "Local\\RoomEq.Asio.Data.v1"

/* Ring capacity in frames (power of two). ~0.68 s @ 48 kHz. */
#define ROOMEQ_ASIO_RING_FRAMES  32768
#define ROOMEQ_ASIO_CHANNELS     2

/* Header is padded to 256 bytes so the float ring starts cache-friendly. */
#define ROOMEQ_ASIO_HEADER_BYTES 256

/* Error / status codes surfaced to the UI. */
#define ROOMEQ_ASIO_OK            0
#define ROOMEQ_ASIO_ERR_NO_HOST   1   /* RoomEq app not running / not hosting */
#define ROOMEQ_ASIO_ERR_OVERFLOW  2   /* ring overflowed (frames dropped)    */
#define ROOMEQ_ASIO_ERR_STALLED   3   /* host stopped consuming              */

/*
 * Shared control block (fixed layout, natural 8-byte alignment).
 * C# mirror: [StructLayout(LayoutKind.Sequential, Pack = 8)]
 */
typedef struct RoomeqAsioHeader
{
    volatile uint32_t magic;          /* ROOMEQ_ASIO_MAGIC when initialized  */
    volatile uint32_t version;        /* ROOMEQ_ASIO_VERSION                 */

    volatile uint32_t sampleRate;     /* negotiated ASIO rate (44100/48000…) */
    volatile uint32_t bufferFrames;   /* ASIO buffer size (frames per pass)  */
    volatile uint32_t ringFrames;     /* ROOMEQ_ASIO_RING_FRAMES             */
    volatile uint32_t channels;       /* ROOMEQ_ASIO_CHANNELS                */

    volatile uint32_t hostActive;     /* set by the app while hosting        */
    volatile uint32_t clientActive;   /* set by the DLL while streaming      */
    volatile uint32_t session;        /* increments on every (re)start       */
             uint32_t _pad0;          /* alignment to 8 for the int64 block  */

    volatile int64_t writePos;        /* frames written by the DAW via DLL   */
    volatile int64_t readPos;         /* frames consumed by the app          */
    volatile int64_t droppedFrames;   /* ring overflow counter (DLL)        */

    volatile int64_t hostClock;       /* frames of host master clock (pace)  */
    volatile int64_t hostBeat;        /* bumped by host on every mixer pass  */

    uint32_t hostSampleRate;          /* host master rate, for drift display */
    uint32_t xruns;                   /* underruns seen by the DLL           */

    /* 88 bytes above; pad to a 256-byte header. */
    uint8_t  reserved[ROOMEQ_ASIO_HEADER_BYTES - 88];
} RoomeqAsioHeader;

/*
 * Audio ring follows the header in the same mapping:
 *   float ring[ringFrames][channels]
 * Mapping size = ROOMEQ_ASIO_HEADER_BYTES + ringFrames * channels * 4.
 */

/* ---- pure helpers (implemented in roomeq_asio_bridge.c) ---- */

/* Available frames to read (>= 0, may exceed ring size only if consumer
   is stalled: caller should clamp). */
int64_t roomeq_asio_available(const RoomeqAsioHeader* h);

/* Free frames for writing before the writer would lap the reader. */
int64_t roomeq_asio_free(const RoomeqAsioHeader* h);

/* Copy n frames out of the ring starting at readPos (advancing it is the
   caller's job). Safe when n <= available. Returns frames copied. */
int64_t roomeq_asio_read(const RoomeqAsioHeader* h, const float* ring,
                         int64_t wanted, float* dest);

/* Drop-oldest write: writes n frames at writePos, advancing it past
   readPos if the ring would overflow (updates droppedFrames). */
int64_t roomeq_asio_write(RoomeqAsioHeader* h, float* ring,
                          int64_t n, const float* src);

/* Frame pointer of the audio ring inside the mapping. */
static inline float* roomeq_asio_ring(RoomeqAsioHeader* h)
{
    return (float*)((uint8_t*)h + ROOMEQ_ASIO_HEADER_BYTES);
}
static inline const float* roomeq_asio_ring_const(const RoomeqAsioHeader* h)
{
    return (const float*)((const uint8_t*)h + ROOMEQ_ASIO_HEADER_BYTES);
}

/*
 * Pacing PLL: the DLL free-runs its buffer callbacks from a high priority
 * thread. To stay glued to the host master clock without drifting, the
 * interval is continuously corrected based on how hostClock advances
 * relative to local elapsed frames. Pure math, unit-testable.
 */
typedef struct RoomeqAsioPll
{
    double ratio;       /* host_frames_per_local_frame, ~1.0 */
    int    locked;      /* 1 after initial convergence       */
} RoomeqAsioPll;

void     roomeq_asio_pll_init(RoomeqAsioPll* p);
/* Update with (localFramesElapsed, hostFramesElapsed) over the last pass.
   Returns the corrected local->host ratio. */
double   roomeq_asio_pll_update(RoomeqAsioPll* p,
                                double localFrames, double hostFrames);

#ifdef __cplusplus
} /* extern "C" */
#endif

#endif /* ROOMEQ_ASIO_PROTOCOL_H */
