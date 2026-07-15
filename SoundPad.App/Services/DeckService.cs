using System.Diagnostics;
using System.IO;
using System.Text.Json;
using SoundPad.App.Models;

namespace SoundPad.App.Services;

public static class DeckService
{
    private static readonly JsonSerializerOptions _jsonOptions = new() { WriteIndented = true };

    
    
    
    
    
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
