using System.Text;

namespace RoomEq.Core.Io;

/// <summary>
/// Journal de diagnostic : trace les phases de la calibration, les formats de
/// capture, les analyses de balayage et toutes les erreurs (avec pile complète)
/// dans un fichier texte par jour. But : pouvoir diagnostiquer à distance un
/// « la courbe ne s'affiche pas » sans accès à la machine.
/// Emplacement : logs/ à côté de l'exécutable (application portable) — sinon
/// %LOCALAPPDATA%\RoomEq\logs — sinon en mémoire (jamais d'exception).
/// </summary>
public static class DiagnosticsLog
{
    /// <summary>
    /// Interrupteur global de la journalisation. La VERSION PUBLIQUE le passe à
    /// false au démarrage : plus rien n'est écrit — ni fichier, ni tampon mémoire
    /// (« ne log rien, n'envoie rien »). Toute autre version journalise normalement.
    /// </summary>
    public static bool Enabled { get; set; } = true;

    private static readonly object Gate = new();
    private static string? _day;
    private static StreamWriter? _writer;
    private static string? _path;
    private static string? _failedBase;

    // repli mémoire (annonce du commentaire de classe) : les dernières lignes
    // restent disponibles pour la télémétrie même si aucun fichier n'est accessible
    private const int MemoryCapacity = 600;
    private static readonly Queue<string> Memory = new();

    /// <summary>Chemin du fichier de journal courant (peut être null si aucun).</summary>
    public static string? LogPath => _path;

    /// <summary>Ouverture paresseuse : le premier appel crée le fichier du jour.</summary>
    private static StreamWriter OpenForToday()
    {
        string day = DateTime.Now.ToString("yyyy-MM-dd");
        if (_writer != null && _day == day) return _writer;

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
                if (!append) _writer.WriteLine($"# RoomEq — journal de diagnostic, {DateTime.Now:yyyy-MM-dd HH:mm:ss}");
                return _writer;
            }
            catch
            {
                _failedBase = dir; // répertoire inaccessible : on tentera le suivant
            }
        }
        _path = null;
        throw new IOException("Aucun emplacement de journal accessible.");
    }

    private static System.Collections.Generic.IEnumerable<string> CandidateDirs()
    {
        // 1) à côté de l'exécutable (application portable)
        yield return Path.Combine(AppContext.BaseDirectory, "logs");
        // 2) profil utilisateur (répertoires protégés en écriture)
        string localAppData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        if (!string.IsNullOrEmpty(localAppData))
            yield return Path.Combine(localAppData, "RoomEq", "logs");
        // 3) on ne retente pas indéfiniment un répertoire qui a déjà échoué
        if (_failedBase != null) yield return _failedBase;
    }

    /// <summary>Écrit une ligne horodatée (niveau + message). Ne lève jamais.</summary>
    public static void Write(string level, string message)
    {
        if (!Enabled) return; // version publique : aucun journal

        lock (Gate)
        {
            string line = $"{DateTime.Now:HH:mm:ss.fff} [{level}] {message.ReplaceLineEndings(" ⏎ ")}";
            Memory.Enqueue(line);
            if (Memory.Count > MemoryCapacity) Memory.Dequeue();
            try
            {
                var w = OpenForToday();
                w.WriteLine(line);
            }
            catch
            {
                // le journal ne doit jamais faire planter l'application
            }
        }
    }

    /// <summary>Contenu du tampon mémoire (dernières lignes écrites, même sans fichier).</summary>
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
        if (ex == null)
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
