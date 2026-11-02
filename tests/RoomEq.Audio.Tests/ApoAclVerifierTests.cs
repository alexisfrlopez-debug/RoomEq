using System.Security.AccessControl;
using System.Security.Principal;
using RoomEq.Audio.SystemApo;
using Xunit;

namespace RoomEq.Audio.Tests;

/// <summary>
/// Verrou du vérificateur d'ACL du dossier partagé APO (correctif v3.9.5) :
/// icacls « meilleur effort » pouvait échouer en silence → l'app ne pouvait
/// plus écrire active.json → EQ activé mais muet. La construction d'objets
/// ACL (SecurityPrincipal Windows) n'existe que sur Windows : hors Windows
/// les tests sont inertes (garde EN LIGNE dans chaque accès — forme reconnue
/// par l'analyseur CA1416, build 0 avertissement), la lecture réelle est
/// verrouillée par le test du dossier inexistant.
/// </summary>
public class ApoAclVerifierTests
{
    private static SecurityIdentifier? UsersSid()
        => OperatingSystem.IsWindows() ? new(WellKnownSidType.BuiltinUsersSid, null) : null;

    private static AuthorizationRuleCollection? Rules(params FileSystemAccessRule[] rules)
    {
        if (!OperatingSystem.IsWindows())
            return null; // jamais atteint : les tests appelants retournent avant
        var security = new DirectorySecurity();
        foreach (var r in rules)
            security.AddAccessRule(r);
        return security.GetAccessRules(true, true, typeof(SecurityIdentifier));
    }

    [Fact]
    public void UtilisateursAvecModifier_EstConforme()
    {
        if (!OperatingSystem.IsWindows()) return; // objets ACL : Windows uniquement

        var rules = Rules(new FileSystemAccessRule(
            UsersSid()!, FileSystemRights.Modify, AccessControlType.Allow));
        Assert.NotNull(rules);
        Assert.True(ApoAclVerifier.UsersCanModify(rules!));
    }

    [Fact]
    public void UtilisateursAvecFullControl_EstConforme()
    {
        if (!OperatingSystem.IsWindows()) return;

        var rules = Rules(new FileSystemAccessRule(
            UsersSid()!, FileSystemRights.FullControl, AccessControlType.Allow));
        Assert.NotNull(rules);
        Assert.True(ApoAclVerifier.UsersCanModify(rules!));
    }

    [Fact]
    public void UtilisateursLectureSeule_EstRefuse()
    {
        if (!OperatingSystem.IsWindows()) return;

        // le cas v3.9.4 : icacls a raté, il ne reste que SYSTEM/Admins —
        // l'app ne peut pas écrire la courbe → refus explicite à l'activation
        var admins = new SecurityIdentifier(WellKnownSidType.BuiltinAdministratorsSid, null);
        var rules = Rules(
            new FileSystemAccessRule(admins, FileSystemRights.FullControl, AccessControlType.Allow),
            new FileSystemAccessRule(UsersSid()!, FileSystemRights.ReadAndExecute, AccessControlType.Allow));
        Assert.NotNull(rules);
        Assert.False(ApoAclVerifier.UsersCanModify(rules!));
    }

    [Fact]
    public void AucuneRegleUtilisateurs_EstRefuse()
    {
        if (!OperatingSystem.IsWindows()) return;

        var admins = new SecurityIdentifier(WellKnownSidType.BuiltinAdministratorsSid, null);
        var rules = Rules(new FileSystemAccessRule(
            admins, FileSystemRights.FullControl, AccessControlType.Allow));
        Assert.NotNull(rules);
        Assert.False(ApoAclVerifier.UsersCanModify(rules!));
    }

    [Fact]
    public void DossierInexistantOuIllisible_EstRefuseSansException()
    {
        // multiplateforme : hors Windows la lecture ACL réelle n'existe pas —
        // le vérificateur répond false (jamais d'exception, jamais de
        // « silence » : refus explicite plutôt qu'un EQ muet). L'appel est
        // VOLONTAIREMENT multiplateforme : il verrouille le contrat « inerte
        // hors Windows » du vérificateur (CA1416 supprimé pour ce seul appel).
#pragma warning disable CA1416 // contrat multiplateforme testé exprès
        Assert.False(ApoAclVerifier.UsersCanModify("/roomeq-inexistant-" + Guid.NewGuid().ToString("N")));
#pragma warning restore CA1416
    }
}
