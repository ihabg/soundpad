namespace SoundPad.App.Models;

public sealed class RecordingClip
{
    public SoundItem Sound { get; set; } = new();
    public double DurationSeconds { get; set; }
    public string Source { get; set; } = "System audio";
    public float[] Peaks { get; set; } = [];
}
