using RoomEq.Core.Eq;
using RoomEq.Core.Io;

namespace RoomEq.Audio.SystemApo;

/// <summary>État d'une sortie Windows vis-à-vis de l'EQ système APO.</summary>
public sealed record ApoEndpointStatus(
    string Key,
    string FriendlyName,
    bool Enabled,        // notre CLSID est en EFX de cette sortie
    bool OriginalSaved,  // une sauvegarde d'origine existe
    bool Forbidden,      // entrée de câble virtuel : jamais activable
    bool IsDefault);     // sortie par défaut de Windows

/// <summary>
/// Façade « EQ système » pour l'interface (spec I-AUD-09) : statut des
/// sorties (lectures non élevées), activation/désactivation/réparation via
/// <see cref="ApoSetupRunner"/> (élévation UAC unique), courbe active via
/// <see cref="ApoConfigStore"/> et bypass de mesure. Inerte hors Windows
/// (listes vides, aucun effet, jamais d'exception — invariant 10).
/// </summary>
public sealed class WindowsApoSystem : IDisposable
{
    private readonly IApoEndpointStore _store = new WindowsApoEndpointStore();
    private readonly ApoSetupRunner _runner = new();
    private readonly object _sync = new();
    private bool _desiredBypass;          // état voulu par l'onglet Égaliseur
    private EqPreset? _desiredPreset;     // dernière courbe poussée
    private bool _measurementBypass;      // bypass forcé pendant une mesure
    private bool _anyEnabled;             // cache des activations connues
    private string? _lastCurveWriteError;  // v3.9.5 : dernière erreur d'écriture (maillon 1)

    public ApoConfigStore Config { get; }

    /// <summary>Plateforme capable d'héberger l'APO (Windows 64 bits).</summary>
    public static bool IsPlatformSupported
        => OperatingSystem.IsWindows() && Environment.Is64BitOperatingSystem;

    /// <summary>Les DLL APO sont-elles livrées à côté de l'application ?</summary>
    public bool IsReady { get; private set; }

    public event Action? Changed;

    public WindowsApoSystem(string? configDirectoryOverride = null)
    {
        Config = new ApoConfigStore(configDirectoryOverride);
        IsReady = DetectReadiness();
    }

    private bool DetectReadiness()
    {
        if (!IsPlatformSupported)
            return false;
        try
        {
            string src = Path.Combine(AppContext.BaseDirectory, ApoConstants.AppApoSubdir);
            return File.Exists(Path.Combine(src, "RoomEqApo_x64.dll"))
                || File.Exists(Path.Combine(src, "RoomEqApo_arm64.dll"));
        }
        catch
        {
            return false;
        }
    }

    // ------------------------------------------------------------- statuts

    /// <summary>Sorties Windows actives avec leur état APO (lecture seule).</summary>
    public IReadOnlyList<ApoEndpointStatus> EnumerateEndpoints()
    {
        if (!IsPlatformSupported)
            return Array.Empty<ApoEndpointStatus>();

        var registrar = new ApoRegistrar(_store, new ApoBackupFileStore(ApoConstants.BackupPath));
        string? defaultKey = null;
        try
        {
            defaultKey = GetDefaultRenderKey();
        }
        catch
        {
            // défaut indisponible : pas de marqueur
        }

        var list = new List<ApoEndpointStatus>();
        foreach (var e in _store.EnumerateRenderEndpoints())
        {
            bool enabled;
            try { enabled = registrar.IsEnabled(e.Key); }
            catch { enabled = false; }
            list.Add(new ApoEndpointStatus(
                e.Key, e.FriendlyName, enabled,
                registrar.HasBackup(e.Key),
                ApoRegistrar.IsForbiddenName(e.FriendlyName),
                string.Equals(e.Key, defaultKey, StringComparison.OrdinalIgnoreCase)));
        }
        _anyEnabled = list.Any(s => s.Enabled);
        return list;
    }

