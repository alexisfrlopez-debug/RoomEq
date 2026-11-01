using System.Text.Json;
using System.Text.Json.Serialization;

namespace RoomEq.Audio.SystemApo;

/// <summary>Résultat d'une opération d'installation APO (jamais d'exception).</summary>
public sealed record ApoInstallResult(bool Success, string Message)
{
    public static ApoInstallResult Ok(string message) => new(true, message);
    public static ApoInstallResult Fail(string message) => new(false, message);
}

/// <summary>Un endpoint de rendu vu par le registrar (clé registre + nom).</summary>
public sealed record ApoEndpointRecord(string Key, string FriendlyName);

/// <summary>
/// Abstraction du registre Windows utilisée par le registrar (spec I-AUD-09,
/// invariant 10 : logique pure testable sous Linux sur un faux registre ;
/// l'implémentation réelle <see cref="WindowsApoEndpointStore"/> est inerte
/// hors Windows).
/// </summary>
public interface IApoEndpointStore
{
    /// <summary>Endpoints de rendu actifs (clé registre "{GUID}" + nom convivial).</summary>
    IReadOnlyList<ApoEndpointRecord> EnumerateRenderEndpoints();

    /// <summary>Valeur REG_SZ d'un endpoint (null si absente — jamais d'exception).</summary>
    string? GetString(string endpointKey, string valueName);

    /// <summary>Écrit (ou supprime si null) une valeur REG_SZ d'un endpoint.</summary>
    void SetString(string endpointKey, string valueName, string? value);

    /// <summary>Valeur REG_DWORD d'un endpoint (null si absente).</summary>
    int? GetInt(string endpointKey, string valueName);

    /// <summary>Écrit (ou supprime si null) une valeur REG_DWORD d'un endpoint.</summary>
    void SetInt(string endpointKey, string valueName, int? value);

    /// <summary>
    /// La sous-clé FxProperties de cet endpoint existe-t-elle ? Un pilote sans
    /// effets déclarés ne la crée pas ; l'activation la créera (le moteur
    /// audio lit cette clé quel que soit son auteur — mécanisme standalone
    /// d'Equalizer APO, spec I-AUD-09 as-built v1.2).
    /// </summary>
    bool FxPropertiesExists(string endpointKey);

    /// <summary>Noms des valeurs présentes dans FxProperties (vide si absente).</summary>
    IReadOnlyList<string> FxValueNames(string endpointKey);

    /// <summary>
    /// Supprime la sous-clé FxProperties (restauration d'origine : clé créée
    /// par nous et redevenue vide). Absente = succès sans effet.
    /// </summary>
    void DeleteFxProperties(string endpointKey);
}

/// <summary>
/// Valeurs FxProperties d'origine d'un endpoint modifié par RoomEq.
/// </summary>
/// <param name="FxPropertiesExisted">La sous-clé FxProperties existait-elle
/// avant activation ? Faux = c'est NOUS qui l'avons créée (pilote sans slot
/// d'effets déclaré, ex. pilote générique Windows) : la désactivation la
/// supprimera si elle est redevenue vide. Défaut VRAI — une sauvegarde
/// antérieure au champ (v3.7–v3.9) ne fait JAMAIS supprimer la clé.</param>
public sealed record ApoEndpointBackup(
    string? OriginalEndpointEffectClsid,
    int? OriginalDisableSysFx,
    bool FxPropertiesExisted = true);

/// <summary>Magasin de sauvegardes (JSON sur disque, ou mémoire pour les tests).</summary>
public interface IApoBackupStore
{
    ApoEndpointBackup? Load(string endpointKey);
    void Save(string endpointKey, ApoEndpointBackup? backup);
    IReadOnlyList<string> Keys();
}

/// <summary>
/// Sauvegardes JSON dans <c>%ProgramData%\RoomEq\apo\backups.json</c> —
/// écrites par le processus élevé, lues par l'application (statut UI).
/// Écriture atomique (temp + renommage), tolérante aux fichiers absents.
/// </summary>
public sealed class ApoBackupFileStore(string path) : IApoBackupStore
{
    private sealed class Payload
    {
        public Dictionary<string, ApoEndpointBackup> Endpoints { get; set; } = new();
    }

    private readonly object _sync = new();

    public ApoEndpointBackup? Load(string endpointKey)
    {
        lock (_sync)
        {
            return ReadAll().Endpoints.TryGetValue(endpointKey, out var b) ? b : null;
        }
    }

    public void Save(string endpointKey, ApoEndpointBackup? backup)
    {
        lock (_sync)
        {
            var payload = ReadAll();
            if (backup == null)
                payload.Endpoints.Remove(endpointKey);
            else
                payload.Endpoints[endpointKey] = backup;
            var json = JsonSerializer.Serialize(payload, JsonOpts());
            var tmp = path + ".tmp";
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            File.WriteAllText(tmp, json);
            File.Move(tmp, path, overwrite: true);
        }
    }

