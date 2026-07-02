namespace SoundPad.App.Audio;

// One sound block placed on a MixTrack (timeline row).
// Holds its own timeline position and optional local trim — the original
// SoundItem is never modified.
public sealed class MixClip
{
    public Guid    Id                   { get; set; } = Guid.NewGuid();
    public Guid    SoundItemId          { get; set; } = Guid.Empty;

    // Position of the clip's left edge in the timeline (seconds, >= 0).
    public double  StartOffsetSeconds   { get; set; } = 0.0;

    // Local trim applied after EffectProcessor.Render (into the rendered clip).
    // null means no trim at that end.
    public double? LocalTrimStartSeconds { get; set; }
    public double? LocalTrimEndSeconds   { get; set; }

    // Per-clip gain. Applied as Volume^2 (squared) to match main playback engine.
    public float   Volume               { get; set; } = 1.0f;
    public double  FadeInSeconds        { get; set; } = 0.0;
    public double  FadeOutSeconds       { get; set; } = 0.0;
}
