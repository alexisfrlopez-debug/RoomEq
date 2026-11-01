using RoomEq.Audio.SystemApo;
using Xunit;

namespace RoomEq.Audio.Tests;

/// <summary>
/// Registrar APO (spec I-AUD-09) : logique pure sur faux registre — ordre
/// sauvegarde→écriture, restauration exacte, idempotence, réparation,
/// anti-boucle, inertie hors Windows. Aucune clé réelle n'est touchée.
/// </summary>
public class ApoRegistrarTests
{
    // ------------------------------------------------------------- fakes

    /// <summary>Faux registre d'endpoints avec trace des opérations.</summary>
    private sealed class FakeStore : IApoEndpointStore
    {
        public List<ApoEndpointRecord> Endpoints { get; } = new();

        /// <summary>Clé → (nom de valeur → valeur REG_SZ ou int pour DWORD).</summary>
        public Dictionary<string, Dictionary<string, object>> Values { get; } = new();

        /// <summary>Trace ordonnée : "get:…", "set:…", "del:…".</summary>
        public List<string> Trace { get; } = new();

        public IReadOnlyList<ApoEndpointRecord> EnumerateRenderEndpoints() => Endpoints;

        public string? GetString(string key, string name)
        {
            Trace.Add($"get:{key}/{name}");
            return Values.TryGetValue(key, out var v) && v.TryGetValue(name, out var val)
                ? val as string : null;
        }

        public void SetString(string key, string name, string? value)
        {
            Trace.Add(value == null ? $"del:{key}/{name}" : $"set:{key}/{name}");
            var map = Values.TryGetValue(key, out var v) ? v : Values[key] = new();
            if (value == null)
                map.Remove(name);
            else
                map[name] = value;
        }

        public int? GetInt(string key, string name)
        {
            Trace.Add($"get:{key}/{name}");
            return Values.TryGetValue(key, out var v) && v.TryGetValue(name, out var val)
                ? val as int? : null;
        }

        public void SetInt(string key, string name, int? value)
        {
            Trace.Add(value == null ? $"del:{key}/{name}" : $"set:{key}/{name}");
            var map = Values.TryGetValue(key, out var v) ? v : Values[key] = new();
            if (value == null)
                map.Remove(name);
            else
                map[name] = value.Value;
        }

        public bool FxPropertiesExists(string key)
        {
            Trace.Add($"exists:{key}");
            return Values.ContainsKey(key);
        }

        public IReadOnlyList<string> FxValueNames(string key)
            => Values.TryGetValue(key, out var v) ? v.Keys.ToList() : new List<string>();

        public void DeleteFxProperties(string key)
        {
            Trace.Add($"delkey:{key}");
            Values.Remove(key);
        }
    }

    private sealed class MemoryBackups : IApoBackupStore
    {
        public Dictionary<string, ApoEndpointBackup> Map { get; } = new();

        public ApoEndpointBackup? Load(string key)
            => Map.TryGetValue(key, out var b) ? b : null;

        public void Save(string key, ApoEndpointBackup? backup)
        {
            if (backup == null)
                Map.Remove(key);
            else
                Map[key] = backup;
        }

        public IReadOnlyList<string> Keys() => Map.Keys.ToList();
    }

    private static (ApoRegistrar registrar, FakeStore store, MemoryBackups backups) Make()
    {
        var store = new FakeStore();
        store.Endpoints.Add(new ApoEndpointRecord("{hp}", "Casque Realtek"));
        store.Endpoints.Add(new ApoEndpointRecord("{spk}", "Enceintes (NVIDIA HDMI)"));
        store.Endpoints.Add(new ApoEndpointRecord("{cable}", "RoomEq Input"));
        // v3.9.1 : sortie SANS sous-clé FxProperties (pilote générique sans
        // effets déclarés — le cas terrain qui échouait en v3.7–v3.9)
        store.Endpoints.Add(new ApoEndpointRecord("{bt}", "Haut-parleurs (Realtek(R) Audio)"));
        // valeurs d'origine : un EFX constructeur + effets débloqués
        store.Values["{hp}"] = new()
        {
            [ApoConstants.FxEndpointEffectClsid] = "{realtek-efx}",
        };
        store.Values["{spk}"] = new()
        {
            [ApoConstants.FxEndpointEffectClsid] = "{nvidia-efx}",
            [ApoConstants.FxDisableSysFx] = 1,
        };
        // «{bt}» : volontairement AUCUNE entrée Values → pas de FxProperties
        var backups = new MemoryBackups();
        return (new ApoRegistrar(store, backups), store, backups);
    }

