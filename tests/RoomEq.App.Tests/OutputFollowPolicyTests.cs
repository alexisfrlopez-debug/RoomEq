using RoomEq.App.ViewModels;
using Xunit;

namespace RoomEq.App.Tests;

/// <summary>
/// Politique de suivi de la sortie PAR DÉFAUT de Windows (v3.9.7 — correctif
/// « la modification du volume Windows n'a aucun effet sur la barre de
/// volume de l'appli, qui elle ne modifie pas le volume entendu ») :
/// l'écoute (volume physique + preset) reste branchée sur une sortie fixe
/// pendant que l'utilisateur écoute le défaut Windows (casque JBL devenu
/// défaut, appli restée sur les haut-parleurs Realtek).
/// </summary>
public class OutputFollowPolicyTests
{
    [Fact]
    public void Diverges_OnlyOnDeliberateNonDefaultChoice()
    {
        Assert.True(OutputFollowPolicy.Diverges("wasapi|speakers", "wasapi|headphones"));
        Assert.False(OutputFollowPolicy.Diverges("wasapi|headphones", "wasapi|headphones"));
        // défaut inconnu (hors Windows / indisponible) : jamais de divergence
        Assert.False(OutputFollowPolicy.Diverges("wasapi|speakers", null));
        Assert.False(OutputFollowPolicy.Diverges(null, null));
    }

    [Fact]
    public void ShouldFollow_NewDefault_WhenNotDiverged()
    {
        // le défaut change (casque branché) et l'utilisateur n'a pas divergé :
        // l'écoute suit
        Assert.True(OutputFollowPolicy.ShouldFollow(
            "wasapi|speakers", "wasapi|headphones", diverged: false));
    }

    [Fact]
    public void ShouldFollow_Stays_WhenUserDiverged()
    {
        // choix délibéré d'une autre sortie : le nouveau défaut ne l'écrase pas
        Assert.False(OutputFollowPolicy.ShouldFollow(
            "wasapi|speakers", "wasapi|headphones", diverged: true));
    }

    [Fact]
    public void ShouldFollow_NoOp_WhenAlreadyOnDefaultOrUnknownDefault()
    {
        Assert.False(OutputFollowPolicy.ShouldFollow(
            "wasapi|headphones", "wasapi|headphones", diverged: false));
        Assert.False(OutputFollowPolicy.ShouldFollow(
            "wasapi|speakers", null, diverged: false));
    }

    [Fact]
    public void ShouldFollow_SelectsDefault_WhenNothingSelected()
    {
        // rien de sélectionné et un défaut connu : l'écoute le prend
        Assert.True(OutputFollowPolicy.ShouldFollow(
            null, "wasapi|headphones", diverged: false));
    }
}
