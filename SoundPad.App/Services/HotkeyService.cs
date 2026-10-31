using SoundPad.App.Hotkeys;
using SoundPad.App.Models;

namespace SoundPad.App.Services;


public class HotkeyRegistrationResult
{
    public HashSet<Guid> FailedSoundIds            { get; } = new HashSet<Guid>();
    public bool           StopAllFailed            { get; set; }
    public bool           InstantReplayClipFailed   { get; set; }
    public bool           InstantReplayToggleFailed { get; set; }
    public bool           SoundboardLockFailed      { get; set; }
}




public class HotkeyService
{
    private const int SoundboardLockId = 9096;
    private const int InstantReplayClipId   = 9097; 
    private const int InstantReplayToggleId = 9098; 
    private const int StopAllId             = 9099;
    private const int BaseId                = 9100; 

    private readonly IHotkeyManager _manager;
    private bool _locked;
    private readonly Dictionary<int, Guid> _idToSoundId = new Dictionary<int, Guid>();

    
    public event Action<Guid>? HotkeyTriggered;

    
    public event Action? StopAllHotkeyTriggered;

    
    public event Action? InstantReplayClipTriggered;

    
    public event Action? InstantReplayToggleTriggered;
    public event Action? SoundboardLockTriggered;

    public HotkeyService(IHotkeyManager manager)
    {
        _manager = manager;
        _manager.HotkeyPressed += OnHotkeyPressed;
    }

    private void OnHotkeyPressed(int id)
    {
        if (id == SoundboardLockId) { SoundboardLockTriggered?.Invoke(); return; }
        // Ignore already-queued hotkey messages after registrations are released.
        if (_locked) return;
        if (id == StopAllId)             { StopAllHotkeyTriggered?.Invoke();         return; }
        if (id == InstantReplayClipId)   { InstantReplayClipTriggered?.Invoke();     return; }
        if (id == InstantReplayToggleId) { InstantReplayToggleTriggered?.Invoke();   return; }

        if (_idToSoundId.TryGetValue(id, out var soundId))
            HotkeyTriggered?.Invoke(soundId);
    }

    
    
    
    
    public HotkeyRegistrationResult ReregisterAll(
        IEnumerable<SoundItem> library,
        HotkeyBinding?         stopAllHotkey,
        HotkeyBinding?         instantReplayClipHotkey   = null,
        HotkeyBinding?         instantReplayToggleHotkey = null,
        HotkeyBinding?         soundboardLockHotkey = null,
        bool                   locked = false)
    {
        _locked = locked;
        UnregisterAll();

        var result = new HotkeyRegistrationResult();

        if (soundboardLockHotkey is not null)
        {
            // Prevent a held toggle key from repeatedly locking and unlocking.
            bool ok = _manager.Register(SoundboardLockId, soundboardLockHotkey.Modifiers | 0x4000, soundboardLockHotkey.Key);
            result.SoundboardLockFailed = !ok;
        }
        // Do not merely mute callbacks: unregister the combinations so Windows
        // delivers them to the foreground game. Only the unlock shortcut stays.
        if (_locked) return result;

        if (stopAllHotkey is not null)
        {
            bool ok = _manager.Register(StopAllId, stopAllHotkey.Modifiers, stopAllHotkey.Key);
            if (!ok) result.StopAllFailed = true;
        }

        if (instantReplayClipHotkey is not null)
        {
            bool ok = _manager.Register(InstantReplayClipId, instantReplayClipHotkey.Modifiers, instantReplayClipHotkey.Key);
            if (!ok) result.InstantReplayClipFailed = true;
        }

        if (instantReplayToggleHotkey is not null)
        {
            bool ok = _manager.Register(InstantReplayToggleId, instantReplayToggleHotkey.Modifiers, instantReplayToggleHotkey.Key);
            if (!ok) result.InstantReplayToggleFailed = true;
        }

        int nextId = BaseId;
        foreach (var item in library)
        {
            if (item.Hotkey is null) continue;
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
