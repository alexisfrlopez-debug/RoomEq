using RoomEq.Core.Io;
using Xunit;

namespace RoomEq.Core.Tests;

/// <summary>
/// Détection de la version de l'exécutable (test / public) par son nom de fichier.
/// Règle : tout nom contenant « public » = version publique ; tout le reste
/// (RoomEq-test.exe, RoomEq.App.exe de développement, cas imprévus) = version test.
/// </summary>
public class AppFlavorTests
{
    [Theory]
    [InlineData("RoomEq-public.exe")]
    [InlineData("roomeq-public.exe")]               // casse indifférente
    [InlineData("RoomEq-PUBLIC.exe")]
    [InlineData("C:\\Applications\\RoomEq\\RoomEq-public.exe")] // chemin complet
    [InlineData("RoomEq-public")]                   // sans extension
    [InlineData("mon-appli-roomeq-publicaire.exe")] // la sous-chaîne suffit
    public void FromExecutableName_PublicNames_ArePublicFlavor(string name)
    {
        Assert.Equal(AppFlavorKind.Public, AppFlavor.FromExecutableName(name));
    }

    [Theory]
    [InlineData("RoomEq-test.exe")]
    [InlineData("RoomEq.App.exe")]                  // exécutable de développement
    [InlineData("RoomEq.exe")]                      // nom neutre : comportement historique
    [InlineData("C:\\Applications\\RoomEq\\RoomEq-test.exe")]
    [InlineData("RoomEq-test")]                     // « test » n'est PAS « public »
    [InlineData("")]                                // vide : prudence → test
    public void FromExecutableName_OtherNames_AreTestFlavor(string name)
    {
        Assert.Equal(AppFlavorKind.Test, AppFlavor.FromExecutableName(name));
    }

    [Fact]
    public void DisplayName_MatchesCurrentFlavor()
    {
        Assert.Equal(AppFlavor.IsPublic ? "public" : "test", AppFlavor.DisplayName);
        Assert.Equal(AppFlavor.IsPublic, !AppFlavor.IsTest);
    }

    [Fact]
    public void PublicExePath_IsSiblingExecutableName()
    {
        Assert.EndsWith("RoomEq-public.exe", AppFlavor.PublicExePath, StringComparison.OrdinalIgnoreCase);
        // le chemin est absolu et pointe vers le répertoire de l'exécutable courant
        Assert.True(Path.IsPathRooted(AppFlavor.PublicExePath));
    }
}
