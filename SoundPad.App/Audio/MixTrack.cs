namespace SoundPad.App.Audio;



public sealed class MixTrack
{
    public Guid          Id     { get; set; } = Guid.NewGuid();
    public string        Label  { get; set; } = "";

    
    public float         Volume { get; set; } = 1.0f;

    public List<MixClip> Clips  { get; set; } = new();
}
