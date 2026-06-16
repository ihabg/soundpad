using SoundPad.App.Hotkeys;
using SoundPad.App.Models;

namespace SoundPad.App.Services;

// Bridges the sound library's per-sound HotkeyBinding data to the Win32-level
// HotkeyManager. Owns the mapping from registered hotkey IDs back to the
// SoundItem.Id that owns each binding, so a WM_HOTKEY message can be resolved
// to the correct sound regardless of its position in the library.
public class HotkeyService
{
    private const int BaseId = 9100;

    private readonly HotkeyManager _manager;
    private readonly Dictionary<int, Guid> _idToSoundId = new Dictionary<int, Guid>();

    // Fired on the UI thread with the SoundItem.Id whose hotkey was pressed.
    public event Action<Guid>? HotkeyTriggered;

    public HotkeyService(HotkeyManager manager)
    {
        _manager = manager;
        _manager.HotkeyPressed += OnHotkeyPressed;
    }

    private void OnHotkeyPressed(int id)
    {
        if (_idToSoundId.TryGetValue(id, out var soundId))
            HotkeyTriggered?.Invoke(soundId);
    }

    // Unregisters every previously registered hotkey, then registers every
    // item in the library that currently has a HotkeyBinding.
    // Returns the set of SoundItem.Id values whose binding could not be
    // registered at the OS level (e.g. already owned by another application).
    public HashSet<Guid> ReregisterAll(IEnumerable<SoundItem> library)
    {
        UnregisterAll();

        var failed = new HashSet<Guid>();
        int nextId = BaseId;

        foreach (var item in library)
        {
            if (item.Hotkey is null)
                continue;

            int id = nextId++;
            bool ok = _manager.Register(id, item.Hotkey.Modifiers, item.Hotkey.Key);
            if (ok)
                _idToSoundId[id] = item.Id;
            else
                failed.Add(item.Id);
        }

        return failed;
    }

    public void UnregisterAll()
    {
        _manager.UnregisterAll();
        _idToSoundId.Clear();
    }
}
