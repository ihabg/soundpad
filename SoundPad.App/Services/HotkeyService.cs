using SoundPad.App.Hotkeys;
using SoundPad.App.Models;

namespace SoundPad.App.Services;


public class HotkeyRegistrationResult
{
    public HashSet<Guid> FailedSoundIds            { get; } = new HashSet<Guid>();
    public bool           StopAllFailed            { get; set; }
    public bool           InstantReplayClipFailed   { get; set; }
    public bool           InstantReplayToggleFailed { get; set; }
}




public class HotkeyService
{
    private const int InstantReplayClipId   = 9097; 
    private const int InstantReplayToggleId = 9098; 
    private const int StopAllId             = 9099;
    private const int BaseId                = 9100; 

    private readonly HotkeyManager _manager;
    private readonly Dictionary<int, Guid> _idToSoundId = new Dictionary<int, Guid>();

    
    public event Action<Guid>? HotkeyTriggered;

    
    public event Action? StopAllHotkeyTriggered;

    
    public event Action? InstantReplayClipTriggered;

    
    public event Action? InstantReplayToggleTriggered;

    public HotkeyService(HotkeyManager manager)
    {
        _manager = manager;
        _manager.HotkeyPressed += OnHotkeyPressed;
    }

    private void OnHotkeyPressed(int id)
    {
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
        HotkeyBinding?         instantReplayToggleHotkey = null)
    {
        UnregisterAll();

        var result = new HotkeyRegistrationResult();

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
