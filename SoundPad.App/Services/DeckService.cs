using System.Diagnostics;
using System.IO;
using System.Text.Json;
using SoundPad.App.Models;

namespace SoundPad.App.Services;

public static class DeckService
{
    private static readonly JsonSerializerOptions _jsonOptions = new() { WriteIndented = true };

    // Loads decks from decks.json. If absent, migrates sounds.json into a General
    // deck non-destructively (saves decks.json first, then renames sounds.json to a
    // .bak file). Falls back to a fresh General deck if both files are missing.
    // A corrupt or empty decks.json is renamed to a timestamped backup before
    // falling through, so user data is never silently overwritten.
    public static List<Deck> Load(AppSettings settings)
    {
        if (File.Exists(AppPaths.DecksJsonPath))
        {
            try
            {
                var json  = File.ReadAllText(AppPaths.DecksJsonPath);
                var decks = JsonSerializer.Deserialize<List<Deck>>(json, _jsonOptions);

                if (decks is { Count: > 0 })
                {
                    SanitizeDecks(decks);
                    return decks;
                }

                // Valid JSON but empty array — treat as corrupt so we don't lose data.
                BackupAndRemove(AppPaths.DecksJsonPath, "decks.json was an empty list");
            }
            catch (Exception ex)
            {
                BackupAndRemove(AppPaths.DecksJsonPath, $"decks.json was invalid ({ex.Message})");
            }
        }

        if (File.Exists(AppPaths.SoundsJsonPath))
        {
            try
            {
                return MigrateFromSoundsJson(settings);
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[DeckService] Migration from sounds.json failed: {ex.Message}");
            }
        }

        return new List<Deck> { new Deck { Name = "General" } };
    }

    // Renames a bad decks.json to a timestamped backup instead of deleting or
    // silently overwriting it. Logs the result to the debug output.
    private static void BackupAndRemove(string path, string reason)
    {
        var stamp  = DateTime.UtcNow.ToString("yyyyMMddHHmmss");
        var backup = path + $".bak_{stamp}";
        try
        {
            File.Move(path, backup);
            Debug.WriteLine($"[DeckService] {reason}; backed up to {backup}");
        }
        catch (Exception moveEx)
        {
            Debug.WriteLine($"[DeckService] {reason}; could not back up ({moveEx.Message})");
        }
    }

    // Guards against corrupt/hand-edited JSON where null replaces a list field,
    // since System.Text.Json ignores C# property initializers for explicit nulls.
    private static void SanitizeDecks(List<Deck> decks)
    {
        foreach (var d in decks)
        {
            d.Sounds           ??= new();
            d.CustomCategories ??= new();
        }
    }

    private static List<Deck> MigrateFromSoundsJson(AppSettings settings)
    {
        var json   = File.ReadAllText(AppPaths.SoundsJsonPath);
        var sounds = JsonSerializer.Deserialize<List<SoundItem>>(json, _jsonOptions) ?? new();

        var generalDeck = new Deck
        {
            Name             = "General",
            Sounds           = sounds,
            CustomCategories = settings.CustomCategories.ToList()
        };

        var decks = new List<Deck> { generalDeck };

        // Save decks.json FIRST so existing data is safe before renaming sounds.json.
        Save(decks);

        var timestamp  = DateTime.UtcNow.ToString("yyyyMMddHHmmss");
        var backupPath = AppPaths.SoundsJsonPath + $".v1.3.bak_{timestamp}";
        File.Move(AppPaths.SoundsJsonPath, backupPath);
        Debug.WriteLine($"[DeckService] Migrated sounds.json → {backupPath}");

        return decks;
    }

    public static void Save(List<Deck> decks)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(AppPaths.DecksJsonPath)!);
        File.WriteAllText(AppPaths.DecksJsonPath, JsonSerializer.Serialize(decks, _jsonOptions));
    }
}
