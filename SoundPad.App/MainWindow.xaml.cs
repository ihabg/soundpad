using SoundPad.App.Audio;
using SoundPad.App.Hotkeys;
using System.IO;
using System.Windows;
using System.Windows.Controls;

namespace SoundPad.App;

public partial class MainWindow : Window
{
    private AudioPlaybackEngine?                      _engine;
    private readonly Dictionary<string, CachedSound> _sounds = new();
    private HotkeyManager? _hotkeys;

    private const int HotkeyId1 = 9001;
    private const int HotkeyId2 = 9002;
    private const int HotkeyId3 = 9003;
    private const int HotkeyId4 = 9004;

    public MainWindow()
    {
        // InitializeComponent is the ONLY call allowed here.
        // It creates the visual tree from XAML and cannot fail for valid markup.
        // Everything else — NAudio, device enumeration, hotkeys — goes in Loaded
        // so the window always appears before any startup logic runs.
        InitializeComponent();
    }

    // Loaded fires after the window's HWND is created and layout is complete,
    // but the app will not crash if something here throws — our try/catch shows
    // the error in StatusText and the window stays open.
    private void MainWindow_Loaded(object sender, RoutedEventArgs e)
    {
        try
        {
            LoadSounds();
            PopulateDeviceList();
            RegisterHotkeys();
        }
        catch (Exception ex)
        {
            // Safety net: any exception that escapes the methods above lands here
            // instead of crashing the process.
            StatusText.Text = $"Startup error: {ex.Message}";
        }
    }

    // ── Sound loading ─────────────────────────────────────────────────────────

    private void LoadSounds()
    {
        var fileNames = new[] { "sound1.mp3", "sound2.mp3", "sound3.mp3", "sound4.mp3" };

        try
        {
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

    // ── Output device selection ───────────────────────────────────────────────

    private void PopulateDeviceList()
    {
        try
        {
            // Unhook while populating so setting SelectedIndex = 0 below
            // does not trigger SelectionChanged during initialisation.
            DeviceComboBox.SelectionChanged -= DeviceComboBox_SelectionChanged;

            DeviceComboBox.ItemsSource  = AudioDevice.GetAll();
            DeviceComboBox.SelectedIndex = 0;

            DeviceComboBox.SelectionChanged += DeviceComboBox_SelectionChanged;

            // Create the audio engine once, pointed at the default device.
            CreateEngine(AudioDevice.DefaultDeviceNumber);
        }
        catch (Exception ex)
        {
            StatusText.Text = $"Device list error: {ex.Message}";
        }
    }

    private void DeviceComboBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (DeviceComboBox.SelectedItem is not AudioDevice device)
            return;

        SwitchOutputDevice(device);
    }

    private void SwitchOutputDevice(AudioDevice device)
    {
        _engine?.StopAll();
        _engine?.Dispose();
        _engine = null;

        CreateEngine(device.Number);

        if (_engine is not null)
            StatusText.Text = $"Output: {device.Name}";
    }

    private void CreateEngine(int deviceNumber)
    {
        try
        {
            _engine = new AudioPlaybackEngine(deviceNumber);
        }
        catch (Exception ex)
        {
            _engine = null;
            StatusText.Text = $"Device error: {ex.Message}";
        }
    }

    // ── Hotkey registration ───────────────────────────────────────────────────

    private void RegisterHotkeys()
    {
        try
        {
            _hotkeys = new HotkeyManager(this);
            _hotkeys.HotkeyPressed += OnHotkeyPressed;

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
                    StatusText.Text = $"Could not register {Label} — already in use by another app";
                    return;
                }
            }

            if (_sounds.Count == 4)
                StatusText.Text = "Ready — Ctrl+Alt+1..4 active";
        }
        catch (Exception ex)
        {
            StatusText.Text = $"Hotkey error: {ex.Message}";
        }
    }

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
        _hotkeys?.Dispose();
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
