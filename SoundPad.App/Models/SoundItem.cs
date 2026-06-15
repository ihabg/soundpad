namespace SoundPad.App.Models;

public class SoundItem
{
    public Guid     Id          { get; set; } = Guid.NewGuid();
    public string   DisplayName { get; set; } = "";
    public string   FilePath    { get; set; } = "";
    public DateTime CreatedAt   { get; set; } = DateTime.UtcNow;
    public string?  HotkeyText  { get; set; }
    public string   Category    { get; set; } = "General";
    public float    Volume      { get; set; } = 1.0f;
}
