using RoomEq.App.ViewModels;
using RoomEq.Audio.Playback;
using RoomEq.Audio.SystemApo;
using Xunit;

namespace RoomEq.App.Tests;

/// <summary>
/// Onglet Mixage après le retrait de la console (spec I-APP-03 v3, D26) :
/// l'onglet est le poste de pilotage de l'EQ système — sortie d'écoute qui
/// suit la barre du haut, verdict des actions APO qui ne disparaît JAMAIS
/// au rafraîchissement, persistance réduite de mixer.json qui continue
/// d'avaler les configurations des versions antérieures, et garde
/// d'instance unique (fin de l'installation qui ouvrait l'appli plusieurs
/// fois).
/// </summary>
public class MixerSystemTabTests : IDisposable
{
    private readonly string _dir;

    public MixerSystemTabTests()
    {
        _dir = Path.Combine(Path.GetTempPath(), "roomeq-mixer-d26-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_dir);
    }

    public void Dispose()
    {
        try { Directory.Delete(_dir, true); } catch { /* temp */ }
    }

    private static MixerViewModel CreateVm(string? dir = null)
        => new(null, null,
               dir == null ? null : new RoomEq.Audio.Routing.MixerStore(dir));

    // ============================================================ sortie d'écoute

    [Fact]
    public void FollowTopBarOutput_UpdatesSelectionAndNotifies()
    {
        var vm = CreateVm();

        OutputDeviceInfo? notified = null;
        vm.MasterOutputChanged += d => notified = d;

        var speakers = new OutputDeviceInfo("wasapi|spk", "Enceintes", "WASAPI");
        vm.FollowTopBarOutput(speakers);

        Assert.Equal("wasapi|spk", vm.SelectedOutput?.Id);
        Assert.Equal("wasapi|spk", notified?.Id);

        // null ou identique : aucun effet, pas d'événement parasites
        int fired = 0;
        vm.MasterOutputChanged += _ => fired++;
        vm.FollowTopBarOutput(null);
        vm.FollowTopBarOutput(speakers);
        Assert.Equal(0, fired);
    }

    [Fact]
    public void PreferredInitialOutput_ReturnsMemorizedOutputOnce()
    {
        File.WriteAllText(Path.Combine(_dir, "mixer.json"), """
            {
              "MasterOutputId": "wasapi|old",
              "AutoOutputPreset": false,
              "SavedDefaultRenderId": "render-1",
              "SavedDefaultCaptureId": "capture-1",
              "Channels": [
                { "Kind": "capture", "DeviceId": "mic", "Name": "Micro" },
                { "Kind": "player", "DeviceId": "", "Name": "Lecteur" },
                { "Kind": "loopback", "DeviceId": "spk", "Name": "Sortie" }
              ]
            }
            """);
        var vm = CreateVm(_dir);

        // la configuration héritée (tranches, protocoles, routage) est
        // chargée sans exception ; l'option conservée est restaurée
        Assert.False(vm.AutoOutputPreset);

        var outputs = new[]
        {
            new OutputDeviceInfo("wasapi|new", "Nouvelle carte", "WASAPI"),
            new OutputDeviceInfo("wasapi|old", "Ancienne carte", "WASAPI"),
        };

        var preferred = vm.PreferredInitialOutput(outputs);
        Assert.Equal("wasapi|old", preferred?.Id); // la sortie mémorisée est proposée
        Assert.Null(vm.PreferredInitialOutput(outputs)); // …une seule fois
    }

    [Fact]
    public void SaveConfig_WritesReducedState_NoConsoleLeftovers()
    {
        var vm = CreateVm(_dir);
        vm.FollowTopBarOutput(new OutputDeviceInfo("wasapi|spk", "Enceintes", "WASAPI"));
        vm.AutoOutputPreset = false;
        vm.SaveConfig();

        string json = File.ReadAllText(Path.Combine(_dir, "mixer.json"));
        Assert.Contains("wasapi|spk", json);
        Assert.Contains("\"AutoOutputPreset\": false", json);

        // D26 : plus AUCUNE tranche persistée — le tableau est vide et aucun
        // contenu de console ne réapparaît au rechargement
        var reloaded = new RoomEq.Audio.Routing.MixerStore(_dir).Load();
        Assert.Empty(reloaded.Channels);
        Assert.Equal("wasapi|spk", reloaded.MasterOutputId);
        Assert.False(reloaded.AutoStart);
    }

    // ============================================================ verdict EQ système

    [Fact]
    public void SystemApoVerdict_SurvivesRefreshAndFlagsErrors()
    {
        var vm = CreateVm();

        // simulons le verdict posé par une activation qui a échoué
        vm.SystemApoVerdictText = "✗ Activation de « Casque » — Élévation refusée.";
        vm.SystemApoVerdictIsError = true;
        Assert.True(vm.HasSystemApoVerdict);

        // le rafraîchissement de la liste (fin d'action, branchement à chaud,
        // changement d'onglet…) ne doit JAMAIS effacer la raison d'un échec
        vm.RefreshSystemApoCore();
        Assert.Equal("✗ Activation de « Casque » — Élévation refusée.", vm.SystemApoVerdictText);
        Assert.True(vm.SystemApoVerdictIsError);

        // un verdict vide reste invisible
        vm.SystemApoVerdictText = "";
        Assert.False(vm.HasSystemApoVerdict);
    }

    [Fact]
    public void SystemApo_InertOnLinux_NeverThrows()
    {
        // CI/Linux : le module est inerte — listes vides, aucun état actif,
        // la couverture d'une sortie est toujours fausse (le lecteur interne
        // garde donc son EQ local, aucun contournement abusif)
        using var system = new WindowsApoSystem();
        Assert.False(system.IsReady);
        Assert.False(system.AnyEnabled);
        Assert.Empty(system.EnumerateEndpoints());
        Assert.False(system.CoversEndpoint("wasapi|{0.0.0.00000000}.{11111111-2222-3333-4444-555555555555}"));
        Assert.False(system.CoversEndpoint(null));
        Assert.False(system.CoversEndpoint(""));
    }

    // ============================================================ garde d'instance unique

    [Fact]
    public void SingleInstanceGuard_SecondAcquireReturnsNull()
    {
        string name = "RoomEq.Tests." + Guid.NewGuid().ToString("N");

        using var first = RoomEq.App.SingleInstanceGuard.Acquire(name);
        Assert.NotNull(first);

        // une seconde acquisition (l'installateur a fini, l'appli est déjà
        // lancée) : null → l'instance surnuméraire se ferme immédiatement
        Assert.Null(RoomEq.App.SingleInstanceGuard.Acquire(name));

        first!.Dispose();

        // après la fermeture de la première, une nouvelle instance peut démarrer
        using var second = RoomEq.App.SingleInstanceGuard.Acquire(name);
        Assert.NotNull(second);
    }
}
