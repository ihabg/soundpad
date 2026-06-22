namespace SoundPad.App.Models;

public class Deck
{
    public Guid            Id               { get; set; } = Guid.NewGuid();
    public string          Name             { get; set; } = "General";
    public DateTime        CreatedAt        { get; set; } = DateTime.UtcNow;
    public List<SoundItem> Sounds           { get; set; } = new();
    public List<string>    CustomCategories { get; set; } = new();
}