    private static string? GetDefaultRenderKey()
    {
        try
        {
            var dev = new NAudio.CoreAudioApi.MMDeviceEnumerator()
                .GetDefaultAudioEndpoint(NAudio.CoreAudioApi.DataFlow.Render, NAudio.CoreAudioApi.Role.Console);
            return ApoConstants.EndpointKeyFromMmdeviceId(dev.ID);
        }
        catch
        {
            return null;
        }
    }

    /// <summary>Au moins une sortie a l'EQ système actif (dernier état connu).</summary>
    public bool AnyEnabled
    {
        get { lock (_sync) return _anyEnabled; }
        private set { lock (_sync) _anyEnabled = value; }
    }

    /// <summary>L'APO s'est-il manifesté récemment ? (journal écrit par audiodg)</summary>
    public DateTime? LastApoLoadUtc => Config.ApoLogLastWriteUtc;

    /// <summary>
    /// Dernière erreur d'écriture de la courbe (v3.9.5, null = aucune) : le
    /// panneau l'affiche — une écriture impossible (permissions
    /// %ProgramData%) rendait l'EQ inaudible SANS le moindre signal visible.
    /// </summary>
    public string? LastCurveWriteError
    {
        get { lock (_sync) return _lastCurveWriteError; }
        private set { lock (_sync) _lastCurveWriteError = value; }
    }

    /// <summary>
    /// Dernières lignes du journal écrit par l'APO dans audiodg (v3.9.5) :
    /// « nouvelle courbe publiee (rev N, …) » prouve que le moteur audio a LU
    /// active.json et applique la courbe ; « LockForProcess » prouve le
    /// chargement. Liste vide si absent/illisible — jamais d'exception.
    /// </summary>
    public IReadOnlyList<string> ReadApoLogTail(int maxLines = 8)
        => Config.ReadApoLogTail(maxLines);

    /// <summary>
    /// L'EQ système est-il actif sur CETTE sortie ? (D26) — sert à l'anti-double-
    /// correction du lecteur interne : quand la courbe est déjà appliquée par le
    /// moteur audio Windows, l'EQ local du lecteur doit se contourner.
    /// Accepte l'identifiant complet du lecteur (« wasapi|{0.0.0.00000000}.{GUID} »)
    /// ou la clé registre nue (« {GUID} »). Jamais d'exception, false dans le doute.
    /// </summary>
    public bool CoversEndpoint(string? outputId)
    {
        if (!IsPlatformSupported || string.IsNullOrEmpty(outputId))
            return false;
        string id = outputId.StartsWith("wasapi|", StringComparison.Ordinal)
            ? outputId["wasapi|".Length..]
            : outputId;
        string key = ApoConstants.EndpointKeyFromMmdeviceId(id);
        try
        {
            return EnumerateEndpoints().Any(e =>
                e.Enabled && string.Equals(e.Key, key, StringComparison.OrdinalIgnoreCase));
        }
        catch
        {
            return false; // registre indisponible : ne pas contourner l'EQ local
        }
    }

    // -------------------------------------------------------- opérations élevées

    /// <summary>Active l'EQ système sur une sortie (une invite UAC).</summary>
    public async Task<ApoInstallResult> EnableAsync(string endpointKey, CancellationToken ct = default)
    {
        var result = await _runner.RunAsync("enable", new[] { endpointKey }, ct).ConfigureAwait(false);
        if (result.Success)
        {
            AnyEnabled = true;
            RepushCurve();
            DiagnosticsLog.Info("EQ système activé (" + endpointKey + ") : " + result.Message);
        }
        else
        {
            // D26 : les échecs sont journalisés aussi — sans eux, un bouton qui
            // « ne s'active pas » est indiagnosticable à distance
            DiagnosticsLog.Error("EQ système : activation échouée (" + endpointKey + ") : " + result.Message);
        }
        Changed?.Invoke();
        return result;
    }

