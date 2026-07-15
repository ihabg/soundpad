namespace SoundPad.App.Audio;




public sealed class MixClip
{
    public Guid    Id                   { get; set; } = Guid.NewGuid();
    public Guid    SoundItemId          { get; set; } = Guid.Empty;

    
    public double  StartOffsetSeconds   { get; set; } = 0.0;

    
    
    public double? LocalTrimStartSeconds { get; set; }
    public double? LocalTrimEndSeconds   { get; set; }

    
    public float   Volume               { get; set; } = 1.0f;
    public double  FadeInSeconds        { get; set; } = 0.0;
    public double  FadeOutSeconds       { get; set; } = 0.0;
}
