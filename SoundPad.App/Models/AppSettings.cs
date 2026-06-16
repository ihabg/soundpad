namespace SoundPad.App.Models;

// Persistent app-level settings, distinct from the sound library itself.
// Kept intentionally small; add new settings here as they're needed.
public class AppSettings
{
    public HotkeyBinding? StopAllHotkey { get; set; }
}
