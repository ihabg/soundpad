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
}