    // ------------------------------------------------------------- activation

    [Fact]
    public void Enable_SavesOriginal_BeforeWriting()
    {
        var (registrar, store, backups) = Make();

        var result = registrar.Enable("{hp}");

        Assert.True(result.Success, result.Message);
        Assert.Equal(ApoConstants.Clsid, store.Values["{hp}"][ApoConstants.FxEndpointEffectClsid]);
        var backup = backups.Load("{hp}");
        Assert.NotNull(backup);
        Assert.Equal("{realtek-efx}", backup.OriginalEndpointEffectClsid); // origine capturée

        // ORDRE (invariant 2) : la LECTURE d'origine précède la première
        // ÉCRITURE de la valeur EFX sur cet endpoint
        int readOrig = store.Trace.IndexOf($"get:{{hp}}/{ApoConstants.FxEndpointEffectClsid}");
        int writeEfx = store.Trace.IndexOf($"set:{{hp}}/{ApoConstants.FxEndpointEffectClsid}");
        Assert.True(readOrig >= 0 && writeEfx > readOrig,
            "la sauvegarde d'origine doit précéder l'écriture du CLSID");
    }

    // ------------------------------------------- v3.9.1 : clé FxProperties créée

    [Fact]
    public void Enable_WithoutFxProperties_CreatesKeyAndSucceeds()
    {
        var (registrar, store, backups) = Make();

        // v3.7–v3.9 : échec « n'expose pas de slot d'effets système » ;
        // v3.9.1 : la clé est CRÉÉE par l'écriture (mécanisme Equalizer APO)
        var result = registrar.Enable("{bt}");

        Assert.True(result.Success, result.Message);
        Assert.Equal(ApoConstants.Clsid, store.Values["{bt}"][ApoConstants.FxEndpointEffectClsid]);
        var backup = backups.Load("{bt}");
        Assert.NotNull(backup);
        Assert.Null(backup.OriginalEndpointEffectClsid); // rien avant nous
        Assert.False(backup.FxPropertiesExisted);        // la clé est de nous

        // ORDRE : l'existence est capturée AVANT la première écriture
        int exists = store.Trace.IndexOf("exists:{bt}");
        int write = store.Trace.IndexOf($"set:{{bt}}/{ApoConstants.FxEndpointEffectClsid}");
        Assert.True(exists >= 0 && write > exists,
            "l'existence de FxProperties doit être capturée avant l'écriture");
    }

    [Fact]
    public void Disable_AfterKeyCreation_RemovesNowEmptyFxProperties()
    {
        var (registrar, store, backups) = Make();
        registrar.Enable("{bt}");

        Assert.True(registrar.Disable("{bt}").Success);

        // restitution exacte : la clé que NOUS avons créée disparaît
        Assert.False(store.Values.ContainsKey("{bt}"));
        Assert.Null(backups.Load("{bt}"));
    }

    [Fact]
    public void Disable_AfterKeyCreation_KeepsKeyHoldingForeignValues()
    {
        var (registrar, store, _) = Make();
        registrar.Enable("{bt}");
        // le pilote (ou le service audio reconstruisant l'endpoint) a écrit
        // sa propre valeur dans la sous-clé créée entre-temps
        store.Values["{bt}"]["{valeur-pilote}"] = "pilote";

        Assert.True(registrar.Disable("{bt}").Success);

        // la clé reste (elle ne contient plus rien à nous)…
        Assert.True(store.Values.ContainsKey("{bt}"));
        Assert.DoesNotContain(ApoConstants.FxEndpointEffectClsid, store.Values["{bt}"].Keys);
        // …et la valeur étrangère est intacte
        Assert.Equal("pilote", store.Values["{bt}"]["{valeur-pilote}"]);
    }

