using SoundPad.App.Audio;

namespace SoundPad.App.Models;

public sealed class SoundEditorProject
{
    public string Name { get; set; } = "Untitled Mix";
    public List<MixTrack> Tracks { get; set; } = [];
    public List<SoundItem> Sources { get; set; } = [];
    public double Zoom { get; set; } = 100;
    public double PlayheadSeconds { get; set; }
}
