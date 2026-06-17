using System.IO;
using System.IO.Compression;
using System.Text.Json;
using SoundPad.App.Models;

namespace SoundPad.App.Services;

public static class LibraryBackupService
{
    private static readonly JsonSerializerOptions _jsonOptions = new() { WriteIndented = true };

    // ── Export ────────────────────────────────────────────────────────────────

    public static void Export(IReadOnlyList<SoundItem> library, string zipPath)
    {
        // Build sanitized copies where FilePath is just the audio filename.
        var exportItems = library.Select(src => new SoundItem
        {
            Id                = src.Id,
            DisplayName       = src.DisplayName,
            FilePath          = Path.GetFileName(src.FilePath),
            CreatedAt         = src.CreatedAt,
            Category          = src.Category,
            Volume            = src.Volume,
            Hotkey            = src.Hotkey,
            HotkeyInitialized = src.HotkeyInitialized,
            IsFavorite        = src.IsFavorite,
            LastPlayedAt      = src.LastPlayedAt
        }).ToList();

        if (File.Exists(zipPath)) File.Delete(zipPath);
        using var zip = ZipFile.Open(zipPath, ZipArchiveMode.Create);

        // sounds.json entry
        var jsonEntry = zip.CreateEntry("sounds.json", CompressionLevel.Optimal);
        using (var writer = new StreamWriter(jsonEntry.Open()))
            writer.Write(JsonSerializer.Serialize(exportItems, _jsonOptions));

        // One audio file per sound; skip missing files and exact-filename duplicates.
        var addedFiles = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var src in library)
        {
            if (!File.Exists(src.FilePath)) continue;
            var fileName = Path.GetFileName(src.FilePath);
            if (!addedFiles.Add(fileName)) continue;
            zip.CreateEntryFromFile(src.FilePath, $"Sounds/{fileName}", CompressionLevel.Optimal);
        }
    }

    // ── Import ────────────────────────────────────────────────────────────────

    public static ImportResult Import(string zipPath,
                                      IReadOnlyList<SoundItem> currentLibrary,
                                      HotkeyBinding? stopAllHotkey)
    {
        var result = new ImportResult();

        using var zip = ZipFile.OpenRead(zipPath);

        var jsonEntry = zip.GetEntry("sounds.json")
            ?? throw new InvalidOperationException("sounds.json not found inside the backup.");

        List<SoundItem> imported;
        using (var reader = new StreamReader(jsonEntry.Open()))
        {
            var json = reader.ReadToEnd();
            imported = JsonSerializer.Deserialize<List<SoundItem>>(json, _jsonOptions)
                ?? throw new InvalidOperationException("sounds.json in the backup is empty or invalid.");
        }

        // Build conflict-detection sets from the current library + existing StopAll hotkey.
        var existingIds  = new HashSet<Guid>(currentLibrary.Select(x => x.Id));
        var takenHotkeys = new HashSet<(uint Mod, uint Key)>();
        foreach (var existing in currentLibrary)
        {
            if (existing.Hotkey is not null)
                takenHotkeys.Add((existing.Hotkey.Modifiers, existing.Hotkey.Key));
        }
        if (stopAllHotkey is not null)
            takenHotkeys.Add((stopAllHotkey.Modifiers, stopAllHotkey.Key));

        Directory.CreateDirectory(AppPaths.SoundsDirectory);

        foreach (var item in imported)
        {
            // Skip sounds already in the library (same Guid).
            if (existingIds.Contains(item.Id))
            {
                result.SkippedDuplicates++;
                continue;
            }

            var audioFileName = Path.GetFileName(item.FilePath);
            if (string.IsNullOrEmpty(audioFileName))
            {
                result.Errors.Add($"Skipped '{item.DisplayName}': no audio filename.");
                continue;
            }

            var audioEntry = zip.GetEntry($"Sounds/{audioFileName}");
            if (audioEntry is null)
            {
                result.Errors.Add($"Skipped '{item.DisplayName}': audio file missing from backup.");
                continue;
            }

            string destPath;
            try
            {
                destPath = DeduplicatePath(Path.Combine(AppPaths.SoundsDirectory, audioFileName));
                audioEntry.ExtractToFile(destPath, overwrite: false);
                item.FilePath = destPath;
            }
            catch (Exception ex)
            {
                result.Errors.Add($"Skipped '{item.DisplayName}': {ex.Message}");
                continue;
            }

            // Check hotkey conflicts against both existing library and already-accepted imports.
            if (item.Hotkey is not null)
            {
                var hk = (item.Hotkey.Modifiers, item.Hotkey.Key);
                if (takenHotkeys.Contains(hk))
                {
                    item.Hotkey = null;
                    result.ClearedHotkeys++;
                }
                else
                {
                    takenHotkeys.Add(hk);
                }
            }

            result.NewItems.Add(item);
            existingIds.Add(item.Id);
        }

        return result;
    }

    private static string DeduplicatePath(string path)
    {
        if (!File.Exists(path)) return path;
        var dir  = Path.GetDirectoryName(path)!;
        var stem = Path.GetFileNameWithoutExtension(path);
        var ext  = Path.GetExtension(path);
        for (int i = 1; i < 10000; i++)
        {
            var candidate = Path.Combine(dir, $"{stem}_{i}{ext}");
            if (!File.Exists(candidate)) return candidate;
        }
        return path;
    }
}

public class ImportResult
{
    public List<SoundItem> NewItems          { get; } = new();
    public int             SkippedDuplicates { get; set; }
    public int             ClearedHotkeys   { get; set; }
    public List<string>    Errors           { get; } = new();
}
