using System.Linq;
using RoomEq.Core.Io;
using Xunit;

namespace RoomEq.Core.Tests;

/// <summary>
/// Tests du manifeste des signaux téléchargeables et des règles d'URL :
/// analyse JSON, règles de sécurité (nom de fichier, tailles, SHA-256,
/// doublons) et verrouillage du domaine de téléchargement.
/// </summary>
public class SignalPackTests
{
    private static string ValidEntry(
        string id = "impulsion-dirac",
        string displayName = "Impulsion (Dirac)",
        string fileName = "impulsion-dirac.wav",
        long bytes = 88_244,
        string sha = "a3f8c9d0e1b2a3f8c9d0e1b2a3f8c9d0e1b2a3f8c9d0e1b2a3f8c9d0e1b2a3f8")
        => $$"""{"id":"{{id}}","displayName":"{{displayName}}","description":"Cinq impulsions.","fileName":"{{fileName}}","bytes":{{bytes}},"sha256":"{{sha}}"}""";

    private static string Manifest(params string[] entries)
        => """{"version":1,"signals":[""" + string.Join(",", entries) + "]}";

    // ------------------------------------------------------------------ parse OK

    [Fact]
    public void Parse_ValidManifest_ReturnsItems()
    {
        bool ok = SignalManifest.TryParse(
            Manifest(ValidEntry(), ValidEntry(id: "bruit-gris", fileName: "bruit-gris-10s.wav", bytes: 882_044)),
            out var signals, out var error);

        Assert.True(ok, error);
        Assert.Equal(2, signals.Count);
        Assert.Equal("impulsion-dirac", signals[0].Id);
        Assert.Equal("Impulsion (Dirac)", signals[0].DisplayName);
        Assert.Equal("impulsion-dirac.wav", signals[0].FileName);
        Assert.Equal(88_244, signals[0].Bytes);
        Assert.Equal(64, signals[0].Sha256.Length);
    }

    [Fact]
    public void Parse_EmptyDescription_IsAccepted()
    {
        bool ok = SignalManifest.TryParse(
            """{"signals":[{"id":"a","displayName":"A","description":"","fileName":"a.wav","bytes":10,"sha256":"a3f8c9d0e1b2a3f8c9d0e1b2a3f8c9d0e1b2a3f8c9d0e1b2a3f8c9d0e1b2a3f8"}]}""",
            out var signals, out var error);

        Assert.True(ok, error);
        Assert.Equal("", signals[0].Description);
    }

    // ------------------------------------------------------------------ erreurs

    [Theory]
    [InlineData("pas du json")]
    [InlineData("{}")]
    [InlineData("{\"signals\":[]}")]
    [InlineData("{\"signals\":{\"id\":\"a\"}}")]
    [InlineData("{\"signals\":[1,2]}")]
    public void Parse_InvalidJsonOrStructure_Fails(string json)
    {
        bool ok = SignalManifest.TryParse(json, out _, out var error);
        Assert.False(ok);
        Assert.False(string.IsNullOrWhiteSpace(error));
    }

    [Fact]
    public void Parse_PathTraversalFileName_Rejected()
    {
        bool ok = SignalManifest.TryParse(
            Manifest(ValidEntry(fileName: "../secret.wav")), out _, out var error);
        Assert.False(ok);
        Assert.Contains("fichier", error);
    }

    [Fact]
    public void Parse_NonWavExtension_Rejected()
    {
        bool ok = SignalManifest.TryParse(
            Manifest(ValidEntry(fileName: "install.exe")), out _, out _);
        Assert.False(ok);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-5)]
    [InlineData(25_000_001)]
    public void Parse_BadSize_Rejected(long bytes)
    {
        bool ok = SignalManifest.TryParse(Manifest(ValidEntry(bytes: bytes)), out _, out _);
        Assert.False(ok);
    }

    [Fact]
    public void Parse_BadSha256_Rejected()
    {
        bool ok = SignalManifest.TryParse(
            Manifest(ValidEntry(sha: "ZZZZ")), out _, out _);
        Assert.False(ok);
    }

    [Fact]
    public void Parse_DuplicateId_Rejected()
    {
        bool ok = SignalManifest.TryParse(
            Manifest(ValidEntry(), ValidEntry(fileName: "autre.wav")), out _, out var error);
        Assert.False(ok);
        Assert.Contains("doublon", error);
    }

    [Fact]
    public void Parse_DuplicateFileName_Rejected()
    {
        bool ok = SignalManifest.TryParse(
            Manifest(ValidEntry(), ValidEntry(id: "autre-signal")), out _, out var error);
        Assert.False(ok);
        Assert.Contains("doublon", error);
    }

    [Fact]
    public void Parse_BadId_Rejected()
    {
        bool ok = SignalManifest.TryParse(
            Manifest(ValidEntry(id: "ID INVALIDÉ !")), out _, out _);
        Assert.False(ok);
    }

    [Fact]
    public void Parse_TooManySignals_Rejected()
    {
        var entries = Enumerable.Range(0, 51)
            .Select(i => ValidEntry(id: $"signal-{i:00}", fileName: $"signal-{i:00}.wav"))
            .ToArray();
        bool ok = SignalManifest.TryParse(Manifest(entries), out _, out _);
        Assert.False(ok);
    }

    // ------------------------------------------------------------------ URLs

    [Fact]
    public void FileUrl_IsWithinAllowedDirectory()
    {
        string url = SignalUrls.FileUrl("impulsion-dirac.wav");
        Assert.True(SignalUrls.IsAllowedFileUrl(url));
        Assert.StartsWith("https://raw.githubusercontent.com/", url);
        Assert.Contains("/alexisfrlopez-debug/RoomEq/main/signaux/", url);
    }

    [Theory]
    [InlineData("http://raw.githubusercontent.com/alexisfrlopez-debug/RoomEq/signaux/a.wav")] // pas https
    [InlineData("https://evil.example.com/alexisfrlopez-debug/RoomEq/signaux/a.wav")] // autre domaine
    [InlineData("https://raw.githubusercontent.com/autre-org/RoomEq/signaux/a.wav")] // autre dépôt
    [InlineData("https://raw.githubusercontent.com/alexisfrlopez-debug/RoomEq/autres/a.wav")] // autre dossier
    [InlineData("https://raw.githubusercontent.com/alexisfrlopez-debug/RoomEq/signaux/a.wav?x=1")] // paramètre
    [InlineData("https://raw.githubusercontent.com/alexisfrlopez-debug/RoomEq/signaux/../secret.wav")] // traversal
    [InlineData("ftp://raw.githubusercontent.com/alexisfrlopez-debug/RoomEq/signaux/a.wav")]
    [InlineData("texte quelconque")]
    public void IsAllowedFileUrl_ForeignUrls_Rejected(string url)
    {
        Assert.False(SignalUrls.IsAllowedFileUrl(url));
    }

    [Fact]
    public void ManifestUrl_IsAllowed()
    {
        Assert.True(SignalUrls.IsAllowedFileUrl(SignalUrls.ManifestUrl));
    }
}
