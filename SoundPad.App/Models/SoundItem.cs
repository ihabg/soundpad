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

    
    
    
    public bool            HotkeyInitialized { get; set; } = false;

    public bool            IsFavorite        { get; set; } = false;
    public DateTime?       LastPlayedAt      { get; set; }

    
    
    
    
    public double? TrimStartSeconds { get; set; }
    public double? TrimEndSeconds   { get; set; }
    public double? FadeInSeconds    { get; set; }
    public double? FadeOutSeconds   { get; set; }

    
    
    public string? PadColor { get; set; }

    
    
    
    public List<AudioSegment>? Segments { get; set; }

    
    
    public List<string>? Tags { get; set; }

    
    
    
    public bool   ReverseAudio  { get; set; } = false;
    public bool   NormalizeAudio { get; set; } = false;
    public double PlaybackSpeed  { get; set; } = 1.0;
}
