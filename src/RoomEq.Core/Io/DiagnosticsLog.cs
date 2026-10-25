using System.Text;

namespace RoomEq.Core.Io;

/// <summary>
/// Journal de diagnostic (spec I-IO-02) : trace les phases de la calibration,
/// les formats de capture, les analyses et toutes les erreurs (piles complètes)
/// dans un fichier texte par jour. Emplacements : logs/ à côté de l'exécutable
/// (application portable), sinon %LOCALAPPDATA%\RoomEq\logs, sinon en mémoire.
/// Ne lève JAMAIS d'exception.
/// </summary>
public static class DiagnosticsLog
{
    /// <summary>
    /// Interrupteur global. La VERSION PUBLIQUE le passe à false au démarrage :
    /// plus rien n'est écrit — ni fichier, ni tampon mémoire.
    /// </summary>
    public static bool Enabled { get; set; } = true;

    private static string? _overrideDir;

    /// <summary>
    /// Répertoire injectable pour les tests (null = comportement normal).
    /// Le changement ferme le writer courant pour forcer la réouverture dans le
    /// nouveau répertoire (le journal est statique : son état survit aux tests).
    /// </summary>
    public static string? OverrideDir
    {
        get => _overrideDir;
        set
        {
            lock (Gate)
            {
                _overrideDir = value;
                _writer?.Dispose();
                _writer = null;
                _day = null;
                _path = null;
                _failedBase = null;
            }
        }
    }

    /// <summary>Réinitialisation complète pour les tests (writer + tampon mémoire).</summary>
    internal static void ResetForTests()
    {
        lock (Gate)
        {
            _writer?.Dispose();
            _writer = null;
            _day = null;
            _path = null;
            _failedBase = null;
            Memory.Clear();
        }
    }

    private const int MemoryCapacity = 600;
    private static readonly object Gate = new();
    private static readonly Queue<string> Memory = new();
    private static string? _day;
    private static StreamWriter? _writer;
    private static string? _path;
    private static string? _failedBase;

    /// <summary>Chemin du fichier de journal courant (null si aucun).</summary>
    public static string? LogPath => _path;

    /// <summary>Ouverture paresseuse : le premier appel crée le fichier du jour.</summary>
    private static StreamWriter OpenForToday()
    {
        string day = DateTime.Now.ToString("yyyy-MM-dd");
        if (_writer != null && _day == day)
            return _writer;

        _writer?.Dispose();
        _writer = null;
        _day = day;

        string fileName = $"roomeq-{day}.log";
        foreach (string dir in CandidateDirs())
        {
            try
            {
                Directory.CreateDirectory(dir);
                string path = Path.Combine(dir, fileName);
                bool append = File.Exists(path);
                _writer = new StreamWriter(path, append, Encoding.UTF8) { AutoFlush = true };
                _path = path;
                if (!append)
                    _writer.WriteLine($"# RoomEq — journal de diagnostic, {DateTime.Now:yyyy-MM-dd HH:mm:ss}");
                return _writer;
            }
            catch
            {
                _failedBase = dir; // répertoire inaccessible : on tente le suivant
            }
        }
        _path = null;
        throw new IOException("Aucun emplacement de journal accessible.");
    }

    private static IEnumerable<string> CandidateDirs()
    {
        // 1) répertoire injectable (tests)
        if (OverrideDir != null)
            yield return OverrideDir;
        // 2) à côté de l'exécutable (application portable)
        yield return Path.Combine(AppContext.BaseDirectory, "logs");
        // 3) profil utilisateur (répertoires protégés en écriture)
        string localAppData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        if (!string.IsNullOrEmpty(localAppData))
            yield return Path.Combine(localAppData, "RoomEq", "logs");
    }

    /// <summary>Écrit une ligne horodatée (niveau + message). Ne lève jamais.</summary>
    public static void Write(string level, string message)
    {
        if (!Enabled)
            return; // version publique : aucun journal

        lock (Gate)
        {
            string line = $"{DateTime.Now:HH:mm:ss.fff} [{level}] {message.ReplaceLineEndings(" ⏎ ")}";
            Memory.Enqueue(line);
            while (Memory.Count > MemoryCapacity)
                Memory.Dequeue();
            try
            {
                OpenForToday().WriteLine(line);
            }
            catch
            {
                // le journal ne doit jamais faire planter l'application
            }
        }
    }

    /// <summary>Contenu du tampon mémoire (dernières lignes, même sans fichier).</summary>
    public static string MemoryDump()
    {
        lock (Gate)
        {
            return Memory.Count == 0 ? "" : string.Join(Environment.NewLine, Memory);
        }
    }

    public static void Info(string message) => Write("INFO", message);
    public static void Warn(string message) => Write("WARN", message);

    /// <summary>Erreur avec détails complets : type, message, erreurs internes et pile.</summary>
    public static void Error(string message, Exception? ex = null)
    {
        if (ex is null)
        {
            Write("ERREUR", message);
            return;
        }

        var sb = new StringBuilder();
        sb.Append(message).Append(" — ").Append(ex.GetType().Name).Append(" : ").AppendLine(ex.Message);
        for (var inner = ex.InnerException; inner != null; inner = inner.InnerException)
            sb.Append("  ↳ ").Append(inner.GetType().Name).Append(" : ").AppendLine(inner.Message);
        sb.Append(ex.StackTrace);
        Write("ERREUR", sb.ToString());
    }
}
