namespace SoundPad.App.Hotkeys;

public interface IHotkeyManager
{
    event Action<int>? HotkeyPressed;
    bool Register(int id, uint modifiers, uint virtualKey);
    void UnregisterAll();
}
