namespace SoundPad.App.Models;

// A captured global hotkey. Modifiers/Key use the same numeric values as
// System.Windows.Input.ModifierKeys and the Win32 virtual-key code, so they
// can be passed directly to RegisterHotKey without conversion.
public class HotkeyBinding
{
    public uint   Modifiers   { get; set; }
    public uint   Key         { get; set; }
    public string DisplayText { get; set; } = "";
}