    [Fact]
    public void Disable_LegacyBackupWithoutExistenceFlag_NeverDeletesKey()
    {
        var (registrar, store, backups) = Make();
        registrar.Enable("{bt}");
        // sauvegarde au format v3.7–v3.9 (champ FxPropertiesExisted absent
        // → défaut VRAI par rétrocompatibilité JSON)
        backups.Save("{bt}", new ApoEndpointBackup(null, null));

        Assert.True(registrar.Disable("{bt}").Success);

        // prudence : sans la connaissance « clé créée par nous », jamais de
        // suppression — nos valeurs sont retirées, la clé (vide) reste
        Assert.True(store.Values.ContainsKey("{bt}"));
        Assert.Empty(store.Values["{bt}"]);
    }

    [Fact]
    public void Enable_UnblocksDisabledSysFx_WithBackup()
    {
        var (registrar, store, backups) = Make();

        var result = registrar.Enable("{spk}");

        Assert.True(result.Success, result.Message);
        Assert.Equal(0, store.Values["{spk}"][ApoConstants.FxDisableSysFx]);
        Assert.Equal(1, backups.Load("{spk}")!.OriginalDisableSysFx);
    }

    [Fact]
    public void Enable_ForbiddenEndpoint_RefusedWithoutAnyWrite()
    {
        var (registrar, store, backups) = Make();
        int writesBefore = store.Trace.Count(t => t.StartsWith("set:") || t.StartsWith("del:"));

        var result = registrar.Enable("{cable}");

        Assert.False(result.Success);
        Assert.Contains("boucle", result.Message);
        Assert.Null(backups.Load("{cable}"));
        Assert.Equal(writesBefore,
            store.Trace.Count(t => t.StartsWith("set:") || t.StartsWith("del:"))); // RIEN modifié
    }

    [Fact]
    public void Enable_UnknownEndpoint_Fails()
    {
        var (registrar, _, _) = Make();
        var result = registrar.Enable("{gone}");
        Assert.False(result.Success);
    }

    [Fact]
    public void Enable_Twice_IsIdempotent()
    {
        var (registrar, store, backups) = Make();

        Assert.True(registrar.Enable("{hp}").Success);
        var backupFirst = backups.Load("{hp}");
        int writesAfterFirst = store.Trace.Count(t => t.StartsWith("set:"));

        var second = registrar.Enable("{hp}");

        Assert.True(second.Success);
        Assert.Equal(ApoConstants.Clsid, store.Values["{hp}"][ApoConstants.FxEndpointEffectClsid]);
        Assert.Equal(backupFirst, backups.Load("{hp}")); // l'origine n'est PAS réécrasée
        Assert.Equal(writesAfterFirst, store.Trace.Count(t => t.StartsWith("set:"))); // aucune nouvelle écriture
    }

    // ------------------------------------------------------------ désactivation

    [Fact]
    public void Disable_RestoresExactOriginal_IncludingAbsentValue()
    {
        var (registrar, store, backups) = Make();
        registrar.Enable("{hp}");
        registrar.Enable("{spk}");

        Assert.True(registrar.Disable("{hp}").Success);
        // EFX constructeur restauré
        Assert.Equal("{realtek-efx}", store.Values["{hp}"][ApoConstants.FxEndpointEffectClsid]);
        Assert.Null(backups.Load("{hp}")); // sauvegarde consommée

        Assert.True(registrar.Disable("{spk}").Success);
        Assert.Equal("{nvidia-efx}", store.Values["{spk}"][ApoConstants.FxEndpointEffectClsid]);
        Assert.Equal(1, store.Values["{spk}"][ApoConstants.FxDisableSysFx]); // débloqué → rébloqué comme à l'origine
    }

