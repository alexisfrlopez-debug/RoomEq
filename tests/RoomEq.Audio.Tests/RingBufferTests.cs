using RoomEq.Audio.Routing;
using Xunit;

namespace RoomEq.Audio.Tests;

/// <summary>Tampon circulaire SPSC (spec I-AUD-03) — portage des tests legacy v2.2.2.</summary>
public class RingBufferTests
{
    [Fact]
    public void Write_Then_Read_ReturnsSameData()
    {
        var rb = new RingBuffer(1024);
        var input = Enumerable.Range(0, 300).Select(i => (float)i / 300f).ToArray();
        rb.Write(input);

        var output = new float[300];
        int read = rb.Read(output);

        Assert.Equal(300, read);
        Assert.Equal(input, output);
    }

    [Fact]
    public void Read_WithEmptyBuffer_ReturnsSilence()
    {
        var rb = new RingBuffer(1024);
        var output = new float[100];
        for (int i = 0; i < output.Length; i++) output[i] = 1f;

        int read = rb.Read(output);

        Assert.Equal(0, read);
        Assert.All(output, v => Assert.Equal(0f, v));
    }

    [Fact]
    public void Read_PadsRemainderWithSilence()
    {
        var rb = new RingBuffer(1024);
        rb.Write(new float[] { 0.5f, 0.25f });
        var output = new float[5];
        int read = rb.Read(output);

        Assert.Equal(2, read);
        Assert.Equal(0.5f, output[0]);
        Assert.Equal(0.25f, output[1]);
        Assert.Equal(0f, output[2]);
        Assert.Equal(0f, output[3]);
        Assert.Equal(0f, output[4]);
    }

    [Fact]
    public void WrapAround_PreservesContinuity()
    {
        // capacité volontairement non alignée sur les tailles lues/écrites
        var rb = new RingBuffer(997);
        var outputAll = new List<float>();
        var chunk = new float[64];

        long expected = 0;
        for (int pass = 0; pass < 40; pass++)
        {
            for (int i = 0; i < chunk.Length; i++) chunk[i] = expected + i;
            rb.Write(chunk);
            expected += chunk.Length;

            var outBuf = new float[64];
            rb.Read(outBuf);
            outputAll.AddRange(outBuf);
        }

        for (int i = 0; i < outputAll.Count; i++)
            Assert.Equal(i, outputAll[i]);
    }

    [Fact]
    public void Overflow_DropsOldest_AndBoundsLatency()
    {
        var rb = new RingBuffer(512);
        var big = new float[2048];
        for (int i = 0; i < big.Length; i++) big[i] = i;

        rb.Write(big);

        // après saturation, seuls les derniers échantillons restent lisibles
        Assert.True(rb.Available <= 512);
        Assert.True(rb.Dropped > 0);

        var output = new float[512];
        int read = rb.Read(output);
        Assert.Equal(512, read);
        // les 512 derniers échantillons écrits sont 1536..2047
        Assert.Equal(1536f, output[0]);
        Assert.Equal(2047f, output[511]);
    }

    [Fact]
    public void Clear_EmptiesBuffer_AndResetsDrops()
    {
        var rb = new RingBuffer(512);
        rb.Write(new float[512 + 100]);   // sature : Dropped > 0
        Assert.True(rb.Dropped > 0);
        rb.Clear();
        Assert.Equal(0, rb.Available);
        Assert.Equal(0, rb.Dropped);
    }

    [Fact]
    public void SmallCapacity_IsFlooredTo256()
    {
        var rb = new RingBuffer(10);
        Assert.Equal(256, rb.Capacity);
        var data = Enumerable.Range(0, 200).Select(i => (float)i).ToArray();
        Assert.Equal(200, rb.Write(data));
        var outBuf = new float[200];
        Assert.Equal(200, rb.Read(outBuf));
        Assert.Equal(199f, outBuf[^1]);
    }
}
