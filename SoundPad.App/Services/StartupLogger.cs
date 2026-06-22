using System;
using System.IO;

namespace SoundPad.App.Services;

public static class StartupLogger
{
    private static readonly string _logPath;
    private static readonly object _sync = new();

    public static string LogPath => _logPath;

    static StartupLogger()
    {
        try
        {
            var dir = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                "SoundPad");
            Directory.CreateDirectory(dir);
            _logPath = Path.Combine(dir, "startup.log");
            File.WriteAllText(_logPath,
                $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss.fff}] === SoundPad startup ==={Environment.NewLine}");
        }
        catch
        {
            _logPath = "";
        }
    }

    public static void Log(string message)
    {
        if (_logPath.Length == 0) return;
        try
        {
            lock (_sync)
            {
                File.AppendAllText(_logPath,
                    $"[{DateTime.Now:HH:mm:ss.fff}] {message}{Environment.NewLine}");
            }
        }
        catch { }
    }
}
