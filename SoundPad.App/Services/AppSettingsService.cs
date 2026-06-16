using System.Diagnostics;
using System.IO;
using System.Text.Json;
using SoundPad.App.Models;

namespace SoundPad.App.Services;

public static class AppSettingsService
{
    private static readonly JsonSerializerOptions _json = new JsonSerializerOptions
    {
        WriteIndented = true
    };

    public static AppSettings Load()
    {
        if (!File.Exists(AppPaths.SettingsJsonPath))
            return new AppSettings();

        try
        {
            var text = File.ReadAllText(AppPaths.SettingsJsonPath);
            return JsonSerializer.Deserialize<AppSettings>(text, _json) ?? new AppSettings();
        }
        catch (Exception ex)
        {
            // Rename the broken file so we don't lose it, then start fresh.
            var stamp  = DateTime.Now.ToString("yyyyMMdd_HHmmss");
            var backup = AppPaths.SettingsJsonPath + $".bak_{stamp}";
            try { File.Move(AppPaths.SettingsJsonPath, backup); } catch { }
            Debug.WriteLine($"[Settings] settings.json was invalid ({ex.Message}); backed up to {backup}");
            return new AppSettings();
        }
    }

    public static void Save(AppSettings settings)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(AppPaths.SettingsJsonPath)!);
        File.WriteAllText(AppPaths.SettingsJsonPath, JsonSerializer.Serialize(settings, _json));
    }
}
