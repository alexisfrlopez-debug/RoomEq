using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using RoomEq.Core.Io;
using Xunit;

namespace RoomEq.Core.Tests;

/// <summary>
/// Tests de la bibliothèque locale des signaux téléchargés : enregistrement
/// vérifié par SHA-256, refus des noms non sûrs, index des noms conviviaux,
/// liste résistante à un index corrompu.
/// </summary>
public class SignalStoreTests : IDisposable
{
    private readonly string _dir;
    private readonly SignalStore _store;

    private const string Sha64 = "a3f8c9d0e1b2a3f8c9d0e1b2a3f8c9d0e1b2a3f8c9d0e1b2a3f8c9d0e1b2a3f8";

    public SignalStoreTests()
    {
        _dir = Path.Combine(Path.GetTempPath(), "roomeq-tests-signaux-" + Path.GetRandomFileName());
        _store = new SignalStore(_dir);
    }

    public void Dispose()
    {
        try { Directory.Delete(_dir, recursive: true); } catch { /* nettoyage best effort */ }
    }

    private static byte[] Content(int size = 128)
    {
        var data = new byte[size];
        RandomNumberGenerator.Fill(data);
        return data;
    }

    private static string Sha(byte[] data)
        => Convert.ToHexString(SHA256.HashData(data)).ToLowerInvariant();

    [Fact]
    public void Save_AndList_RoundTripWithFriendlyName()
    {
        byte[] content = Content();
        _store.Save("impulsion-dirac.wav", content, Sha(content), "Impulsion (Dirac)");

        var list = _store.List();
        Assert.Single(list);
        Assert.Equal("impulsion-dirac.wav", list[0].FileName);
        Assert.Equal("Impulsion (Dirac)", list[0].DisplayName);
        Assert.Equal(content.Length, list[0].Bytes);
        Assert.True(File.Exists(_store.PathFor("impulsion-dirac.wav")));
    }

    [Fact]
    public void Save_WrongSha_ThrowsAndWritesNothing()
    {
        Assert.ThrowsAny<Exception>(
            () => _store.Save("a.wav", Content(), Sha64, "A"));

        Assert.Empty(_store.List());
        Assert.False(File.Exists(_store.PathFor("a.wav")));
    }

    [Fact]
    public void Save_CorrectSha_WritesFile()
    {
        byte[] content = Content();
        _store.Save("a.wav", content, Sha(content), "A");
        Assert.True(_store.Contains("a.wav", content.Length));
    }

    [Theory]
    [InlineData("../evil.wav")]
    [InlineData("..\\evil.wav")]
    [InlineData(".hidden.wav")]
    [InlineData("sub/dir.wav")]
    [InlineData("sans-extension")]
    [InlineData("notes.txt")]
    public void Save_UnsafeName_Throws(string fileName)
    {
        byte[] content = Content();
        Assert.Throws<ArgumentException>(
            () => _store.Save(fileName, content, Sha(content), "X"));
    }

    [Fact]
    public void Save_EmptyContent_Throws()
    {
        Assert.Throws<ArgumentException>(() => _store.Save("a.wav", Array.Empty<byte>(), Sha(Content()), "A"));
    }

    [Fact]
    public void Contains_SizeMismatch_ReturnsFalse()
    {
        byte[] content = Content();
        _store.Save("a.wav", content, Sha(content), "A");
        Assert.False(_store.Contains("a.wav", content.Length + 1));
        Assert.False(_store.Contains("absent.wav", 10));
    }

    [Fact]
    public void Save_SameFileTwice_DoesNotDuplicate()
    {
        byte[] content = Content();
        _store.Save("a.wav", content, Sha(content), "Premier");
        _store.Save("a.wav", content, Sha(content), "Second");

        Assert.Single(_store.List());
        Assert.Equal("Second", _store.List()[0].DisplayName); // l'index garde le dernier libellé
    }

    [Fact]
    public void List_CorruptedIndex_StillListsFilesWithFallbackName()
    {
        // le store range tout dans un sous-dossier dédié : on corrompt SON index
        // et on dépose un WAV directement dans SON dossier
        File.WriteAllText(Path.Combine(_store.DirectoryPath, "index.json"), "{ ceci n'est pas du json");
        File.WriteAllBytes(Path.Combine(_store.DirectoryPath, "bruit-gris-10s.wav"), Content());

        var list = _store.List();
        Assert.Single(list);
        Assert.Equal("bruit-gris-10s", list[0].DisplayName); // repli : nom de fichier sans extension
    }

    [Fact]
    public void List_EmptyStore_ReturnsEmpty()
    {
        Assert.Empty(_store.List());
    }

    [Fact]
    public void List_OrdersFilesByName()
    {
        byte[] content = Content();
        _store.Save("c.wav", content, Sha(content), "C");
        _store.Save("a.wav", content, Sha(content), "A");
        _store.Save("b.wav", content, Sha(content), "B");

        Assert.Equal(new[] { "a.wav", "b.wav", "c.wav" }, _store.List().Select(f => f.FileName).ToArray());
    }
}
