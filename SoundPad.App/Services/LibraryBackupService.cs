using System.IO;
using System.IO.Compression;
using System.Text.Json;
using SoundPad.App.Models;

namespace SoundPad.App.Services;

public static class LibraryBackupService
{
    private static readonly JsonSerializerOptions _jsonOptions = new() { WriteIndented = true };

    // ── Export ────────────────────────────────────────────────────────────────

    // Writes decks.json (full deck structure) + sounds.json (flat, backward compat)
    // + Sounds/ folder with all audio files.
    public static void Export(IReadOnlyList<Deck> decks, string zipPath)
    {
        // Build sanitized deck copies: FilePath → filename only.
        var exportDecks = decks.Select(deck => new Deck
        {
            Id               = deck.Id,
            Name             = deck.Name,
            CreatedAt        = deck.CreatedAt,
            CustomCategories = deck.CustomCategories.ToList(),
            Sounds           = deck.Sounds.Select(s => new SoundItem
            {
                Id                = s.Id,
                DisplayName       = s.DisplayName,
                FilePath          = Path.GetFileName(s.FilePath),
                CreatedAt         = s.CreatedAt,
                Category          = s.Category,
                Volume            = s.Volume,
                Hotkey            = s.Hotkey,
                HotkeyInitialized = s.HotkeyInitialized,
                IsFavorite        = s.IsFavorite,
                LastPlayedAt      = s.LastPlayedAt,
                TrimStartSeconds  = s.TrimStartSeconds,
                TrimEndSeconds    = s.TrimEndSeconds,
                FadeInSeconds     = s.FadeInSeconds,
                FadeOutSeconds    = s.FadeOutSeconds,
                PadColor          = s.PadColor,
                Tags              = s.Tags?.ToList()
            }).ToList()
        }).ToList();

        // Flat list of all sounds across all decks for backward compat.
        var allSounds = exportDecks.SelectMany(d => d.Sounds).ToList();

        if (File.Exists(zipPath)) File.Delete(zipPath);
        using var zip = ZipFile.Open(zipPath, ZipArchiveMode.Create);

        var decksEntry = zip.CreateEntry("decks.json", CompressionLevel.Optimal);
        using (var w = new StreamWriter(decksEntry.Open()))
            w.Write(JsonSerializer.Serialize(exportDecks, _jsonOptions));

        var soundsEntry = zip.CreateEntry("sounds.json", CompressionLevel.Optimal);
        using (var w = new StreamWriter(soundsEntry.Open()))
            w.Write(JsonSerializer.Serialize(allSounds, _jsonOptions));

        var addedFiles = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var deck in decks)
        {
            foreach (var src in deck.Sounds)
            {
                if (!File.Exists(src.FilePath)) continue;
                var fileName = Path.GetFileName(src.FilePath);
                if (!addedFiles.Add(fileName)) continue;
                zip.CreateEntryFromFile(src.FilePath, $"Sounds/{fileName}", CompressionLevel.Optimal);
            }
        }
    }

    // ── Import ────────────────────────────────────────────────────────────────

    // Detects backup format from the ZIP contents:
    //   decks.json present  → new format: merge decks into currentDecks
    //   sounds.json only    → old format: add sounds into activeDeck
    public static ImportResult Import(string zipPath,
                                      List<Deck> currentDecks,
                                      Deck activeDeck,
                                      HotkeyBinding? stopAllHotkey)
    {
        var result = new ImportResult();

        using var zip = ZipFile.OpenRead(zipPath);
        Directory.CreateDirectory(AppPaths.SoundsDirectory);

        var decksEntry = zip.GetEntry("decks.json");
        if (decksEntry is not null)
        {
            ImportNewFormat(zip, decksEntry, currentDecks, stopAllHotkey, result);
        }
        else
        {
            var soundsEntry = zip.GetEntry("sounds.json")
                ?? throw new InvalidOperationException("sounds.json not found inside the backup.");
            ImportOldFormat(zip, soundsEntry, activeDeck, currentDecks, stopAllHotkey, result);
        }

        return result;
    }

    private static void ImportNewFormat(ZipArchive zip, ZipArchiveEntry decksEntry,
                                        List<Deck> currentDecks, HotkeyBinding? stopAllHotkey,
                                        ImportResult result)
    {
        List<Deck> importedDecks;
        using (var reader = new StreamReader(decksEntry.Open()))
        {
            var json = reader.ReadToEnd();
            importedDecks = JsonSerializer.Deserialize<List<Deck>>(json, _jsonOptions)
                ?? throw new InvalidOperationException("decks.json in the backup is empty or invalid.");
        }

        foreach (var importedDeck in importedDecks)
        {
            var target = currentDecks.FirstOrDefault(d =>
                string.Equals(d.Name, importedDeck.Name, StringComparison.OrdinalIgnoreCase));

            bool isNew = target is null;
            if (isNew)
            {
                target = new Deck
                {
                    Name             = importedDeck.Name,
                    CustomCategories = importedDeck.CustomCategories.ToList()
                };
                currentDecks.Add(target);
                result.DecksAdded++;
            }
            else
            {
                // Merge any new categories.
                foreach (var cat in importedDeck.CustomCategories)
                {
                    if (!target!.CustomCategories.Any(c =>
                            string.Equals(c, cat, StringComparison.OrdinalIgnoreCase)))
                        target.CustomCategories.Add(cat);
                }
                result.DecksMerged++;
            }

            // Per-deck conflict sets so hotkeys between different decks don't interfere.
            var takenHotkeys = BuildTakenHotkeys(target!.Sounds, stopAllHotkey);
            var existingIds  = new HashSet<Guid>(target.Sounds.Select(x => x.Id));

            foreach (var item in importedDeck.Sounds)
                ImportSoundItem(zip, item, target, existingIds, takenHotkeys, result);
        }
    }

    private static void ImportOldFormat(ZipArchive zip, ZipArchiveEntry soundsEntry,
                                        Deck activeDeck, List<Deck> currentDecks,
                                        HotkeyBinding? stopAllHotkey,
                                        ImportResult result)
    {
        List<SoundItem> imported;
        using (var reader = new StreamReader(soundsEntry.Open()))
        {
            var json = reader.ReadToEnd();
            imported = JsonSerializer.Deserialize<List<SoundItem>>(json, _jsonOptions)
                ?? throw new InvalidOperationException("sounds.json in the backup is empty or invalid.");
        }

        var takenHotkeys = BuildTakenHotkeys(activeDeck.Sounds, stopAllHotkey);
        // Check cross-deck duplicates: a GUID that exists in any deck is skipped,
        // even though the sound would be added to activeDeck only.
        var existingIds  = new HashSet<Guid>(currentDecks.SelectMany(d => d.Sounds).Select(x => x.Id));

        foreach (var item in imported)
            ImportSoundItem(zip, item, activeDeck, existingIds, takenHotkeys, result);
    }

    private static void ImportSoundItem(ZipArchive zip, SoundItem item, Deck targetDeck,
                                        HashSet<Guid> existingIds,
                                        HashSet<(uint Mod, uint Key)> takenHotkeys,
                                        ImportResult result)
    {
        if (existingIds.Contains(item.Id))
        {
            result.SkippedDuplicates++;
            return;
        }

        var audioFileName = Path.GetFileName(item.FilePath);
        if (string.IsNullOrEmpty(audioFileName))
        {
            result.Errors.Add($"Skipped '{item.DisplayName}': no audio filename.");
            return;
        }

        var audioEntry = zip.GetEntry($"Sounds/{audioFileName}");
        if (audioEntry is null)
        {
            result.Errors.Add($"Skipped '{item.DisplayName}': audio file missing from backup.");
            return;
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
            return;
        }

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

        targetDeck.Sounds.Add(item);
        result.AllNewSounds.Add(item);
        existingIds.Add(item.Id);
    }

    private static HashSet<(uint Mod, uint Key)> BuildTakenHotkeys(
        IEnumerable<SoundItem> sounds, HotkeyBinding? stopAllHotkey)
    {
        var taken = new HashSet<(uint, uint)>();
        foreach (var s in sounds)
            if (s.Hotkey is not null)
                taken.Add((s.Hotkey.Modifiers, s.Hotkey.Key));
        if (stopAllHotkey is not null)
            taken.Add((stopAllHotkey.Modifiers, stopAllHotkey.Key));
        return taken;
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
    public List<SoundItem> AllNewSounds      { get; } = new();
    public int             SkippedDuplicates { get; set; }
    public int             ClearedHotkeys   { get; set; }
    public int             DecksAdded       { get; set; }
    public int             DecksMerged      { get; set; }
    public List<string>    Errors           { get; } = new();
}
