using RoomEq.Audio.SystemApo;
using Xunit;

namespace RoomEq.Audio.Tests;

/// <summary>
/// Verrou du diagnostic par maillon de la CHAÎNE de courbe (correctif
/// v3.9.5 — « l'EQ est activé et chargé mais je n'entends pas de
/// différence »). Après cette version, le panneau EQ système nomme le
/// maillon défaillant : courbe jamais écrite / écriture refusée /
/// bypass actif / courbe plate / APO qui ne publie rien / chaîne
/// complète. Composeur PUR — exécutable sous Linux.
/// </summary>
public class ApoChainTextTests
{
    private static ApoConfigStore.ActiveCurve Curve(
        int bandes = 8, bool bypass = false, long revision = 5, double preamp = -2.1)
        => new("Correction", preamp,
            Enumerable.Range(0, bandes)
                .Select(i => new RoomEq.Core.Eq.EqBand(
                    RoomEq.Core.Dsp.FilterType.Peaking, 100 + i * 400, -2.5 + (i % 3), 1.2, true))
                .ToList(),
            bypass, revision);

    [Fact]
    public void ChaineComplete_CourbePublieeALaMemeRevision()
    {
        var text = ApoChainText.Compose(new ApoChainState(
            Curve(revision: 5),
            CurveWriteError: null,
            new[] { "LockForProcess : 48000 Hz, 2 canaux, conteneur 4 octets, float=1",
                    "nouvelle courbe publiee (rev 5, active)" },
            ApoLogLastWriteUtc: DateTime.UtcNow));

        Assert.Contains("Courbe envoyée à l'APO : 8 bandes actives", text);
        Assert.Contains("révision 5", text);
        Assert.Contains("✓ APO : courbe publiée et appliquée par le moteur audio (révision 5)", text);
        Assert.DoesNotContain("⚠", text);
    }

    [Fact]
    public void ErreurDEcriture_EnonceLaCauseEtLaMarcheASuivre()
    {
        var text = ApoChainText.Compose(new ApoChainState(
            Curve(), "Accès au chemin refusé.",
            new[] { "LockForProcess : 48000 Hz, 2 canaux, conteneur 4 octets, float=1" },
            DateTime.UtcNow));

        Assert.Contains("⚠ Écriture de la courbe IMPOSSIBLE : Accès au chemin refusé.", text);
        Assert.Contains("%ProgramData%\\RoomEq\\apo", text);
    }

    [Fact]
    public void CourbeJamaisEcrite_EstExplicite()
    {
        var text = ApoChainText.Compose(new ApoChainState(
            null, null, Array.Empty<string>(), null));

        Assert.Contains("⚠ AUCUNE courbe écrite (active.json absent)", text);
        Assert.Contains("⚠ Journal de l'APO absent", text);
    }

    [Fact]
    public void BypassActif_ExpliqueQueLEstSansEffet()
    {
        var text = ApoChainText.Compose(new ApoChainState(
            Curve(bypass: true), null,
            new[] { "nouvelle courbe publiee (rev 5, bypass)" },
            DateTime.UtcNow));

        Assert.Contains("⚠ Courbe CONTOURNÉE (bypass de l'onglet Égaliseur actif)", text);
        Assert.Contains("décochez « Bypass »", text);
    }

    [Fact]
    public void CourbePlate_SansBandesActives_EstSignalee()
    {
        var plate = new ApoConfigStore.ActiveCurve("Plat", 0.0,
            new List<RoomEq.Core.Eq.EqBand> { new(RoomEq.Core.Dsp.FilterType.Peaking, 1000, 0.0, 1.2, true) },
            false, 3);
        var text = ApoChainText.Compose(new ApoChainState(
            plate, null, new[] { "nouvelle courbe publiee (rev 3, active)" }, DateTime.UtcNow));

        Assert.Contains("⚠ Courbe envoyée mais PLATE", text);
        Assert.Contains("aucune active", text);
    }

    [Fact]
    public void ApoChargeSansPublication_NommeLeProblemeDeLecture()
    {
        var text = ApoChainText.Compose(new ApoChainState(
            Curve(), null,
            new[] { "APO instancie (RoomEqApo)",
                    "LockForProcess : 48000 Hz, 2 canaux, conteneur 4 octets, float=1",
                    "surveillant actif (periode 100 ms)" },
            DateTime.UtcNow));

        Assert.Contains("⚠ APO chargé par le moteur audio mais AUCUNE courbe publiée", text);
        Assert.Contains("active.json", text);
    }

    [Fact]
    public void RevisionEnRetard_DitLaquelleEstAppliquee()
    {
        var text = ApoChainText.Compose(new ApoChainState(
            Curve(revision: 12), null,
            new[] { "nouvelle courbe publiee (rev 9, active)" },
            DateTime.UtcNow));

        Assert.Contains("… APO : il applique encore la révision 9 (dernière envoyée : 12)", text);
    }

    [Fact]
    public void JournalPresentSansCourbeNiPublication_AfficheLaDerniereLigne()
    {
        var text = ApoChainText.Compose(new ApoChainState(
            null, null,
            new[] { "surveillant actif (periode 100 ms)", "APO instancie (RoomEqApo)" },
            DateTime.UtcNow));

        Assert.Contains("APO : journal présent", text);
        Assert.Contains("APO instancie (RoomEqApo)", text);
    }
}
