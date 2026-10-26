using RoomEq.App.ViewModels;
using Xunit;

namespace RoomEq.App.Tests;

/// <summary>
/// Tampon circulaire thread-safe (spec I-APP-05) : capacité, instantanés
/// partiels/complets, boucle en deux segments, remise à zéro.
/// </summary>
public class RollingBufferTests
{
    [Fact]
    public void Capacity_IsSecondsTimesRate()
    {
        var b = new RollingBuffer(2, 1000);
        Assert.Equal(2000, b.Capacity);
        Assert.Equal(1000, b.SampleRate);
    }

    [Fact]
    public void Snapshot_ReturnsLastSamples_Partial()
    {
        var b = new RollingBuffer(2, 1000);
        b.Add(Enumerable.Range(0, 500).Select(i => (float)i).ToArray());
        var snap = b.Snapshot(2000);
        Assert.Equal(500, snap.Length);
        Assert.Equal(0f, snap[0], 3);
        Assert.Equal(499f, snap[^1], 3);
    }

    [Fact]
    public void Snapshot_WrapsAround_InTwoSegments()
    {
        var b = new RollingBuffer(2, 1000);
        // 2 500 échantillons dans une capacité de 2 000 : boucle atteinte,
        // les 500 premiers sont écrasés par 2000..2499
        b.Add(Enumerable.Range(0, 2500).Select(i => (float)i).ToArray());

        var snap = b.Snapshot(2000);
        Assert.Equal(2000, snap.Length);
        // les 2000 derniers : 500..2499
        Assert.Equal(500f, snap[0], 3);
        Assert.Equal(2499f, snap[^1], 3);

        // instantané borné : les N derniers
        var last = b.Snapshot(10);
        Assert.Equal(10, last.Length);
        Assert.Equal(2490f, last[0], 3);
        Assert.Equal(2499f, last[^1], 3);
    }

    [Fact]
    public void Snapshot_AfterWrapContinuousWrite_ReturnsLastWritten()
    {
        var b = new RollingBuffer(1, 100);
        for (int block = 0; block < 30; block++)
            b.Add(Enumerable.Range(0, 50).Select(i => (float)(block * 50 + i)).ToArray());

        var snap = b.Snapshot(100);
        Assert.Equal(100, snap.Length);
        // écrits : 0..1499 → les 100 derniers : 1400..1499
        Assert.Equal(1400f, snap[0], 3);
        Assert.Equal(1499f, snap[^1], 3);
    }

    [Fact]
    public void Clear_EmptiesTheBuffer()
    {
        var b = new RollingBuffer(1, 100);
        b.Add(new float[100]);
        b.Clear();
        Assert.Empty(b.Snapshot(100));
    }

    [Fact]
    public void ConcurrentAddAndSnapshot_DoNotCorrupt()
    {
        var b = new RollingBuffer(1, 48000);
        var stop = new ManualResetEvent(false);
        var writer = new Thread(() =>
        {
            int counter = 0;
            while (!stop.WaitOne(0))
            {
                b.Add(Enumerable.Range(0, 480).Select(i => (float)counter++).ToArray());
                Thread.SpinWait(50);
            }
        });
        writer.Start();

        for (int i = 0; i < 200; i++)
        {
            var snap = b.Snapshot(48000);
            // aucune valeur aberrante : la copie sous verrou est cohérente
            foreach (var v in snap)
                Assert.True(float.IsFinite(v));
        }

        stop.Set();
        writer.Join();
    }
}
