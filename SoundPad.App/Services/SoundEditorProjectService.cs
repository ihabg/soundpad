using System.IO;
using System.Text.Json;
using SoundPad.App.Models;

namespace SoundPad.App.Services;

internal static class SoundEditorProjectService
{
    internal static string DraftPath => Path.Combine(AppPaths.AppDataDir, "Editor", "draft.json");

    internal static SoundEditorProject? Load(string? path = null)
    {
        path ??= DraftPath;
        if (!File.Exists(path)) return null;
        var project = JsonSerializer.Deserialize<SoundEditorProject>(File.ReadAllText(path))
            ?? throw new InvalidDataException("The editor draft is empty.");
        if (project.Tracks is null || project.Sources is null || project.Tracks.Count > 8)
            throw new InvalidDataException("The editor draft has invalid tracks or sources.");
        if (project.Tracks.Any(t => t is null || t.Clips is null || !float.IsFinite(t.Volume)
            || t.Clips.Any(c => c is null || !double.IsFinite(c.StartOffsetSeconds) || c.StartOffsetSeconds < 0
                || !float.IsFinite(c.Volume) || !double.IsFinite(c.FadeInSeconds) || !double.IsFinite(c.FadeOutSeconds))))
            throw new InvalidDataException("The editor draft contains invalid clip values.");
        return project;
    }

    internal static void Save(SoundEditorProject project, string? path = null)
    {
        path ??= DraftPath;
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        string temporary = path + ".tmp";
        File.WriteAllText(temporary, JsonSerializer.Serialize(project, new JsonSerializerOptions { WriteIndented = true }));
        File.Move(temporary, path, overwrite: true);
    }
}
