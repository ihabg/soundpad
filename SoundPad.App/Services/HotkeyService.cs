using SoundPad.App.Hotkeys;
using SoundPad.App.Models;

namespace SoundPad.App.Services;

// Outcome of a ReregisterAll pass: which sound hotkeys failed to register at
// the OS level, and whether the Stop All hotkey did.
public class HotkeyRegistrationResult
{
    public HashSet<Guid> FailedSoundIds { get; } = new HashSet<Guid>();
    public bool           StopAllFailed { get; set; }
}

// Bridges the sound library's per-sound HotkeyBinding data (and the app-level
// Stop All hotkey) to the Win32-level HotkeyManager. Owns the mapping from
// registered hotkey IDs back to the SoundItem.Id that owns each binding, so a
// WM_HOTKEY message can be resolved to the correct sound regardless of its
// position in the library. The Stop All hotkey uses a single reserved ID.
public class HotkeyService
{
    private const int StopAllId = 9099;
    private const int BaseId    = 9100;

    private readonly HotkeyManager _manager;
    private readonly Dictionary<int, Guid> _idToSoundId = new Dictionary<int, Guid>();

    // Fired on the UI thread with the SoundItem.Id whose hotkey was pressed.
    public event Action<Guid>? HotkeyTriggered;

    // Fired on the UI thread when the Stop All hotkey is pressed.
    public event Action? StopAllHotkeyTriggered;

    public HotkeyService(HotkeyManager manager)
    {
        _manager = manager;
        _manager.HotkeyPressed += OnHotkeyPressed;
    }

    private void OnHotkeyPressed(int id)
    {
        if (id == StopAllId)
        {
            StopAllHotkeyTriggered?.Invoke();
            return;
        }

        if (_idToSoundId.TryGetValue(id, out var soundId))
            HotkeyTriggered?.Invoke(soundId);
    }

    // Unregisters every previously registered hotkey, then registers the Stop
    // All hotkey (if any) and every library item that currently has a
    // HotkeyBinding. Returns which of those failed to register at the OS
    // level (e.g. already owned by another application).
    public HotkeyRegistrationResult ReregisterAll(IEnumerable<SoundItem> library, HotkeyBinding? stopAllHotkey)
    {
        UnregisterAll();

        var result = new HotkeyRegistrationResult();

        if (stopAllHotkey is not null)
        {
            bool ok = _manager.Register(StopAllId, stopAllHotkey.Modifiers, stopAllHotkey.Key);
            if (!ok)
                result.StopAllFailed = true;
        }

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
                result.FailedSoundIds.Add(item.Id);
        }

        return result;
    }

    public void UnregisterAll()
    {
        _manager.UnregisterAll();
        _idToSoundId.Clear();
    }
}
