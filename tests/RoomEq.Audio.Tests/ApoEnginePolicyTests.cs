using RoomEq.Audio.SystemApo;
using Xunit;

namespace RoomEq.Audio.Tests;

/// <summary>
/// Politique/inscription du moteur audio (v3.9.6 — correctif « la DLL n'a
/// jamais été chargée par le moteur audio ») : les 12 valeurs d'inscription
/// verrouillées une à une (miroir EXACT de ce que la DLL native déclare dans
/// GetRegistrationProperties), la sémantique de la politique « audio protégé »
/// (seule la valeur 1 autorise une DLL non signée) et la sauvegarde/restation
/// de la valeur d'origine (round-trip JSON, fichier absent = ne rien toucher).
/// Logique pure — testable sous Linux.
/// </summary>
public class ApoEnginePolicyTests
{
    // ------------------------------------------------ valeurs d'inscription

    [Fact]
    public void RegistrationValues_MirrorNativeGetRegistrationProperties()
    {
        var values = ApoEnginePolicy.RegistrationValues();

        // les 12 valeurs nommées du sample officiel SysVAD
        // (ComponentizedApoSample.inx) — ni plus, ni moins
        Assert.Equal(new[]
        {
            "FriendlyName", "Copyright", "MajorVersion", "MinorVersion",
            "Flags", "MinInputConnections", "MaxInputConnections",
            "MinOutputConnections", "MaxOutputConnections", "MaxInstances",
            "NumAPOInterfaces", "APOInterface0",
        }, values.Select(v => v.Name));

        // champs texte : identiques à RoomEqApo::GetRegistrationProperties
        Assert.Equal("RoomEq - egaliseur systeme",
            values.Single(v => v.Name == "FriendlyName").Text);
        Assert.Equal("RoomEq", values.Single(v => v.Name == "Copyright").Text);
        // versions 1.0
        Assert.Equal(1, values.Single(v => v.Name == "MajorVersion").Dword);
        Assert.Equal(0, values.Single(v => v.Name == "MinorVersion").Dword);
        // APO_FLAG_DEFAULT | APO_FLAG_INPLACE = 0x2|0x4|0x8|0x1 = 15
        Assert.Equal(ApoEnginePolicy.RegFlags,
            values.Single(v => v.Name == "Flags").Dword);
        Assert.Equal(15, ApoEnginePolicy.RegFlags);
        // connexions 1/1/1/1 et instances max (miroir du natif : 64)
        Assert.Equal(1, values.Single(v => v.Name == "MinInputConnections").Dword);
        Assert.Equal(1, values.Single(v => v.Name == "MaxInputConnections").Dword);
        Assert.Equal(1, values.Single(v => v.Name == "MinOutputConnections").Dword);
        Assert.Equal(1, values.Single(v => v.Name == "MaxOutputConnections").Dword);
        Assert.Equal(64, values.Single(v => v.Name == "MaxInstances").Dword);
        // une seule interface listée — v3.9.7 : IAudioProcessingObject, comme
        // les DEUX références terrain (SysVAD écrit {FD7F2B29-…} à
        // APOInterface0 ; Equalizer APO passe __uuidof(IAudioProcessingObject)
        // à CRegAPOProperties) ; la RT reste déclarée à l'objet par QueryInterface.
        Assert.Equal(1, values.Single(v => v.Name == "NumAPOInterfaces").Dword);
        Assert.Equal(ApoEnginePolicy.ApoInterface0,
            values.Single(v => v.Name == "APOInterface0").Text);
        // IID de IAudioProcessingObject (audioenginebaseapo.h) — verrou
        // anti-retour vers la RT : une divergence avec les références serait
        // une régression de la recette de chargement.
        Assert.Equal("{FD7F2B29-24D0-4B5C-B177-592C39F9CA10}",
            ApoEnginePolicy.ApoInterface0);
    }

