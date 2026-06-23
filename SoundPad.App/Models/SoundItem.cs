namespace SoundPad.App.Models;

public class SoundItem
{
    public Guid           Id                { get; set; } = Guid.NewGuid();
    public string         DisplayName       { get; set; } = "";
    public string         FilePath          { get; set; } = "";
    public DateTime       CreatedAt         { get; set; } = DateTime.UtcNow;
    public string         Category          { get; set; } = "General";
    public float          Volume            { get; set; } = 1.0f;
    public HotkeyBinding? Hotkey            { get; set; }

    // True once this item has gone through hotkey migration/assignment at least
    // once (whether or not a hotkey ended up bound). Prevents re-seeding a
    // default hotkey after the user deliberately clears it.
    public bool            HotkeyInitialized { get; set; } = false;

    public bool            IsFavorite        { get; set; } = false;
    public DateTime?       LastPlayedAt      { get; set; }

    // Non-destructive edit settings — applied at playback time only.
    // The original audio file is never modified.
    // All four fields are nullable so old sounds.json files that omit them
    // deserialize to null, preserving v1.1.0 playback behaviour exactly.
    public double? TrimStartSeconds { get; set; }
    public double? TrimEndSeconds   { get; set; }
    public double? FadeInSeconds    { get; set; }
    public double? FadeOutSeconds   { get; set; }

    // Optional pad color shown in Grid View — stored as "#RRGGBB" hex string, null = default card color.
    // Nullable so old decks.json files without this field deserialize cleanly to null.
    public string? PadColor { get; set; }
}
