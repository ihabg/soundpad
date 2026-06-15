using System.IO;

namespace SoundPad.App.Services;

public static class AppPaths
{
    private static readonly string _base = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
        "SoundPad");

    public static string SoundsDirectory => Path.Combine(_base, "Sounds");
    public static string SoundsJsonPath  => Path.Combine(_base, "sounds.json");
}