    [Fact]
    public void RegistrationValues_KindsMatchSysvadInf()
    {
        var values = ApoEnginePolicy.RegistrationValues();

        // le sample SysVAD écrit FriendlyName/Copyright/APOInterface0 en
        // REG_SZ (pas de flag 0x00010001) et tout le reste en REG_DWORD
        foreach (var v in values)
        {
            bool text = v.Name is "FriendlyName" or "Copyright" or "APOInterface0";
            Assert.True(text ? v.Dword == null && v.Text != null
                             : v.Dword != null && v.Text == null,
                "type (chaîne/DWORD) inattendu pour " + v.Name);
        }
    }

    // ------------------------------------------------ politique audio protégé

    [Theory]
    [InlineData(null, false)] // absente : le moteur protégé refuse la DLL
    [InlineData(0, false)]    // explicitement fermée
    [InlineData(2, false)]    // valeur parasite ≠ 1
    [InlineData(1, true)]     // le seul état qui autorise une DLL non signée
    public void IsDllLoadAllowed_OnlyWhenExactlyOne(int? current, bool expected)
        => Assert.Equal(expected, ApoEnginePolicy.IsDllLoadAllowed(current));

    // ------------------------------------------------ sauvegarde de la politique

    [Fact]
    public void Backup_RoundTrip_PreservesAbsentAndPresentOriginals()
    {
        string dir = Path.Combine(Path.GetTempPath(), "roomeq-apo-policy-" + Guid.NewGuid().ToString("N"));
        try
        {
            string path = Path.Combine(dir, "audio-policy.json");
            Assert.Null(ApoEnginePolicy.LoadBackup(path)); // jamais modifié → ne rien toucher

            ApoEnginePolicy.SaveBackup(path, null); // l'origine était ABSENTE
            var loaded = ApoEnginePolicy.LoadBackup(path);
            Assert.NotNull(loaded);
            Assert.Null(loaded!.Original);

            ApoEnginePolicy.SaveBackup(path, 0); // l'origine était 0
            loaded = ApoEnginePolicy.LoadBackup(path);
            Assert.NotNull(loaded);
            Assert.Equal(0, loaded!.Original);

            ApoEnginePolicy.DeleteBackup(path);
            Assert.Null(ApoEnginePolicy.LoadBackup(path)); // consommée
        }
        finally
        {
            try { Directory.Delete(dir, recursive: true); }
            catch { /* meilleur effort */ }
        }
    }

    [Fact]
    public void LoadBackup_CorruptFile_ActsAsAbsent()
    {
        string dir = Path.Combine(Path.GetTempPath(), "roomeq-apo-policy-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        try
        {
            string path = Path.Combine(dir, "audio-policy.json");
            File.WriteAllText(path, "{{{ pas du json");
            Assert.Null(ApoEnginePolicy.LoadBackup(path)); // illisible = absent, jamais d'exception
        }
        finally
        {
            Directory.Delete(dir, recursive: true);
        }
    }

    // ------------------------------------------------ clés registre verrouillées

    [Fact]
    public void Constants_AreTheFieldProvenRecipe()
    {
        // FxProperties : position EFX et modes de traitement EFX (notes
        // É. Dechamps + Equalizer APO DeviceAPOInfo.cpp)
        Assert.Equal("{d04e05a6-594b-4fb6-a80d-01af5eed7d1d},7",
            ApoConstants.FxEndpointEffectClsid);
        Assert.Equal("{d3993a3f-99c2-4402-b5ec-a92a0367664b},7",
            ApoConstants.FxEfxProcessingModes);
        Assert.Equal("{C18E2F7E-933D-4965-B7D1-1EEF228D2AF3}",
            ApoConstants.DefaultProcessingMode);
        // politique du moteur audio (doc développeur Equalizer APO)
        Assert.Equal(@"SOFTWARE\Microsoft\Windows\CurrentVersion\Audio",
            ApoConstants.AudioPolicyKey);
        Assert.Equal("DisableProtectedAudioDG", ApoConstants.DisableProtectedAudioDgValue);
        // inscription moteur : AudioEngine\AudioProcessingObjects\{notre CLSID}
        Assert.EndsWith(ApoConstants.Clsid, ApoConstants.EngineApoRegistrationKey);
        Assert.Contains(@"AudioEngine\AudioProcessingObjects\", ApoConstants.EngineApoRegistrationKey);
    }
}
