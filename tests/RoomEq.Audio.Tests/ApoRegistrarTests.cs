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
            if (RefusalOnSet is { } refusal)
                throw refusal; // v3.9.3 : le store Windows lève après takeover raté
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

        // v3.9.6 : MULTI_SZ (modes de traitement EFX) — stockés en string[].
        public IReadOnlyList<string>? GetStrings(string key, string name)
        {
            Trace.Add($"getmulti:{key}/{name}");
            return Values.TryGetValue(key, out var v) && v.TryGetValue(name, out var val)
                ? val as string[] : null;
        }

        public void SetStrings(string key, string name, IReadOnlyList<string>? values)
        {
            Trace.Add(values == null ? $"delmulti:{key}/{name}" : $"setmulti:{key}/{name}");
            var map = Values.TryGetValue(key, out var v) ? v : Values[key] = new();
            if (values == null)
                map.Remove(name);
            else
                map[name] = values.ToArray();
        }

        /// <summary>v3.9.10 : sous-clé \Properties de l'endpoint (stockée
        /// sous la clé "{guid}\Properties" — détection périphérique combiné).
        /// PRÉSENCE de la valeur, QUEL QUE SOIT SON TYPE (miroir de
        /// RegistryHelper::valueExists de la référence — la propriété
        /// combiné du terrain est un booléen persisté en type NON chaîne,
        /// la lecture « as string » de v3.9.8 la ratait TOUJOURS).</summary>
        public bool HasDeviceProperty(string key, string name)
            => Values.TryGetValue(key + @"\Properties", out var v)
               && v.ContainsKey(name);

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

        /// <summary>v3.9.2 : sécurité à capturer (null = admins inscriptibles).</summary>
        public ApoSecuritySnapshot? SecurityToCapture { get; set; }

        /// <summary>v3.9.3 : si non null, SetString lève cette exception de
        /// refus registre après takeover (simulation du terrain « clé protégée »).</summary>
        public ApoRegistryRefusalException? RefusalOnSet { get; set; }

        public ApoSecuritySnapshot? CaptureSecurity(string key)
        {
            Trace.Add($"capture:{key}");
            return SecurityToCapture;
        }

        public void RestoreSecurity(string key, ApoSecuritySnapshot? snapshot)
        {
            if (snapshot != null)
                Trace.Add($"restore:{key}");
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

    // --------------------------- v3.9.6 : modes de traitement EFX (MULTI_SZ,
    // recette Equalizer APO INSTALL_SFX_EFX — sans mode déclaré, l'EFX peut
    // ne s'appliquer à aucun flux)

    [Fact]
    public void Enable_WritesDefaultProcessingMode_WhenAbsent()
    {
        var (registrar, store, backups) = Make();

        var result = registrar.Enable("{bt}"); // sortie sans FxProperties du tout

        Assert.True(result.Success, result.Message);
        // le mode par défaut est inscrit en MULTI_SZ (un seul GUID)
        var modes = store.GetStrings("{bt}", ApoConstants.FxEfxProcessingModes);
        Assert.NotNull(modes);
        Assert.Equal(new[] { ApoConstants.DefaultProcessingMode }, modes);
        // l'origine (absente) est capturée AVANT l'écriture (invariant 2)
        var backup = backups.Load("{bt}");
        Assert.NotNull(backup);
        Assert.True(backup.EfxProcessingModesCaptured);
        Assert.Null(backup.OriginalEfxProcessingModes);
        int read = store.Trace.IndexOf($"getmulti:{{bt}}/{ApoConstants.FxEfxProcessingModes}");
        int write = store.Trace.IndexOf($"setmulti:{{bt}}/{ApoConstants.FxEfxProcessingModes}");
        Assert.True(read >= 0 && write > read, "la capture doit précéder l'écriture des modes");
    }

    [Fact]
    public void Enable_KeepsDriverProcessingModes_WhenPresent()
    {
        var (registrar, store, backups) = Make();
        // le pilote déclarait ses propres modes pour son EFX
        var driverModes = new[] { "{C18E2F7E-933D-4965-B7D1-1EEF228D2AF3}", "{autre-mode}" };
        store.Values["{hp}"][ApoConstants.FxEfxProcessingModes] = driverModes;

        var result = registrar.Enable("{hp}");

        Assert.True(result.Success, result.Message);
        // les modes du pilote sont INTACTS (comportement Equalizer APO :
        // writeMultiValue seulement si absente)
        Assert.Equal(driverModes,
            store.GetStrings("{hp}", ApoConstants.FxEfxProcessingModes));
        var backup = backups.Load("{hp}");
        Assert.NotNull(backup);
        Assert.Equal(driverModes, backup.OriginalEfxProcessingModes);
    }

    [Fact]
    public void Disable_RestoresProcessingModes_Exactly()
    {
        var (registrar, store, backups) = Make();
        store.Values["{hp}"][ApoConstants.FxEfxProcessingModes] =
            new[] { "{mode-pilote}" };
        registrar.Enable("{hp}");

        Assert.True(registrar.Disable("{hp}").Success);

        // restitués tels quels (pas le mode par défaut)
        Assert.Equal(new[] { "{mode-pilote}" },
            store.GetStrings("{hp}", ApoConstants.FxEfxProcessingModes));
    }

    [Fact]
    public void Disable_RemovesOurDefaultMode_WhenOriginallyAbsent()
    {
        var (registrar, store, _) = Make();
        registrar.Enable("{bt}"); // écrit le mode par défaut (absent à l'origine)

        Assert.True(registrar.Disable("{bt}").Success);

        // restitution exacte : la valeur que NOUS avions écrite disparaît
        // (la clé {bt} entière disparaît de toute façon — clé créée par nous)
        Assert.False(store.Values.ContainsKey("{bt}"));
    }

    [Fact]
    public void Disable_LegacyBackupWithoutModesCapture_NeverTouchesModes()
    {
        var (registrar, store, backups) = Make();
        registrar.Enable("{bt}");
        // le pilote a écrit ses modes APRÈS notre activation
        store.Values["{bt}"][ApoConstants.FxEfxProcessingModes] = new[] { "{modes-pilote}" };
        // sauvegarde au format v3.7–v3.9.5 (champ modes absent → non capturé)
        backups.Save("{bt}", new ApoEndpointBackup(null, null));

        Assert.True(registrar.Disable("{bt}").Success);

        // les modes du pilote sont INTACTS (jamais supprimés par une
        // sauvegarde qui ne les a pas capturés)
        var modes = store.GetStrings("{bt}", ApoConstants.FxEfxProcessingModes);
        Assert.NotNull(modes);
        Assert.Equal(new[] { "{modes-pilote}" }, modes);
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
        // suppression — nos valeurs suivies sont retirées, la clé reste
        Assert.True(store.Values.ContainsKey("{bt}"));
        Assert.DoesNotContain(ApoConstants.FxEndpointEffectClsid, store.Values["{bt}"].Keys);
        // v3.9.6 : la valeur MULTI_SZ de modes de traitement peut rester —
        // une sauvegarde d'avant-champ ne l'a PAS capturée, la supprimer
        // reviendrait à risquer celle du pilote (elle peut légitimement
        // contenir le mode par défaut). Inerte sans CLSID EFX.
        if (store.Values["{bt}"].ContainsKey(ApoConstants.FxEfxProcessingModes))
            Assert.Equal(new[] { ApoConstants.DefaultProcessingMode },
                store.GetStrings("{bt}", ApoConstants.FxEfxProcessingModes));
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

    // --------------------------------------- v3.9.2 : sécurité (prise de possession)

    [Fact]
    public void Enable_CapturesSecurity_BeforeFirstWrite()
    {
        var (registrar, store, backups) = Make();
        // sortie dont la clé appartient à SYSTEM (cas MMDevices réel) : le
        // store capturera owner + DACL pour restitution à la désactivation
        var snapshot = new ApoSecuritySnapshot(
            new ApoKeySecurity("S-1-5-18", "c3lzdGVtLWRlc2NyaXB0ZXVy"), null);
        store.SecurityToCapture = snapshot;

        var result = registrar.Enable("{bt}");

        Assert.True(result.Success, result.Message);
        Assert.Equal(snapshot, backups.Load("{bt}")!.Security); // stocké dans la sauvegarde

        // ORDRE (invariant 2 étendu) : la capture précède la première écriture
        int capture = store.Trace.IndexOf("capture:{bt}");
        int write = store.Trace.IndexOf($"set:{{bt}}/{ApoConstants.FxEndpointEffectClsid}");
        Assert.True(capture >= 0 && write > capture,
            "la sécurité doit être capturée avant la première écriture");
    }

    [Fact]
    public void Enable_AfterFailedTakeover_VerdictNamesTheFailingStep()
    {
        var (registrar, store, _) = Make();
        // v3.9.3 : terrain « clé protégée » après takeover raté — le store
        // lève ApoRegistryRefusalException portant le maillon défaillant ;
        // le verdict doit le NOMMER (au lieu du générique v3.9.2 qui a coûté
        // un aller-retour terrain de débuggage à l'aveugle)
        store.RefusalOnSet = new ApoRegistryRefusalException(
            "le privilège SeTakeOwnershipPrivilege est absent du jeton du processus "
            + "(élévation administrateur refusée ou non demandée)",
            "Le registre a refusé l'écriture sur cette sortie (clé protégée) : "
            + "le privilège SeTakeOwnershipPrivilege est absent du jeton du processus "
            + "(élévation administrateur refusée ou non demandée) "
            + "— refus initial « Requested registry access is not allowed »");

        var result = registrar.Enable("{bt}");

        Assert.False(result.Success);
        // le verdict NOMME le maillon défaillant (privilège) et reste
        // actionnable (relance + invite administrateur)
        Assert.Contains("SeTakeOwnershipPrivilege", result.Message);
        Assert.Contains("clé protégée", result.Message);
        Assert.Contains("invite administrateur", result.Message);
    }

    [Fact]
    public void Disable_RestoresSecurity_AfterKeyOperations()
    {
        var (registrar, store, _) = Make();
        store.SecurityToCapture = new ApoSecuritySnapshot(
            new ApoKeySecurity("S-1-5-18", "c3lzdGVtLWRlc2NyaXB0ZXVy"), null);
        registrar.Enable("{bt}");

        Assert.True(registrar.Disable("{bt}").Success);

        // la restitution de sécurité arrive APRÈS la suppression de la
        // sous-clé — les écritures/suppressions ont besoin de l'ACE d'activation
        int delKey = store.Trace.IndexOf("delkey:{bt}");
        int restore = store.Trace.IndexOf("restore:{bt}");
        Assert.True(delKey >= 0, "la sous-clé créée doit être supprimée");
        Assert.True(restore > delKey,
            "la sécurité est restituée après toutes les écritures/suppressions");
    }

    [Fact]
    public void Disable_LegacyBackupWithoutSecurity_NeverRestoresSecurity()
    {
        var (registrar, store, backups) = Make();
        registrar.Enable("{hp}");
        // sauvegarde au format v3.7–v3.9.1 (champ Security absent → null)
        backups.Save("{hp}", new ApoEndpointBackup("{realtek-efx}", null));
        store.Trace.Clear();

        Assert.True(registrar.Disable("{hp}").Success);

        Assert.DoesNotContain(store.Trace, t => t.StartsWith("restore:")); // rien à restituer
    }

    [Fact]
    public void Disable_CapturedNullSecurity_NeverRestores()
    {
        var (registrar, store, _) = Make();
        // cas nominal (SecurityToCapture null) : admins déjà inscriptibles,
        // la capture (lecture pure) retourne null — aucune restitution
        registrar.Enable("{hp}");
        int captures = store.Trace.Count(t => t.StartsWith("capture:"));

        Assert.True(registrar.Disable("{hp}").Success);

        Assert.Equal(1, captures); // la lecture a bien eu lieu…
        Assert.DoesNotContain(store.Trace, t => t.StartsWith("restore:")); // …null → rien à restituer
    }

    // ---------------------- v3.9.8 : position ADAPTÉE AU PÉRIPHÉRIQUE — les
    // sorties combinées (Bluetooth Windows 11) n'acceptent pas l'EFX
    // (« bluetooth devices may be combined in Windows 11, EFX will not work
    // then » — Equalizer APO, DeviceAPOInfo.cpp) : RoomEq s'y inscrit en MFX

    [Fact]
    public void Enable_CombinedDevice_WritesMfx_NotEfx()
    {
        var (registrar, store, backups) = Make();
        // périphérique combiné : la propriété {b3f8fa53…},41 existe —
        // v3.9.10 : en type NON chaîne (int), LE cas du terrain (booléen
        // persisté REG_DWORD/REG_BINARY que la lecture REG_SZ ratait)
        store.Values["{bt}\\Properties"] = new()
        {
            [ApoConstants.CombinedDeviceProperty] = 1,
        };

        var result = registrar.Enable("{bt}");

        Assert.True(result.Success, result.Message);
        // inscrit en MFX (,6) — PAS en EFX (,7)
        Assert.Equal(ApoConstants.Clsid, store.Values["{bt}"][ApoConstants.FxModeEffectClsid]);
        Assert.False(store.Values["{bt}"].ContainsKey(ApoConstants.FxEndpointEffectClsid));
        // modes de traitement attachés AU MFX
        var modes = store.GetStrings("{bt}", ApoConstants.FxMfxProcessingModes);
        Assert.NotNull(modes);
        Assert.Equal(new[] { ApoConstants.DefaultProcessingMode }, modes);
        // la sauvegarde mémorise la position et l'origine MFX
        var backup = backups.Load("{bt}");
        Assert.NotNull(backup);
        Assert.Equal("MFX", backup.SlotUsed);
        Assert.Null(backup.OriginalModeEffectClsid); // rien avant nous
        Assert.True(result.Message.Contains("MFX", StringComparison.Ordinal));
    }

    [Fact]
    public void Enable_CombinedDevice_WithOriginalMfx_PreservesItInBackup()
    {
        var (registrar, store, backups) = Make();
        store.Values["{bt}"] = new()
        {
            [ApoConstants.FxModeEffectClsid] = "{mfx-pilote}",
            [ApoConstants.FxMfxProcessingModes] = new[] { "{mode-pilote}" },
        };
        store.Values["{bt}\\Properties"] = new()
        {
            [ApoConstants.CombinedDeviceProperty] = 1,
        };

        Assert.True(registrar.Enable("{bt}").Success);

        var backup = backups.Load("{bt}");
        Assert.NotNull(backup);
        Assert.Equal("{mfx-pilote}", backup.OriginalModeEffectClsid);
        Assert.Equal(new[] { "{mode-pilote}" }, backup.OriginalMfxProcessingModes);
        // les modes du pilote ne sont PAS écrasés (comportement Equalizer APO)
        Assert.Equal(new[] { "{mode-pilote}" },
            store.GetStrings("{bt}", ApoConstants.FxMfxProcessingModes));
    }

    [Fact]
    public void Disable_AfterMfxActivation_RestoresExactOrigin()
    {
        var (registrar, store, backups) = Make();
        store.Values["{bt}"] = new()
        {
            [ApoConstants.FxModeEffectClsid] = "{mfx-pilote}",
            [ApoConstants.FxMfxProcessingModes] = new[] { "{mode-pilote}" },
        };
        store.Values["{bt}\\Properties"] = new()
        {
            [ApoConstants.CombinedDeviceProperty] = 1,
        };
        registrar.Enable("{bt}");

        Assert.True(registrar.Disable("{bt}").Success);

        // origine EXACTE restaurée dans le slot MFX
        Assert.Equal("{mfx-pilote}", store.Values["{bt}"][ApoConstants.FxModeEffectClsid]);
        Assert.Equal(new[] { "{mode-pilote}" },
            store.GetStrings("{bt}", ApoConstants.FxMfxProcessingModes));
        Assert.Null(backups.Load("{bt}")); // sauvegarde consommée
    }

    [Fact]
    public void Enable_NonCombinedDevice_StillWritesEfx()
    {
        var (registrar, store, _) = Make();

        // SANS propriété combinée : position EFX inchangée (comportement
        // v3.9.6/v3.9.7 — les sorties Realtek/HDMI classiques)
        var result = registrar.Enable("{bt}");

        Assert.True(result.Success, result.Message);
        Assert.Equal(ApoConstants.Clsid, store.Values["{bt}"][ApoConstants.FxEndpointEffectClsid]);
        Assert.False(store.Values["{bt}"].ContainsKey(ApoConstants.FxModeEffectClsid));
        Assert.Null(store.GetStrings("{bt}", ApoConstants.FxMfxProcessingModes));
    }

    // --------------- v3.9.10 : PRÉSENCE, PAS TYPE + position IMPOSÉE (repli)
    // Retour terrain v3.9.9 : « Position choisie : EFX » sur le casque JBL
    // COMBINÉ, sortie morte — la détection v3.9.8 lisait la propriété
    // booléenne « as string » et la ratait TOUJOURS ; la référence teste la
    // PRÉSENCE (RegistryHelper::valueExists). L'échelle de repli impose
    // ensuite la position quand la sonde déclare la sortie morte.

    [Fact]
    public void Enable_CombinedDevice_NonStringType_IsDetected()
    {
        var (registrar, store, backups) = Make();
        // LE cas du terrain : la propriété combiné en type NON chaîne
        // (booléen → REG_DWORD/REG_BINARY) — v3.9.8 voyait « absente »
        store.Values["{bt}\\Properties"] = new()
        {
            [ApoConstants.CombinedDeviceProperty] = 1,          // int, pas string
        };

        var result = registrar.Enable("{bt}");

        Assert.True(result.Success, result.Message);
        Assert.Equal(ApoConstants.Clsid, store.Values["{bt}"][ApoConstants.FxModeEffectClsid]);
        Assert.False(store.Values["{bt}"].ContainsKey(ApoConstants.FxEndpointEffectClsid));
        Assert.Equal("MFX", backups.Load("{bt}")!.SlotUsed);
    }

    [Fact]
    public void Enable_CombinedDevice_StringType_IsStillDetected()
    {
        var (registrar, store, _) = Make();
        // compatibilité : la présence compte pour TOUT type — même une
        // chaîne (faux registre historique, ou propriété future)
        store.Values["{bt}\\Properties"] = new()
        {
            [ApoConstants.CombinedDeviceProperty] = "n'importe quoi",
        };

        var result = registrar.Enable("{bt}");

        Assert.True(result.Success, result.Message);
        Assert.Equal(ApoConstants.Clsid, store.Values["{bt}"][ApoConstants.FxModeEffectClsid]);
    }

    [Fact]
    public void Enable_ForcedMfxSlot_OnNonCombinedDevice_WritesMfx()
    {
        var (registrar, store, backups) = Make();

        // repli automatique : la position IMPOSÉE gagne sur la détection
        // (sortie NON combinée → détection EFX, l'échelle impose MFX)
        var result = registrar.Enable("{bt}", ApoFxSlot.Mfx);

        Assert.True(result.Success, result.Message);
        Assert.Equal(ApoConstants.Clsid, store.Values["{bt}"][ApoConstants.FxModeEffectClsid]);
        Assert.False(store.Values["{bt}"].ContainsKey(ApoConstants.FxEndpointEffectClsid));
        Assert.Equal("MFX", backups.Load("{bt}")!.SlotUsed);
        Assert.Equal(new[] { ApoConstants.DefaultProcessingMode },
            store.GetStrings("{bt}", ApoConstants.FxMfxProcessingModes));
    }

    [Fact]
    public void Enable_ForcedEfxSlot_OnCombinedDevice_WritesEfx()
    {
        var (registrar, store, backups) = Make();
        store.Values["{bt}\\Properties"] = new()
        {
            [ApoConstants.CombinedDeviceProperty] = 1,
        };

        var result = registrar.Enable("{bt}", ApoFxSlot.Efx);

        Assert.True(result.Success, result.Message);
        Assert.Equal(ApoConstants.Clsid, store.Values["{bt}"][ApoConstants.FxEndpointEffectClsid]);
        Assert.False(store.Values["{bt}"].ContainsKey(ApoConstants.FxModeEffectClsid));
        Assert.Equal("EFX", backups.Load("{bt}")!.SlotUsed);
    }

    [Fact]
    public void Enable_FallbackCycle_CapturesTrueOriginAgain()
    {
        // le cycle REPLI du terrain : essai EFX (détection trompeuse) →
        // rollback → essai MFX forcé : la sauvegarde du SECOND essai
        // capture l'origine VRAIE (restaurée par le premier Disable)
        var (registrar, store, backups) = Make();
        store.Values["{bt}"] = new()
        {
            [ApoConstants.FxEndpointEffectClsid] = "{efx-pilote}",
            [ApoConstants.FxModeEffectClsid] = "{mfx-pilote}",
        };

        Assert.True(registrar.Enable("{bt}", ApoFxSlot.Efx).Success);   // 1er essai
        Assert.True(registrar.Disable("{bt}").Success);                 // rollback sonde
        Assert.True(registrar.Enable("{bt}", ApoFxSlot.Mfx).Success);   // repli

        var backup = backups.Load("{bt}");
        Assert.NotNull(backup);
        Assert.Equal("MFX", backup.SlotUsed);
        Assert.Equal("{mfx-pilote}", backup.OriginalModeEffectClsid);  // origine vraie
        Assert.Equal("{efx-pilote}", backup.OriginalEndpointEffectClsid);
        // état registre : MFX à nous, EFX du pilote INTACT
        Assert.Equal(ApoConstants.Clsid, store.Values["{bt}"][ApoConstants.FxModeEffectClsid]);
        Assert.Equal("{efx-pilote}", store.Values["{bt}"][ApoConstants.FxEndpointEffectClsid]);
    }

    [Fact]
    public void Enable_StaleOursClsidInOtherSlot_IsRemovedNotRestored()
    {
        var (registrar, store, backups) = Make();
        // reste d'une activation v3.9.7 (EFX) ayant perdu sa sauvegarde,
        // sur une sortie DEVENUE combinée : notre CLSID en EFX ne doit ni
        // rester, ni être « restauré » comme origine
        store.Values["{bt}"] = new()
        {
            [ApoConstants.FxEndpointEffectClsid] = ApoConstants.Clsid,
        };
        store.Values["{bt}\\Properties"] = new()
        {
            [ApoConstants.CombinedDeviceProperty] = 1,
        };

        Assert.True(registrar.Enable("{bt}").Success);

        // le MFX porte notre CLSID, l'EFX est NETTOYÉ
        Assert.Equal(ApoConstants.Clsid, store.Values["{bt}"][ApoConstants.FxModeEffectClsid]);
        Assert.False(store.Values["{bt}"].ContainsKey(ApoConstants.FxEndpointEffectClsid));
        var backup = backups.Load("{bt}");
        Assert.NotNull(backup);
        Assert.Null(backup.OriginalEndpointEffectClsid); // notre CLSID ≠ origine

        // et la désactivation ne « restaure » PAS notre CLSID en EFX
        Assert.True(registrar.Disable("{bt}").Success);
        Assert.False(store.Values["{bt}"].ContainsKey(ApoConstants.FxEndpointEffectClsid));
        Assert.False(store.Values["{bt}"].ContainsKey(ApoConstants.FxModeEffectClsid));
    }

    [Fact]
    public void Repair_ReinstallsIntoBackedUpSlot()
    {
        var (registrar, store, backups) = Make();
        store.Values["{bt}\\Properties"] = new()
        {
            [ApoConstants.CombinedDeviceProperty] = 1,
        };
        registrar.Enable("{bt}");
        // une mise à jour de pilote efface l'inscription ET les modes
        store.Values["{bt}"].Clear();

        int repaired = registrar.RepairEnabled();

        Assert.Equal(1, repaired);
        // réparation DANS la position MFX mémorisée par la sauvegarde
        Assert.Equal(ApoConstants.Clsid, store.Values["{bt}"][ApoConstants.FxModeEffectClsid]);
        Assert.Equal(new[] { ApoConstants.DefaultProcessingMode },
            store.GetStrings("{bt}", ApoConstants.FxMfxProcessingModes));
    }

    [Fact]
    public void BackupFileStore_LegacyJson_WithoutSlot_LoadsAsEfx()
    {
        // rétrocompatibilité : un backups.json v3.9.7 (sans champ SlotUsed)
        // se désactive en RESTAURANT L'EFX, comme avant
        var path = Path.Combine(Path.GetTempPath(),
            "roomeq-test-backups-slot-" + Guid.NewGuid().ToString("N") + ".json");
        try
        {
            File.WriteAllText(path,
                """{"Endpoints":{"{hp}":{"OriginalEndpointEffectClsid":"{o}","OriginalDisableSysFx":null,"FxPropertiesExisted":true,"EfxProcessingModesCaptured":true}}}""");
            var fileStore = new ApoBackupFileStore(path);

            var backup = fileStore.Load("{hp}");

            Assert.NotNull(backup);
            Assert.Null(backup.SlotUsed);                          // champ absent → EFX
            Assert.Equal(ApoFxSlot.Efx, ApoSlotPolicy.ParseSlot(backup.SlotUsed));
            Assert.Null(backup.OriginalModeEffectClsid);           // nouveaux champs absents
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public void BackupFileStore_LegacyJson_LoadsWithoutSecurity()
    {
        // rétrocompatibilité disque : un backups.json écrit par v3.7–v3.9.1
        // (sans champ Security) reste lisible — Security null, jamais de
        // fausse restitution
        var path = Path.Combine(Path.GetTempPath(),
            "roomeq-test-backups-" + Guid.NewGuid().ToString("N") + ".json");
        try
        {
            File.WriteAllText(path,
                """{"Endpoints":{"{hp}":{"OriginalEndpointEffectClsid":"{o}","OriginalDisableSysFx":1,"FxPropertiesExisted":false}}}""");
            var fileStore = new ApoBackupFileStore(path);

            var backup = fileStore.Load("{hp}");

            Assert.NotNull(backup);
            Assert.Null(backup.Security);            // champ absent → null
            Assert.False(backup.FxPropertiesExisted); // champs connus préservés
            Assert.Equal(1, backup.OriginalDisableSysFx);
        }
        finally
        {
            File.Delete(path);
        }
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
        // v3.9.6 : la mise à jour a aussi effacé les modes de traitement
        store.Values["{spk}"].Remove(ApoConstants.FxEfxProcessingModes);

        int repaired = registrar.RepairEnabled();

        Assert.Equal(2, repaired);
        Assert.Equal(ApoConstants.Clsid, store.Values["{hp}"][ApoConstants.FxEndpointEffectClsid]);
        Assert.Equal(ApoConstants.Clsid, store.Values["{spk}"][ApoConstants.FxEndpointEffectClsid]);
        // le mode de traitement EFX est rétabli lui aussi (idempotent)
        Assert.Equal(new[] { ApoConstants.DefaultProcessingMode },
            store.GetStrings("{spk}", ApoConstants.FxEfxProcessingModes));

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
        Assert.Null(store.GetStrings("{x}", ApoConstants.FxEfxProcessingModes));
        Assert.False(store.HasDeviceProperty("{x}", ApoConstants.CombinedDeviceProperty)); // v3.9.10
        store.SetString("{x}", ApoConstants.FxEndpointEffectClsid, ApoConstants.Clsid); // no-op
        Assert.Null(store.GetString("{x}", ApoConstants.FxEndpointEffectClsid));
        Assert.False(store.FxPropertiesExists("{x}"));
        Assert.Empty(store.FxValueNames("{x}"));
        store.DeleteFxProperties("{x}"); // no-op
        Assert.Null(store.CaptureSecurity("{x}")); // v3.9.2 : no-op, null
        store.RestoreSecurity("{x}", new ApoSecuritySnapshot( // no-op, jamais d'exception
            new ApoKeySecurity("S-1-5-18", "c3lzdGVtLWRlc2NyaXB0ZXVy"), null));
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
