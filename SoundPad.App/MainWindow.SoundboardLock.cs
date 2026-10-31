using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using SoundPad.App.Dialogs;
using SoundPad.App.Models;

namespace SoundPad.App;

public partial class MainWindow
{
    private void SoundboardLock_Click(object sender, RoutedEventArgs e) => ToggleSoundboardLock();

    private void ToggleSoundboardLock()
    {
        _settings.SoundboardLocked = !_settings.SoundboardLocked;
        var result = ReregisterAllHotkeysAndReport("soundboard-lock");
        SaveSettings();
        RefreshSoundboardLockUI();
        if (_settings.SoundboardLocked)
            StatusText.Text = "SoundPad shortcuts locked — your game can use those keys. Click the lock or use your lock shortcut to unlock.";
        else if (result.FailedSoundIds.Count == 0 && !result.StopAllFailed && !result.InstantReplayClipFailed && !result.InstantReplayToggleFailed)
            StatusText.Text = "SoundPad shortcuts unlocked — hotkeys are active again.";
        if (result.SoundboardLockFailed)
            StatusText.Text += " The lock shortcut is unavailable; use the on-screen lock button.";
    }

    private void RefreshSoundboardLockUI()
    {
        if (SoundboardLockButton is null) return;
        bool locked = _settings.SoundboardLocked;
        SoundboardLockIcon.Data = Geometry.Parse(locked ? "M 6,8 V 5 A 4,4 0 0 1 14,5 V 8" : "M 6,8 V 4 A 4,4 0 0 1 14,4");
        SoundboardLockButton.Background = (Brush)FindResource(locked ? "ComicYellowBrush" : "ComicCyanBrush");
        string state = locked ? "Locked" : "Unlocked";
        SoundboardLockStateText.Text = locked ? "LOCKED" : "";
        SoundboardLockStateText.Visibility = locked ? Visibility.Visible : Visibility.Collapsed;
        SoundboardLockStateText.Foreground = (Brush)FindResource("ComicYellowBrush");
        string shortcut = _settings.SoundboardLockHotkey?.DisplayText ?? "Not assigned";
        SoundboardLockButton.ToolTip = $"Shortcuts {state.ToLowerInvariant()}. Click to toggle. Lock shortcut: {shortcut}. Right-click to set a shortcut.";
        System.Windows.Automation.AutomationProperties.SetName(SoundboardLockButton, $"Soundboard shortcuts {state.ToLowerInvariant()}");
        SoundboardLockShortcutText.Text = shortcut;
        ClearSoundboardLockShortcutButton.IsEnabled = _settings.SoundboardLockHotkey is not null;
    }

    private void SetSoundboardLockShortcut_Click(object sender, RoutedEventArgs e)
    {
        if (_hotkeyService is null) { StatusText.Text = "Global shortcuts are unavailable in this session."; return; }
        var dialog = new HotkeyCaptureDialog(this, "Soundboard lock", _settings.SoundboardLockHotkey);
        bool? accepted;
        _hotkeyService.UnregisterAll();
        try { accepted = dialog.ShowDialog(); }
        finally { ReregisterAllHotkeysAndReport("lock-capture-closed"); }
        if (accepted != true) return;
        if (dialog.WasCleared) { ClearSoundboardLockShortcut_Click(sender, e); return; }
        var binding = dialog.ResultBinding;
        if (binding is null) return;
        bool Matches(HotkeyBinding? other) => other is not null && other.Key == binding.Key && (other.Modifiers & 0xF) == (binding.Modifiers & 0xF);
        string? conflict = _decks.SelectMany(d => d.Sounds).FirstOrDefault(s => Matches(s.Hotkey))?.DisplayName;
        if (Matches(_settings.StopAllHotkey)) conflict = "Stop All Sounds";
        if (Matches(_settings.InstantReplayClipHotkey)) conflict = "Save replay clip";
        if (Matches(_settings.InstantReplayToggleHotkey)) conflict = "Toggle Instant Replay";
        if (conflict is not null)
        {
            StatusText.Text = $"{binding.DisplayText} is already assigned to {conflict}. Choose another lock shortcut.";
            return;
        }
        var previous = _settings.SoundboardLockHotkey;
        _settings.SoundboardLockHotkey = binding;
        var result = ReregisterAllHotkeysAndReport("lock-shortcut-set");
        if (result.SoundboardLockFailed)
        {
            _settings.SoundboardLockHotkey = previous;
            ReregisterAllHotkeysAndReport("lock-shortcut-rollback");
            StatusText.Text = "Windows could not register that lock shortcut. Your previous shortcut was kept.";
            return;
        }
        SaveSettings();
        RefreshSoundboardLockUI();
        StatusText.Text = $"Soundboard lock shortcut: {binding.DisplayText}. Works while SoundPad is in the tray.";
    }

    private void ClearSoundboardLockShortcut_Click(object sender, RoutedEventArgs e)
    {
        _settings.SoundboardLockHotkey = null;
        ReregisterAllHotkeysAndReport("lock-shortcut-clear");
        SaveSettings();
        RefreshSoundboardLockUI();
        StatusText.Text = "Lock shortcut cleared. The on-screen lock button still works.";
    }
}
