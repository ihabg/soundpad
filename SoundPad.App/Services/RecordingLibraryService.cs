using System.IO;
using System.Text.Json;
using NAudio.Wave;
using SoundPad.App.Models;

namespace SoundPad.App.Services;

internal static class RecordingLibraryService
{
    internal static string DirectoryPath => Path.Combine(AppPaths.AppDataDir, "Recordings");
    internal static List<RecordingClip> Load(string? directory = null)
    {
        directory ??= DirectoryPath;
        var indexPath = Path.Combine(directory, "recordings.json");
        Directory.CreateDirectory(directory);
        List<RecordingClip> clips = [];
        if (File.Exists(indexPath))
        {
            try { clips = JsonSerializer.Deserialize<List<RecordingClip>>(File.ReadAllText(indexPath)) ?? []; }
            catch (JsonException)
            {
                // Preserve damaged metadata; recover the original audio below.
                File.Copy(indexPath, indexPath + $".bak-{DateTime.UtcNow:yyyyMMddHHmmssfff}");
            }
        }
        clips.RemoveAll(c => c.Sound is null || !File.Exists(c.Sound.FilePath));
        var known = clips.Select(c => Path.GetFullPath(c.Sound.FilePath)).ToHashSet(StringComparer.OrdinalIgnoreCase);
        foreach (var path in Directory.EnumerateFiles(directory, "*.wav"))
        {
            if (known.Contains(Path.GetFullPath(path))) continue;
            try
            {
                using var reader = new WaveFileReader(path);
                var date = File.GetCreationTimeUtc(path);
                clips.Add(new RecordingClip
                {
                    Sound = new SoundItem { DisplayName = $"Recovered clip {date.ToLocalTime():MMM d HH:mm}", FilePath = path, CreatedAt = date, Category = "Clips" },
                    DurationSeconds = reader.TotalTime.TotalSeconds
                });
            }
            catch (Exception ex) { AppLogger.Error("Recording", $"Could not recover {path}", ex); }
        }
        return clips.OrderByDescending(c => c.Sound.CreatedAt).ToList();
    }

    internal static void Save(IReadOnlyList<RecordingClip> clips, string? directory = null)
    {
        directory ??= DirectoryPath;
        Directory.CreateDirectory(directory);
        var indexPath = Path.Combine(directory, "recordings.json");
        var temporary = indexPath + ".tmp";
        File.WriteAllText(temporary, JsonSerializer.Serialize(clips, new JsonSerializerOptions { WriteIndented = true }));
        File.Move(temporary, indexPath, overwrite: true);
    }

    internal static void Delete(IReadOnlyList<RecordingClip> clips, RecordingClip clip, string? directory = null)
    {
        directory ??= DirectoryPath;
        var path = Path.GetFullPath(clip.Sound.FilePath);
        var root = Path.GetFullPath(directory);
        if (!string.Equals(Path.GetDirectoryName(path), root, StringComparison.OrdinalIgnoreCase)
            || !string.Equals(Path.GetExtension(path), ".wav", StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("Only audio in the recording folder can be deleted here.");

        // Stage the file outside the WAV recovery scan. Restore it if saving
        // the updated history fails, so deletion cannot leave a broken entry.
        var staged = path + ".deleting-" + Guid.NewGuid();
        bool moved = File.Exists(path);
        if (moved) File.Move(path, staged);
        try { Save(clips.Where(c => c.Sound.Id != clip.Sound.Id).ToList(), directory); }
        catch
        {
            if (moved) File.Move(staged, path);
            throw;
        }
        if (moved)
        {
            try { File.Delete(staged); }
            catch (Exception ex) { AppLogger.Error("Recording", "Could not clean up deleted recording", ex); }
        }
    }
}