    public IReadOnlyList<string> Keys()
    {
        lock (_sync)
        {
            return ReadAll().Endpoints.Keys.ToList();
        }
    }

    private Payload ReadAll()
    {
        try
        {
            if (File.Exists(path))
                return JsonSerializer.Deserialize<Payload>(File.ReadAllText(path), JsonOpts())
                       ?? new Payload();
        }
        catch
        {
            // sauvegarde illisible : agir comme absente (l'activation repart
            // de l'état courant — jamais d'exception au chemin UI)
        }
        return new Payload();
    }

    private static JsonSerializerOptions JsonOpts()
        => new() { WriteIndented = true, Converters = { new JsonStringEnumConverter() } };
}

/// <summary>
/// Registre APO : active/désactive/répare l'APO RoomEq sur les endpoints de
/// rendu (spec I-AUD-09). LOGIQUE PURE (aucune élévation, aucun appel
/// Windows) : toutes les mutations passent par <see cref="IApoEndpointStore"/>
/// avec sauvegarde AVANT écriture (invariant 2 — l'ordre est vérifié par
/// trace dans les tests), restauration exacte de l'origine et idempotence.
/// </summary>
public sealed class ApoRegistrar(IApoEndpointStore store, IApoBackupStore backups)
{
    /// <summary>Anti-boucle (invariant 3) : jamais sur une entrée de câble virtuel.</summary>
    public static bool IsForbiddenName(string friendlyName)
    {
        string n = friendlyName.ToLowerInvariant();
        return n.Contains("roomeq input")       // render du câble RoomEq → boucle
            || n.Contains("roomeq output")
            || n.Contains("roomeq virtual")
            || n.Contains("vb-cable")           // câbles tiers : même danger
            || n.Contains("vb-audio")
            || n.Contains("voiceemeeter")
            || n.Contains("voicemeeter")
            || n.Contains("virtual audio cable")
            || n.Contains("loopbeaudio");
    }

    /// <summary>Notre CLSID est-il inscrit en EFX de cet endpoint ?</summary>
    public bool IsEnabled(string endpointKey)
        => string.Equals(store.GetString(endpointKey, ApoConstants.FxEndpointEffectClsid),
                         ApoConstants.Clsid, StringComparison.OrdinalIgnoreCase);

    /// <summary>L'endpoint a-t-il une sauvegarde d'origine connue ?</summary>
    public bool HasBackup(string endpointKey) => backups.Load(endpointKey) != null;

    /// <summary>
    /// Active l'APO sur un endpoint : (1) contrôle anti-boucle AVANT toute
    /// mutation, (2) sauvegarde des valeurs EFX + Disable_SysFx d'origine,
    /// (3) écriture du CLSID en EFX, (4) déblocage des effets système si
    /// besoin. Idempotent : un double activation ne réécrit pas l'origine.
    /// </summary>
    public ApoInstallResult Enable(string endpointKey)
    {
        var endpoint = FindEndpoint(endpointKey);
        if (endpoint == null)
            return ApoInstallResult.Fail("Périphérique de sortie introuvable (débranché ?).");

        if (IsForbiddenName(endpoint.FriendlyName))
            return ApoInstallResult.Fail(
                "« " + endpoint.FriendlyName + " » est une entrée de câble virtuel : " +
                "l'activer créerait une boucle audio. Choisissez vos enceintes, votre casque ou votre HDMI.");

        bool alreadyEnabled = IsEnabled(endpointKey);
        if (alreadyEnabled && backups.Load(endpointKey) != null)
            return ApoInstallResult.Ok("L'EQ système est déjà actif sur « " + endpoint.FriendlyName + " ».");

        try
        {
            // sauvegarde AVANT écriture (invariant 2) — même si déjà actif sans
            // sauvegarde (installation antérieure perdu) : l'origine devient
            // l'état courant, la désactivation nettoiera proprement.
            // L'EXISTENCE de la sous-clé est capturée aussi : une clé
            // FxProperties CRÉÉE par nous (pilote sans slot déclaré —
            // correctif v3.9.1, cf. WindowsApoEndpointStore.OpenFxForWrite)
            // sera supprimée à la désactivation si elle est redevenue vide.
            bool fxExisted = store.FxPropertiesExists(endpointKey);
            backups.Save(endpointKey, new ApoEndpointBackup(
                OriginalEndpointEffectClsid: store.GetString(endpointKey, ApoConstants.FxEndpointEffectClsid),
                OriginalDisableSysFx: store.GetInt(endpointKey, ApoConstants.FxDisableSysFx),
                FxPropertiesExisted: fxExisted));

            store.SetString(endpointKey, ApoConstants.FxEndpointEffectClsid, ApoConstants.Clsid);

            int? disableSysFx = store.GetInt(endpointKey, ApoConstants.FxDisableSysFx);
            if (disableSysFx == 1)
                store.SetInt(endpointKey, ApoConstants.FxDisableSysFx, 0);

            return ApoInstallResult.Ok(
                "EQ système activé sur « " + endpoint.FriendlyName + " »" +
                (disableSysFx == 1 ? " (effets audio du fabricant débloqués, d'origine sauvegardée)" : "") +
                ".");
        }
        catch (Exception ex)
        {
            return ApoInstallResult.Fail("Activation impossible : " + ex.Message);
        }
    }

