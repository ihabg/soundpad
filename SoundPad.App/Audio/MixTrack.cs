namespace SoundPad.App.Audio;

// A named timeline row/layer in the Sound Composer.
// Each row can hold multiple MixClip blocks.
public sealed class MixTrack
{
    public Guid          Id     { get; set; } = Guid.NewGuid();
    public string        Label  { get; set; } = "";

    // Per-track gain. Applied as Volume^2 (squared) to match main playback engine.
    public float         Volume { get; set; } = 1.0f;

    public List<MixClip> Clips  { get; set; } = new();
}
