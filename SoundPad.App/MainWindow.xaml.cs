using SoundPad.App.Audio;
using SoundPad.App.Hotkeys;
using System.IO;
using System.Windows;

namespace SoundPad.App;

public partial class MainWindow : Window
{
    private AudioPlaybackEngine?             _engine;
    private readonly Dictionary<string, CachedSound> _sounds = new();
    private HotkeyManager? _hotkeys;

    // Unique IDs for each hotkey. Any integers work; these are high enough
    // to avoid the system-reserved range (0x0000–0xBFFF is user-defined space).
    private const int HotkeyId1 = 9001;
    private const int HotkeyId2 = 9002;
    private const int HotkeyId3 = 9003;
    private const int HotkeyId4 = 9004;

    public MainWindow()
    {
        InitializeComponent();
        LoadSounds();
    }

    // Fired by WPF after the window is fully constructed and its HWND exists.
    // RegisterHotKey needs a valid window handle, so this is the right place.
    private void MainWindow_Loaded(object sender, RoutedEventArgs e)
    {
        RegisterHotkeys();
    }

    // ── Sound loading ─────────────────────────────────────────────────────────

    private void LoadSounds()
    {
        var fileNames = new[] { "sound1.mp3", "sound2.mp3", "sound3.mp3", "sound4.mp3" };

        try
        {
            _engine = new AudioPlaybackEngine();

            foreach (var name in fileNames)
            {
                var path = Path.Combine(AppContext.BaseDirectory, "Sounds", name);

                if (!File.Exists(path))
                {
                    StatusText.Text = $"Missing file: {path}";
                    return;
                }

                _sounds[name] = new CachedSound(path);
            }

            StatusText.Text = $"Sounds loaded ({_sounds.Count})";
        }
        catch (Exception ex)
        {
            StatusText.Text = $"Load error: {ex.Message}";
        }
    }

    // ── Hotkey registration ───────────────────────────────────────────────────

    private void RegisterHotkeys()
    {
        _hotkeys = new HotkeyManager(this);
        _hotkeys.HotkeyPressed += OnHotkeyPressed;

        // Virtual key codes for the number row (not the numpad):
        //   '1' = 0x31, '2' = 0x32, '3' = 0x33, '4' = 0x34
        var registrations = new (int Id, uint Vk, string Label)[]
        {
            (HotkeyId1, 0x31, "Ctrl+Alt+1"),
            (HotkeyId2, 0x32, "Ctrl+Alt+2"),
            (HotkeyId3, 0x33, "Ctrl+Alt+3"),
            (HotkeyId4, 0x34, "Ctrl+Alt+4"),
        };

        foreach (var (Id, Vk, Label) in registrations)
        {
            if (!_hotkeys.Register(Id, HotkeyManager.CtrlAlt, Vk))
            {
                // Another app already owns this key combination.
                StatusText.Text = $"Could not register {Label} — already in use by another app";
                return;
            }
        }

        // All four hotkeys registered. Update the status only when sounds also loaded fine.
        if (_sounds.Count == 4)
            StatusText.Text = "Ready — Ctrl+Alt+1..4 active";
    }

    // Called on the UI thread when a registered hotkey is pressed.
    private void OnHotkeyPressed(int id)
    {
        switch (id)
        {
            case HotkeyId1: PlayCachedSound("sound1.mp3"); break;
            case HotkeyId2: PlayCachedSound("sound2.mp3"); break;
            case HotkeyId3: PlayCachedSound("sound3.mp3"); break;
            case HotkeyId4: PlayCachedSound("sound4.mp3"); break;
        }
    }

    // ── Button click handlers ─────────────────────────────────────────────────

    private void Sound1_Click(object sender, RoutedEventArgs e) => PlayCachedSound("sound1.mp3");
    private void Sound2_Click(object sender, RoutedEventArgs e) => PlayCachedSound("sound2.mp3");
    private void Sound3_Click(object sender, RoutedEventArgs e) => PlayCachedSound("sound3.mp3");
    private void Sound4_Click(object sender, RoutedEventArgs e) => PlayCachedSound("sound4.mp3");

    private void StopButton_Click(object sender, RoutedEventArgs e)
    {
        _engine?.StopAll();
        StatusText.Text = "Stopped";
    }

    private void Window_Closing(object sender, System.ComponentModel.CancelEventArgs e)
    {
        _hotkeys?.Dispose();   // unregisters hotkeys so other apps can use them again
        _engine?.Dispose();
    }

    // ── Core playback ─────────────────────────────────────────────────────────

    private void PlayCachedSound(string fileName)
    {
        if (_engine is null || !_sounds.TryGetValue(fileName, out var sound))
        {
            StatusText.Text = "Sound not loaded.";
            return;
        }

        _engine.Play(sound);
        StatusText.Text = $"Playing: {fileName}";
    }
}
