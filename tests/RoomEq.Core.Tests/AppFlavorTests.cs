using RoomEq.Core.Io;
using Xunit;

namespace RoomEq.Core.Tests;

/// <summary>
/// Détection de la version de l'exécutable (test / public) par son nom de fichier.
/// Règle v2.0 (inversée, silencieuse par défaut) : tout nom contenant « test »
/// = version de test (journalisation) ; tout le reste — y compris le « RoomEq.exe »
/// installé — est public (aucun journal, aucun envoi). La variable
/// d'environnement ROOMEQ_TEST=1 force la version test (développement).
/// </summary>
public class AppFlavorTests
{
    [Theory]
    [InlineData("RoomEq.exe")]                        // nom d'installation : public
    [InlineData("roomeq.exe")]                        // casse indifférente
    [InlineData("C:\\Program Files\\RoomEq\\RoomEq.exe")] // chemin complet
    [InlineData("RoomEq.App.exe")]                    // exécutable de développement
    [InlineData("RoomEq")]                            // sans extension
    [InlineData("mon-application.exe")]
    public void FromExecutableName_PlainNames_ArePublicFlavor(string name)
    {
        Assert.Equal(AppFlavorKind.Public, AppFlavor.FromExecutableName(name));
    }

    [Theory]
    [InlineData("RoomEq-test.exe")]
    [InlineData("ROOMEQ-TEST.exe")]
    [InlineData("RoomEq-test")]
    [InlineData("C:\\Temp\\roomeq-test.exe")]
    [InlineData("appli-TESTEUR.exe")]                 // la sous-chaîne suffit
    public void FromExecutableName_TestNames_AreTestFlavor(string name)
    {
        Assert.Equal(AppFlavorKind.Test, AppFlavor.FromExecutableName(name));
    }

    [Fact]
    public void DisplayName_MatchesCurrentFlavor()
    {
        Assert.Equal(AppFlavor.IsPublic ? "public" : "test", AppFlavor.DisplayName);
    }

    [Fact]
    public void PublicExePath_IsSiblingExecutableName()
    {
        Assert.EndsWith("RoomEq.exe", AppFlavor.PublicExePath, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void CurrentFlavor_DerivesFromOwnExecutableName()
    {
        // cette suite tourne sous « testhost » : le nom contient « test » → version test
        Assert.Equal(
            AppFlavor.FromExecutableName(Environment.ProcessPath ?? ""),
            AppFlavor.Current);
    }
}
