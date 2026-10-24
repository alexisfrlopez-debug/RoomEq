/* -*- coding: utf-8 -*-
   RoomEq ASIO bridge core — unit tests (portable, run with g++ on Linux).

   Build & run:  make test      (see Makefile)
*/
#include "roomeq_asio_protocol.h"

#include <stddef.h>
#include <stdio.h>
#include <stdlib.h>
#include <string.h>

static int g_fail = 0;

#define CHECK(cond) do { \
    if (!(cond)) { printf("ÉCHEC ligne %d : %s\n", __LINE__, #cond); g_fail++; } \
} while (0)

static void test_header_layout(void)
{
    CHECK(sizeof(RoomeqAsioHeader) == ROOMEQ_ASIO_HEADER_BYTES);
    CHECK(offsetof(RoomeqAsioHeader, writePos)  == 40);
    CHECK(offsetof(RoomeqAsioHeader, readPos)   == 48);
    CHECK(offsetof(RoomeqAsioHeader, hostBeat)  == 72);
    CHECK(offsetof(RoomeqAsioHeader, hostSampleRate) == 80);
    printf("  en-tête 256 octets, décalages conformes au miroir C#\n");
}

static RoomeqAsioHeader* make_block(void)
{
    const size_t bytes = ROOMEQ_ASIO_HEADER_BYTES
                       + (size_t)ROOMEQ_ASIO_RING_FRAMES * ROOMEQ_ASIO_CHANNELS * sizeof(float);
    RoomeqAsioHeader* h = (RoomeqAsioHeader*)calloc(1, bytes);
    h->magic = ROOMEQ_ASIO_MAGIC;
    h->version = ROOMEQ_ASIO_VERSION;
    h->sampleRate = 48000;
    h->bufferFrames = 256;
    h->ringFrames = ROOMEQ_ASIO_RING_FRAMES;
    h->channels = ROOMEQ_ASIO_CHANNELS;
    return h;
}

static void test_roundtrip(void)
{
    RoomeqAsioHeader* h = make_block();
    float* ring = roomeq_asio_ring(h);

    const int64_t N = 511;                 /* nombre premier : force les wraps */
    float* src = (float*)malloc((size_t)N * 2 * sizeof(float));
    float* dst = (float*)malloc((size_t)N * 2 * sizeof(float));
    for (int64_t i = 0; i < N * 2; i++) src[i] = (float)i;

    int64_t written = roomeq_asio_write(h, ring, N, src);
    CHECK(written == N);
    CHECK(h->writePos == N);
    CHECK(roomeq_asio_available(h) == N);

    int64_t rd = roomeq_asio_read(h, ring, N, dst);
    CHECK(rd == N);
    CHECK(memcmp(src, dst, (size_t)N * 2 * sizeof(float)) == 0);
    h->readPos += N;
    CHECK(roomeq_asio_available(h) == 0);

    /* plusieurs tours d'anneau : les positions linéaires croissent, jamais de perte */
    for (int pass = 0; pass < 40; pass++)
    {
        roomeq_asio_write(h, ring, N, src);
        rd = roomeq_asio_read(h, ring, N, dst);
        CHECK(rd == N);
        CHECK(memcmp(src, dst, (size_t)N * 2 * sizeof(float)) == 0);
        h->readPos += N;
    }
    CHECK(h->writePos == 41 * N);
    CHECK(h->droppedFrames == 0);
    printf("  aller-retour 41×%d éch. sans perte ni décalage\n", (int)N);

    free(src); free(dst); free(h);
}

static void test_drop_oldest(void)
{
    RoomeqAsioHeader* h = make_block();
    float* ring = roomeq_asio_ring(h);

    /* remplir presque tout */
    int64_t cap = h->ringFrames;
    float frame[2] = { 1.0f, -1.0f };
    for (int64_t i = 0; i < cap - 10; i++)
        roomeq_asio_write(h, ring, 1, frame);
    CHECK(roomeq_asio_free(h) == 10);

    /* écrire plus que libre -> les plus vieux sont ignorés, comptés */
    float many[2 * 64];
    for (int i = 0; i < 128; i++) many[i] = (float)i;
    roomeq_asio_write(h, ring, 64, many);
    CHECK(h->droppedFrames == 54);
    CHECK(roomeq_asio_available(h) == cap);
    CHECK(roomeq_asio_free(h) == 0);
    printf("  saturation : 54 éch. les plus anciennes ignorées, comptées\n");

    free(h);
}

static void test_pll(void)
{
    RoomeqAsioPll p;
    roomeq_asio_pll_init(&p);
    CHECK(p.ratio == 1.0 && p.locked == 0);

    /* première mesure : verrouillage immédiat */
    double r = roomeq_asio_pll_update(&p, 48000.0, 48000.0);
    CHECK(p.locked == 1);
    CHECK(r == 1.0);

    /* dérive mesurée de 0,01 % (horloge hôte plus rapide) */
    double local = 48000.0, host = 48004.8;
    for (int i = 0; i < 3000; i++)
        r = roomeq_asio_pll_update(&p, local, host);
    CHECK(r > 1.0 && r < 1.0002);
    CHECK(r > 1.000095);  /* convergé vers le ratio mesuré (approche géométrique) */

    /* entrée dégénérée : inchangé */
    double before = p.ratio;
    roomeq_asio_pll_update(&p, 0.0, 0.0);
    CHECK(p.ratio == before);
    printf("  PLL : dérive 0,01 %% suivie (ratio final %.6f)\n", r);
}

int main(void)
{
    printf("== RoomEq ASIO bridge core ==\n");
    test_header_layout();
    test_roundtrip();
    test_drop_oldest();
    test_pll();
    if (g_fail) { printf("RÉSULTAT : %d échec(s)\n", g_fail); return 1; }
    printf("RÉSULTAT : tout est vert\n");
    return 0;
}
