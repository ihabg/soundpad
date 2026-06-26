using System.IO;
using System.Text;

namespace SoundPad.App.Services;

// Rolling daily log files in %AppData%\SoundPad\Logs\.
// Each session writes a header with app version and OS.
// Keeps the last 10 days of log files; older ones are deleted on Initialize.
// Thread-safe via a single lock object.
public static class AppLogger
{
    private static readonly object _sync = new();
    private static string?         _currentLogPath;
    private static bool            _initialized;

    // ── Initialization ─────────────────────────────────────────────────────────

    public static void Initialize(string appVersion)
    {
        try
        {
            Directory.CreateDirectory(AppPaths.LogsDirectory);
            PruneOldLogs();

            _currentLogPath = Path.Combine(AppPaths.LogsDirectory,
                $"soundpad-{DateTime.Now:yyyy-MM-dd}.log");

            var header = new[]
            {
                "",
                $"=== SoundPad session started {DateTime.Now:yyyy-MM-dd HH:mm:ss} ===",
                $"  Version : {appVersion}",
                $"  OS      : {Environment.OSVersion}",
                $"  .NET    : {Environment.Version}",
                $"  Machine : {Environment.MachineName}",
                "",
            };

            lock (_sync)
            {
                File.AppendAllLines(_currentLogPath, header);
            }

            _initialized = true;
        }
        catch { /* cannot log if logging itself fails */ }
    }

    // ── Public logging API ─────────────────────────────────────────────────────

    public static void Info(string category, string message)
        => Write("INFO ", category, message, null);

    public static void Warn(string category, string message, Exception? ex = null)
        => Write("WARN ", category, message, ex);

    public static void Error(string category, string message, Exception? ex = null)
        => Write("ERROR", category, message, ex);

    // ── Diagnostic helpers ─────────────────────────────────────────────────────

    // Returns the last N lines from today's log file; empty array if unavailable.
    public static string[] GetLastLines(int count)
    {
        if (_currentLogPath is null || !File.Exists(_currentLogPath))
            return Array.Empty<string>();
        try
        {
            lock (_sync)
            {
                var lines = File.ReadAllLines(_currentLogPath);
                return lines.Length <= count ? lines : lines[^count..];
            }
        }
        catch { return Array.Empty<string>(); }
    }

    // ── Internals ──────────────────────────────────────────────────────────────

    private static void Write(string level, string category, string message, Exception? ex)
    {
        if (!_initialized || _currentLogPath is null) return;
        try
        {
            var sb = new StringBuilder();
            sb.Append($"[{DateTime.Now:HH:mm:ss.fff}] [{level}] [{category}] {message}");

            if (ex is not null)
            {
                sb.AppendLine();
                sb.Append($"  {ex.GetType().Name}: {ex.Message}");

                if (ex.StackTrace is not null)
                {
                    // Include up to 8 stack trace lines.
                    // Strip full directory paths — keep only the file name for privacy.
                    var traceLines = ex.StackTrace
                        .Split('\n', StringSplitOptions.RemoveEmptyEntries);
                    foreach (var line in traceLines.Take(8))
                    {
                        var stripped = StripPathFromTraceLine(line.TrimEnd());
                        sb.AppendLine();
                        sb.Append($"  {stripped}");
                    }
                }

                if (ex.InnerException is not null)
                {
                    sb.AppendLine();
                    sb.Append($"  Inner: {ex.InnerException.GetType().Name}: {ex.InnerException.Message}");
                }
            }

            lock (_sync)
            {
                File.AppendAllText(_currentLogPath, sb.ToString() + Environment.NewLine);
            }
        }
        catch { }
    }

    // Removes the in C:\path\to\ prefix from stack trace lines, keeping only the file name.
    // e.g.  "in C:\Users\Kebab\...\MainWindow.xaml.cs:line 123"
    //    →  "in MainWindow.xaml.cs:line 123"
    private static string StripPathFromTraceLine(string line)
    {
        const string inPrefix = " in ";
        int inIdx = line.IndexOf(inPrefix, StringComparison.OrdinalIgnoreCase);
        if (inIdx < 0) return line;

        int pathStart = inIdx + inPrefix.Length;
        int lastSep   = line.LastIndexOf(Path.DirectorySeparatorChar, line.IndexOf(":line", pathStart + 1) < 0 ? line.Length - 1 : line.IndexOf(":line", pathStart + 1));
        if (lastSep > pathStart)
            return line[..pathStart] + line[(lastSep + 1)..];

        return line;
    }

    private static void PruneOldLogs()
    {
        try
        {
            var cutoff = DateTime.Now.Date.AddDays(-10);
            foreach (var file in Directory.GetFiles(AppPaths.LogsDirectory, "soundpad-????-??-??.log"))
            {
                var stem    = Path.GetFileNameWithoutExtension(file); // soundpad-YYYY-MM-DD
                var dateStr = stem.Length > "soundpad-".Length ? stem["soundpad-".Length..] : "";
                if (DateTime.TryParse(dateStr, out var fileDate) && fileDate.Date < cutoff)
                {
                    try { File.Delete(file); } catch { }
                }
            }
        }
        catch { }
    }
}
