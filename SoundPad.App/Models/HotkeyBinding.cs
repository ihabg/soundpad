namespace SoundPad.App.Models;




public class HotkeyBinding
{
    public uint   Modifiers   { get; set; }
    public uint   Key         { get; set; }
    public string DisplayText { get; set; } = "";
}
