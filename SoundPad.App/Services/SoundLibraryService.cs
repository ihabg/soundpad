using System.Diagnostics;
using System.IO;
using System.Text.Json;
using SoundPad.App.Models;

namespace SoundPad.App.Services;

public static class SoundLibraryService
{
    private static readonly JsonSerializerOptions _json = new JsonSerializerOptions
    {
        WriteIndented = true
    };

    public static List<SoundItem> Load()
    {
        if (!File.Exists(AppPaths.SoundsJsonPath))
            return new List<SoundItem>();

        try
        {
            var text = File.ReadAllText(AppPaths.SoundsJsonPath);
            return JsonSerializer.Deserialize<List<SoundItem>>(text, _json)
                   ?? new List<SoundItem>();
        }
        catch (Exception ex)
        {
            // Rename the broken file so we don't lose it, then start fresh.
            var stamp  = DateTime.Now.ToString("yyyyMMdd_HHmmss");
            var backup = AppPaths.SoundsJsonPath + $".bak_{stamp}";
            try { File.Move(AppPaths.SoundsJsonPath, backup); } catch { }
            Debug.WriteLine($"[Library] sounds.json was invalid ({ex.Message}); backed up to {backup}");
            return new List<SoundItem>();
        }
    }

    public static void Save(List<SoundItem> items)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(AppPaths.SoundsJsonPath)!);
        File.WriteAllText(AppPaths.SoundsJsonPath, JsonSerializer.Serialize(items, _json));
    }

    // Copies sourcePath into AppData\SoundPad\Sounds (deduplicates by name)
    // and returns the destination path.  Does nothing if source is already there.
    public static string ImportFile(string sourcePath)
    {
        Directory.CreateDirectory(AppPaths.SoundsDirectory);

        var fileName        = Path.GetFileName(sourcePath);
        var nameWithoutExt  = Path.GetFileNameWithoutExtension(fileName);
        var ext             = Path.GetExtension(fileName);
        var destPath        = Path.Combine(AppPaths.SoundsDirectory, fileName);

        if (string.Equals(Path.GetFullPath(sourcePath), Path.GetFullPath(destPath),
                          StringComparison.OrdinalIgnoreCase))
            return destPath;

        int counter = 1;
        while (File.Exists(destPath))
        {
            destPath = Path.Combine(AppPaths.SoundsDirectory,
                                    $"{nameWithoutExt}_{counter}{ext}");
            counter++;
        }

        File.Copy(sourcePath, destPath);
        return destPath;
    }
}