    /// <summary>Désactive l'EQ système sur une sortie (une invite UAC).</summary>
    public async Task<ApoInstallResult> DisableAsync(string endpointKey, CancellationToken ct = default)
    {
        var result = await _runner.RunAsync("disable", new[] { endpointKey }, ct).ConfigureAwait(false);
        if (result.Success)
        {
            EnumerateEndpoints(); // re-synchronise le cache _anyEnabled
            DiagnosticsLog.Info("EQ système désactivé (" + endpointKey + ") : " + result.Message);
        }
        else
        {
            DiagnosticsLog.Error("EQ système : désactivation échouée (" + endpointKey + ") : " + result.Message);
        }
        Changed?.Invoke();
        return result;
    }

    /// <summary>Répare les sorties activées (après une mise à jour de pilote).</summary>
    public async Task<ApoInstallResult> RepairAsync(CancellationToken ct = default)
    {
        var result = await _runner.RunAsync("repair", Array.Empty<string>(), ct).ConfigureAwait(false);
        DiagnosticsLog.Info(result.Success ? "EQ système : " + result.Message
                                           : "EQ système réparation échouée : " + result.Message);
        Changed?.Invoke();
        return result;
    }

    /// <summary>Désinstalle l'EQ système (restaure TOUT puis nettoie).</summary>
    public async Task<ApoInstallResult> UninstallAsync(CancellationToken ct = default)
    {
        var result = await _runner.RunAsync("uninstall", Array.Empty<string>(), ct).ConfigureAwait(false);
        if (result.Success)
            AnyEnabled = false;
        DiagnosticsLog.Info("EQ système désinstallation : " + result.Message);
        Changed?.Invoke();
        return result;
    }

    // ------------------------------------------------------------- courbe

    /// <summary>
    /// Pousse la courbe de l'onglet Égaliseur vers l'APO (appelé à chaque
    /// changement, anti-rebondu en amont). Sans effet si aucune sortie n'est
    /// activée — l'écriture ne démarre qu'à la première activation.
    /// </summary>
    public void PushCurve(EqPreset preset, bool bypass)
    {
        lock (_sync)
        {
            _desiredPreset = preset;
            _desiredBypass = bypass;
        }
        WriteCurveIfActive();
    }

    /// <summary>
    /// Bypass forcé pendant une mesure de calibration (invariant 9 : on mesure
    /// la pièce, pas la correction) ; <c>false</c> restaure l'état voulu.
    /// </summary>
    public void SetMeasurementBypass(bool on)
    {
        lock (_sync)
        {
            if (_measurementBypass == on)
                return;
            _measurementBypass = on;
        }
        WriteCurveIfActive();
    }

    private void WriteCurveIfActive()
    {
        EqPreset? preset;
        bool bypass;
        lock (_sync)
        {
            if (!_anyEnabled)
                return;
            preset = _desiredPreset;
            bypass = _desiredBypass || _measurementBypass;
        }
        if (preset == null)
            return;

        try
        {
            // écriture complète (courbe + bypass + révision) : le cœur natif
            // ne swappe PAS à contenu identique — états de filtres conservés,
            // continuité garantie (test 9 du harnais natif)
            Config.WriteCurve(preset, bypass);
            LastCurveWriteError = null; // maillon 1 rétabli
        }
        catch (Exception ex)
        {
            // v3.9.5 : l'erreur est EXPOSÉE (panneau) — une écriture impossible
            // rendait l'EQ inaudible sans aucun signal visible pour l'utilisateur
            LastCurveWriteError = ex.Message;
            DiagnosticsLog.Error("EQ système : écriture de la courbe impossible", ex);
        }
    }

    /// <summary>Réécrit la courbe actuelle (après activation, pour partir d'un état connu).</summary>
    private void RepushCurve()
    {
        EqPreset? preset;
        bool bypass;
        lock (_sync) { preset = _desiredPreset; bypass = _desiredBypass; }
        if (preset != null)
        {
            try
            {
                Config.WriteCurve(preset, bypass);
                LastCurveWriteError = null;
            }
            catch (Exception ex)
            {
                LastCurveWriteError = ex.Message; // v3.9.5 : exposé dans le panneau
                DiagnosticsLog.Error("EQ système : écriture de la courbe impossible", ex);
            }
        }
    }

    public void Dispose()
    {
        // rien à libérer (le registre et les processus sont sans état ici)
    }
}
