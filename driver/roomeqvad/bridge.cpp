/*++

Module Name:

    bridge.cpp

Abstract:

    RoomEq virtual audio bridge implementation. See bridge.h.

    Concurrency: all state is guarded by one spin lock. The capture copy runs
    under the lock so the render buffer can never be freed mid-copy (buffer
    lifetime is managed by Publish/UnpublishRenderBuffer on the same lock).
    Copies are a few milliseconds of audio at most, so contention is
    negligible.

--*/

#include "bridge.h"

namespace
{
    struct BridgeState
    {
        KSPIN_LOCK  Lock;
        BOOLEAN     Initialized;

        // Render side.
        PUCHAR      RenderBuffer;       // NULL when no render stream is alive
        ULONG       RenderBufferSize;
        LONGLONG    RenderWriteLinear;  // linear bytes written by the engine
        ULONG       RenderLastWrapped;  // last wrapped write position seen

        // Capture side.
        LONGLONG    LastCaptureEnd;     // last absolute end consumed (-1 = none)
        LONGLONG    CaptureOffset;      // render-abs = capture-linear + offset
        BOOLEAN     ForceRealign;
    };

    BridgeState g_Bridge = {};

    _IRQL_requires_max_(DISPATCH_LEVEL)
    void ResetCaptureLocked(_Inout_ BridgeState* s, _In_ LONGLONG captureLinear)
    {
        // A capture stream (re)starts: align it to the render stream's current
        // write position so it reads "now" instead of stale history.
        s->CaptureOffset = s->RenderWriteLinear - captureLinear;
        s->ForceRealign = FALSE;
    }
}

// KeInitializeSpinLock once.
void RoomEqBridge::Initialize()
{
    if (!g_Bridge.Initialized)
    {
        KeInitializeSpinLock(&g_Bridge.Lock);
        g_Bridge.Initialized = TRUE;
        g_Bridge.LastCaptureEnd = -1;
        g_Bridge.ForceRealign = TRUE;
    }
}

void RoomEqBridge::PublishRenderBuffer(_In_opt_ PUCHAR Buffer, _In_ ULONG Size)
{
    if (!g_Bridge.Initialized) return;
    KIRQL oldIrql;
    KeAcquireSpinLock(&g_Bridge.Lock, &oldIrql);
    g_Bridge.RenderBuffer = Buffer;
    g_Bridge.RenderBufferSize = (Buffer != NULL) ? Size : 0;
    g_Bridge.RenderWriteLinear = 0;
    g_Bridge.RenderLastWrapped = 0;
    g_Bridge.ForceRealign = TRUE;   // render stream restarted
    KeReleaseSpinLock(&g_Bridge.Lock, oldIrql);
}

void RoomEqBridge::UnpublishRenderBuffer()
{
    if (!g_Bridge.Initialized) return;
    KIRQL oldIrql;
    KeAcquireSpinLock(&g_Bridge.Lock, &oldIrql);
    g_Bridge.RenderBuffer = NULL;
    g_Bridge.RenderBufferSize = 0;
    KeReleaseSpinLock(&g_Bridge.Lock, oldIrql);
}

void RoomEqBridge::OnRenderWritePosition(_In_ ULONG WrappedWritePosition)
{
    if (!g_Bridge.Initialized) return;
    KIRQL oldIrql;
    KeAcquireSpinLock(&g_Bridge.Lock, &oldIrql);

    if (g_Bridge.RenderBuffer != NULL && g_Bridge.RenderBufferSize != 0)
    {
        ULONG size = g_Bridge.RenderBufferSize;
        ULONG pos = WrappedWritePosition % size;
        ULONG delta = (pos >= g_Bridge.RenderLastWrapped)
                      ? (pos - g_Bridge.RenderLastWrapped)
                      : (pos + size - g_Bridge.RenderLastWrapped);
        g_Bridge.RenderWriteLinear += (LONGLONG)delta;
        g_Bridge.RenderLastWrapped = pos;
    }

    KeReleaseSpinLock(&g_Bridge.Lock, oldIrql);
}

void RoomEqBridge::CaptureInto(_In_opt_ PUCHAR DmaBuffer, _In_ ULONG DmaSize,
                               _In_ LONGLONG CaptureLinear, _In_ ULONG ByteCount)
{
    if (!g_Bridge.Initialized || DmaBuffer == NULL || DmaSize == 0 || ByteCount == 0)
    {
        return;
    }

    KIRQL oldIrql;
    KeAcquireSpinLock(&g_Bridge.Lock, &oldIrql);

    // Realign when a capture stream (re)starts (position went backwards or
    // first use) or when the render stream was (re)published.
    LONGLONG absEnd = CaptureLinear + (LONGLONG)ByteCount;
    if (g_Bridge.ForceRealign ||
        g_Bridge.LastCaptureEnd == -1 ||
        absEnd < g_Bridge.LastCaptureEnd)
    {
        ResetCaptureLocked(&g_Bridge, CaptureLinear);
    }
    g_Bridge.LastCaptureEnd = absEnd;

    if (g_Bridge.RenderBuffer == NULL || g_Bridge.RenderBufferSize == 0)
    {
        // No render stream alive: pure silence (cable with nothing plugged in).
        RtlZeroMemory(DmaBuffer, DmaSize);
        KeReleaseSpinLock(&g_Bridge.Lock, oldIrql);
        return;
    }

    const PUCHAR src = g_Bridge.RenderBuffer;
    const ULONG  srcSize = g_Bridge.RenderBufferSize;
    const LONGLONG written = g_Bridge.RenderWriteLinear;

    ULONG done = 0;
    while (done < ByteCount)
    {
        LONGLONG absPos = CaptureLinear + (LONGLONG)done + g_Bridge.CaptureOffset;
        LONGLONG avail = written - absPos;   // bytes the engine wrote at/after absPos

        ULONG dstOff = (ULONG)((ULONG64)(CaptureLinear + (LONGLONG)done) % DmaSize);
        ULONG dstRun = min(ByteCount - done, DmaSize - dstOff);

        if (avail <= 0)
        {
            // Render engine has not written this far yet: silence.
            RtlZeroMemory(DmaBuffer + dstOff, dstRun);
            done += dstRun;
            continue;
        }

        ULONG srcOff = (ULONG)((ULONG64)absPos % srcSize);
        ULONG srcRun = min(dstRun, srcSize - srcOff);
        ULONG copyRun = min(srcRun, (ULONG)min(avail, (LONGLONG)0x40000000));

        RtlCopyMemory(DmaBuffer + dstOff, src + srcOff, copyRun);
        if (copyRun < dstRun)
        {
            RtlZeroMemory(DmaBuffer + dstOff + copyRun, dstRun - copyRun);
        }
        done += dstRun;
    }

    KeReleaseSpinLock(&g_Bridge.Lock, oldIrql);
}