    /// <summary>
    /// Désactive l'APO sur un endpoint : restaure EXACTEMENT l'origine
    /// (valeur d'origine, ou suppression si elle n'existait pas). Restaurer
    /// un endpoint jamais modifié = succès sans effet (idempotence).
    /// </summary>
    public ApoInstallResult Disable(string endpointKey)
    {
        var endpoint = FindEndpoint(endpointKey);
        string name = endpoint?.FriendlyName ?? endpointKey;

        try
        {
            var backup = backups.Load(endpointKey);
            bool enabled = IsEnabled(endpointKey);

            if (backup == null)
            {
                if (!enabled)
                    return ApoInstallResult.Ok("L'EQ système n'était pas actif sur « " + name + " ».");
                // activé sans sauvegarde connue : retirer notre CLSID proprement
                store.SetString(endpointKey, ApoConstants.FxEndpointEffectClsid, null);
                return ApoInstallResult.Ok("EQ système retiré de « " + name + " » (aucune origine sauvegardée à restaurer).");
            }

            store.SetString(endpointKey, ApoConstants.FxEndpointEffectClsid, backup.OriginalEndpointEffectClsid);
            store.SetInt(endpointKey, ApoConstants.FxDisableSysFx, backup.OriginalDisableSysFx);

            // Restitution EXACTE (invariant 2) : une sous-clé FxProperties que
            // NOUS avons créée (pilote sans slot déclaré) et qui ne contient
            // plus RIEN après restauration est supprimée — l'état d'origine
            // « pas de clé » revient. Si le pilote y a écrit ses propres
            // valeurs entre-temps, elle reste : elles ne sont pas à nous.
            if (!backup.FxPropertiesExisted
                && store.FxValueNames(endpointKey).Count == 0)
            {
                store.DeleteFxProperties(endpointKey);
            }

            backups.Save(endpointKey, null);

            return ApoInstallResult.Ok("EQ système désactivé : valeurs d'origine de « " + name + " » restaurées.");
        }
        catch (Exception ex)
        {
            return ApoInstallResult.Fail("Désactivation impossible : " + ex.Message);
        }
    }

    /// <summary>
    /// Re-vérifie les endpoints activés : une mise à jour de pilote peut
    /// écraser les FxProperties (invariant 8). Retourne le nombre de
    /// réparations effectivement réécrites.
    /// </summary>
    public int RepairEnabled()
    {
        int repaired = 0;
        foreach (string key in backups.Keys())
        {
            try
            {
                if (IsEnabled(key))
                    continue;
                var backup = backups.Load(key);
                if (backup == null)
                    continue;
                store.SetString(key, ApoConstants.FxEndpointEffectClsid, ApoConstants.Clsid);
                if (backup.OriginalDisableSysFx == 1)
                    store.SetInt(key, ApoConstants.FxDisableSysFx, 0);
                repaired++;
            }
            catch
            {
                // un endpoint irréparable n'empêche pas les autres
            }
        }
        return repaired;
    }

    /// <summary>
    /// Désinstallation totale (invariant 11) : restaure TOUS les endpoints
    /// modifiés. L'inscription COM et les fichiers sont gérés par l'appelant
    /// (ApoSetupCommands) — le registrar ne touche qu'aux endpoints.
    /// </summary>
    public ApoInstallResult UninstallAll()
    {
        int restored = 0;
        var failures = new List<string>();
        foreach (string key in backups.Keys())
        {
            var r = Disable(key);
            if (r.Success)
                restored++;
            else
                failures.Add(r.Message);
        }
        // endpoints activés sans sauvegarde (déjà couverts par Disable, mais
        // par sécurité on balaie aussi les endpoints visibles)
        foreach (var e in store.EnumerateRenderEndpoints())
        {
            if (backups.Load(e.Key) != null)
                continue;
            if (IsEnabled(e.Key))
            {
                var r = Disable(e.Key);
                if (r.Success) restored++; else failures.Add(r.Message);
            }
        }
        return failures.Count == 0
            ? ApoInstallResult.Ok(restored + " sortie(s) restaurée(s) à leur état d'origine.")
            : ApoInstallResult.Fail("Restaurations partielles : " + string.Join(" · ", failures));
    }

    private ApoEndpointRecord? FindEndpoint(string endpointKey)
        => store.EnumerateRenderEndpoints()
            .FirstOrDefault(e => string.Equals(e.Key, endpointKey, StringComparison.OrdinalIgnoreCase));
}
