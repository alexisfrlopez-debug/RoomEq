/*++

Module Name:

    bridge.h

Abstract:

    RoomEq virtual audio bridge. The render WaveRT stream (the endpoint Windows
    plays into) publishes its DMA buffer and the audio engine write position;
    the capture WaveRT stream (the endpoint RoomEq reads) copies the rendered
    audio at its own pace, inserting silence when the engine has not written
    the data yet. This reproduces the behaviour of a virtual audio cable
    without any third-party driver.

--*/

#ifndef _ROOMEQ_BRIDGE_H_
#define _ROOMEQ_BRIDGE_H_

#include <ntddk.h>

namespace RoomEqBridge
{
    // One-time initialization (called from DriverEntry).
    void Initialize();

    // ---- Called by the render stream (m_bCapture == FALSE) ----

    // Publishes (or replaces) the render DMA buffer. Resets the internal
    // linear write counter and forces the capture side to realign.
    void PublishRenderBuffer(_In_opt_ PUCHAR Buffer, _In_ ULONG Size);

    // Withdraws the render DMA buffer (stream is being torn down).
    void UnpublishRenderBuffer();

    // Forwards the (wrapped) WaveRT buffer write position the audio engine
    // reported via SetCurrentWritePosition. Advances the linear counter.
    void OnRenderWritePosition(_In_ ULONG WrappedWritePosition);

    // ---- Called by the capture stream (m_bCapture == TRUE) ----

    // Fills DmaBuffer with the audio bytes corresponding to the absolute
    // range [captureLinear, captureLinear + ByteCount). Bytes the render
    // engine has not written yet are zero-filled (silence). Handles the
    // initial alignment when a capture stream starts mid-render.
    void CaptureInto(_In_opt_ PUCHAR DmaBuffer, _In_ ULONG DmaSize,
                     _In_ LONGLONG CaptureLinear, _In_ ULONG ByteCount);
}

#endif  // _ROOMEQ_BRIDGE_H_
