using RoomEq.Audio.SystemApo;
using Xunit;

namespace RoomEq.Audio.Tests;

/// <summary>
/// Arguments icacls des dossiers partagés APO (v3.9.7 — correctif « plus de
/// son sur la sortie où l'EQ est activé ») : verrous PUREMENT textuels qui
/// garantissent que les identités de service ne peuvent PLUS être exclues
/// de la lecture des DLL (bin : Tout le monde) ni de l'écriture du journal
/// (racine : service audio par nom ET LocalService par SID).
/// </summary>
public class ApoAclArgsTests
{
    [Fact]
    public void Bin_GrantsEveryoneReadAndExecute()
    {
        string args = ApoAclArgs.ForBin(@"C:\ProgramData\RoomEq\apo\bin");

        // lecture + exécution pour TOUT LE MONDE (S-1-1-0) : aucune identité
        // de service (audiodg) ne peut plus se voir refuser le chargement —
        // le mécanisme exact du retour terrain v3.9.6
        Assert.Contains(ApoAclArgs.EveryoneSid + ":(OI)(CI)RX", args);
        // SYSTEM et administrateurs gardent tout pouvoir
        Assert.Contains(ApoAclArgs.SystemSid + ":(OI)(CI)F", args);
        Assert.Contains(ApoAclArgs.AdministratorsSid + ":(OI)(CI)F", args);
        // ACL protégée contre l'héritage
        Assert.Contains("/inheritance:r", args);
        // le chemin ciblé est bin
        Assert.StartsWith("\"C:\\ProgramData\\RoomEq\\apo\\bin\"", args);
        // jamais de droit d'écriture aux non-admins sur les DLL
        Assert.DoesNotContain(ApoAclArgs.EveryoneSid + ":(OI)(CI)F", args);
        Assert.DoesNotContain(ApoAclArgs.EveryoneSid + ":(OI)(CI)M", args);
        Assert.DoesNotContain(ApoAclArgs.UsersSid + ":(OI)(CI)M", args);
    }

    [Fact]
    public void Root_GrantsUsersAudiosrvAndLocalServiceModify()
    {
        string args = ApoAclArgs.ForRoot(@"C:\ProgramData\RoomEq\apo");

        // l'application (Utilisateurs) écrit la courbe
        Assert.Contains(ApoAclArgs.UsersSid + ":(OI)(CI)M", args);
        // audiodg écrit son journal : par le NOM du service audio…
        Assert.Contains("NT SERVICE\\Audiosrv:(OI)(CI)M", args);
        // …ET par le SID LOCAL SERVICE (S-1-5-19) — indépendant de la langue
        // et de la résolution de nom, c'est l'identité documentée d'audiodg
        Assert.Contains(ApoAclArgs.LocalServiceSid + ":(OI)(CI)M", args);
        // SYSTEM et administrateurs
        Assert.Contains(ApoAclArgs.SystemSid + ":(OI)(CI)F", args);
        Assert.Contains(ApoAclArgs.AdministratorsSid + ":(OI)(CI)F", args);
        // ACL protégée
        Assert.Contains("/inheritance:r", args);
    }

    [Fact]
    public void Sids_AreTheDocumentedWellKnownValues()
    {
        // verrou anti-typo sur les SID codés en dur
        Assert.Equal("*S-1-1-0", ApoAclArgs.EveryoneSid);
        Assert.Equal("*S-1-5-19", ApoAclArgs.LocalServiceSid);
        Assert.Equal("*S-1-5-18", ApoAclArgs.SystemSid);
        Assert.Equal("*S-1-5-32-544", ApoAclArgs.AdministratorsSid);
        Assert.Equal("*S-1-5-32-545", ApoAclArgs.UsersSid);
    }
}

/// <summary>
/// Politique PURE du verdict d'activation (v3.9.7) : croise sonde WASAPI ×
/// trace de chargement — la décision de ROLLBACK AUTOMATIQUE (« le son passe
/// avant l'égaliseur ») est verrouillée ici.
/// </summary>
public class ApoActivationPolicyTests
{
    [Theory]
    [InlineData(ApoProbeResult.Alive, true, ApoActivationHealth.Healthy)]
    [InlineData(ApoProbeResult.Alive, false, ApoActivationHealth.AliveNoLog)]
    [InlineData(ApoProbeResult.DeviceAbsent, true, ApoActivationHealth.AliveNoLog)]
    [InlineData(ApoProbeResult.DeviceAbsent, false, ApoActivationHealth.AliveNoLog)]
    [InlineData(ApoProbeResult.DeadGraph, true, ApoActivationHealth.DeadEndpoint)]
    [InlineData(ApoProbeResult.DeadGraph, false, ApoActivationHealth.DeadEndpoint)]
    public void Evaluate_CrossesProbeAndLog(ApoProbeResult probe, bool log,
        ApoActivationHealth expected)
    {
        Assert.Equal(expected, ApoActivationPolicy.Evaluate(probe, log));
    }

    [Fact]
    public void DeadEndpoint_OnlyFromDeadGraph()
    {
        // une sortie DÉBRANCHÉE ne déclenche JAMAIS de rollback : ce n'est
        // pas l'EQ qui l'a coupée
        foreach (ApoProbeResult probe in new[] { ApoProbeResult.Alive, ApoProbeResult.DeviceAbsent })
            foreach (bool log in new[] { true, false })
                Assert.NotEqual(ApoActivationHealth.DeadEndpoint,
                    ApoActivationPolicy.Evaluate(probe, log));
    }

    [Theory]
    [InlineData(1, 4, false, true)]   // témoin mort aussi : on retente (service qui revient)
    [InlineData(3, 4, false, true)]   // il reste des tentatives
    [InlineData(4, 4, false, false)]  // budget épuisé
    [InlineData(1, 4, true, false)]   // témoin VIVANT et elle morte : verdict immédiat
    public void ShouldRetryProbe_RespectsBudgetAndWitness(int attempts, int max, bool witness, bool expected)
    {
        Assert.Equal(expected, ApoActivationPolicy.ShouldRetryProbe(attempts, max, witness));
    }
}
