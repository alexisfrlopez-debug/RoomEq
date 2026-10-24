/* -*- coding: utf-8 -*-
   RoomEq ASIO bridge — pure helper implementations (portable C).
*/
#include "roomeq_asio_protocol.h"

#include <string.h>

/* Compile-time layout checks: the C# mirror (AsioBridgeInterop.cs) relies
   on this exact binary layout. */
typedef char roomeq_static_check_header_size[
    (sizeof(RoomeqAsioHeader) == ROOMEQ_ASIO_HEADER_BYTES) ? 1 : -1];

int64_t roomeq_asio_available(const RoomeqAsioHeader* h)
{
    int64_t w = h->writePos;
    int64_t r = h->readPos;
    return (w > r) ? (w - r) : 0;
}

int64_t roomeq_asio_free(const RoomeqAsioHeader* h)
{
    int64_t cap = (int64_t)h->ringFrames;
    int64_t used = roomeq_asio_available(h);
    int64_t f = cap - used;
    return (f > 0) ? f : 0;
}

int64_t roomeq_asio_read(const RoomeqAsioHeader* h, const float* ring,
                         int64_t wanted, float* dest)
{
    int64_t avail = roomeq_asio_available(h);
    int64_t n = (wanted < avail) ? wanted : avail;
    if (n <= 0) return 0;

    const int64_t mask = (int64_t)h->ringFrames - 1;
    const int   ch = (int)h->channels;
    int64_t pos = h->readPos;
    int64_t done = 0;

    while (done < n)
    {
        int64_t run = n - done;
        int64_t off = pos & mask;
        if (run > h->ringFrames - off) run = h->ringFrames - off;
        memcpy(dest + done * ch,
               ring + off * ch,
               (size_t)(run * ch) * sizeof(float));
        pos += run;
        done += run;
    }
    return n;
}

int64_t roomeq_asio_write(RoomeqAsioHeader* h, float* ring,
                          int64_t n, const float* src)
{
    if (n <= 0) return 0;

    const int64_t mask = (int64_t)h->ringFrames - 1;
    const int   ch = (int)h->channels;

    /* Drop-oldest: if the writer would lap the reader, advance readPos
       (the stale audio is lost, counted) so playback never blocks the DAW. */
    int64_t free = roomeq_asio_free(h);
    if (n > free)
    {
        int64_t drop = n - free;
        h->readPos = h->readPos + drop;   /* skip oldest frames */
        h->droppedFrames = h->droppedFrames + drop;
    }

    int64_t pos = h->writePos;
    int64_t done = 0;
    while (done < n)
    {
        int64_t run = n - done;
        int64_t off = pos & mask;
        if (run > h->ringFrames - off) run = h->ringFrames - off;
        memcpy(ring + off * ch,
               src + done * ch,
               (size_t)(run * ch) * sizeof(float));
        pos += run;
        done += run;
    }
    h->writePos = pos;
    return n;
}

/* ---- pacing PLL ---- */

void roomeq_asio_pll_init(RoomeqAsioPll* p)
{
    p->ratio  = 1.0;
    p->locked = 0;
}

double roomeq_asio_pll_update(RoomeqAsioPll* p,
                              double localFrames, double hostFrames)
{
    if (localFrames <= 0.0 || hostFrames <= 0.0)
        return p->ratio;

    double measured = hostFrames / localFrames;

    if (!p->locked)
    {
        /* First pass: adopt the measured ratio at once, then refine. */
        p->ratio  = measured;
        p->locked = 1;
        return p->ratio;
    }

    /* Slow correction: 0.5 % of the error per pass. A fast loop would
       track jitter; a slow one only compensates real clock drift
       (typically < 0.02 %). Clamp the per-step move to 100 ppm. */
    double err = measured - p->ratio;
    double step = err * 0.005;
    if (step >  0.0001) step =  0.0001;
    if (step < -0.0001) step = -0.0001;
    p->ratio += step;
    return p->ratio;
}