    [Fact]
    public void Disable_NeverEnabled_IsNoOpSuccess()
    {
        var (registrar, store, _) = Make();
        int writesBefore = store.Trace.Count(t => t.StartsWith("set:") || t.StartsWith("del:"));

        var result = registrar.Disable("{hp}");

        Assert.True(result.Success);
        Assert.Equal(writesBefore, store.Trace.Count(t => t.StartsWith("set:") || t.StartsWith("del:")));
    }

    // ---------------------------------------------------------------- réparation

    [Fact]
    public void RepairEnabled_RewritesAfterDriverUpdate_AndCounts()
    {
        var (registrar, store, backups) = Make();
        registrar.Enable("{hp}");
        registrar.Enable("{spk}");

        // mise à jour de pilote : les FxProperties sont écrasées
        store.Values["{hp}"][ApoConstants.FxEndpointEffectClsid] = "{realtek-new}";
        store.Values["{spk}"].Remove(ApoConstants.FxEndpointEffectClsid);

        int repaired = registrar.RepairEnabled();

        Assert.Equal(2, repaired);
        Assert.Equal(ApoConstants.Clsid, store.Values["{hp}"][ApoConstants.FxEndpointEffectClsid]);
        Assert.Equal(ApoConstants.Clsid, store.Values["{spk}"][ApoConstants.FxEndpointEffectClsid]);

        // rien à réparer au second passage
        Assert.Equal(0, registrar.RepairEnabled());
    }

    // ------------------------------------------------------------ désinstallation

    [Fact]
    public void UninstallAll_RestoresEveryEndpoint()
    {
        var (registrar, store, backups) = Make();
        registrar.Enable("{hp}");
        registrar.Enable("{spk}");

        var result = registrar.UninstallAll();

        Assert.True(result.Success, result.Message);
        Assert.Equal("{realtek-efx}", store.Values["{hp}"][ApoConstants.FxEndpointEffectClsid]);
        Assert.Equal("{nvidia-efx}", store.Values["{spk}"][ApoConstants.FxEndpointEffectClsid]);
        Assert.Empty(backups.Keys());
    }

    // ------------------------------------------------------------------ inertie

    [Fact]
    public void WindowsStore_OffWindows_IsInert()
    {
        if (OperatingSystem.IsWindows())
            return; // vérifiable uniquement sur une machine non-Windows (CI)

        var store = new WindowsApoEndpointStore();
        Assert.Empty(store.EnumerateRenderEndpoints());
        Assert.Null(store.GetString("{x}", ApoConstants.FxEndpointEffectClsid));
        Assert.Null(store.GetInt("{x}", ApoConstants.FxDisableSysFx));
        store.SetString("{x}", ApoConstants.FxEndpointEffectClsid, ApoConstants.Clsid); // no-op
        Assert.Null(store.GetString("{x}", ApoConstants.FxEndpointEffectClsid));
        Assert.False(store.FxPropertiesExists("{x}"));
        Assert.Empty(store.FxValueNames("{x}"));
        store.DeleteFxProperties("{x}"); // no-op
    }

    [Theory]
    [InlineData("RoomEq Input")]
    [InlineData("CABLE Output (VB-Audio Virtual Cable)")]
    [InlineData("VoiceMeeter Input (VB-Audio VoiceMeeter VAIO)")]
    [InlineData("LoopBeAudio 1")]
    [InlineData("Enceintes Realtek")]     // négatif : autorisé
    [InlineData("Casque Bluetooth")]
    public void IsForbiddenName_DetectsVirtualCables(string name)
    {
        bool expected = name.Contains("RoomEq", StringComparison.OrdinalIgnoreCase)
                     || name.Contains("VB-", StringComparison.OrdinalIgnoreCase)
                     || name.Contains("VoiceMeeter", StringComparison.OrdinalIgnoreCase)
                     || name.Contains("LoopBeAudio", StringComparison.OrdinalIgnoreCase);
        Assert.Equal(expected, ApoRegistrar.IsForbiddenName(name));
    }
}
