using SoundPad.App.Audio;
using SoundPad.App.Hotkeys;
using System.IO;
using System.Windows;
using System.Windows.Controls;

namespace SoundPad.App;

public partial class MainWindow : Window
{
    // Two independent engines: one per physical output device.
    // _virtualEngine is null when "None" is selected for Virtual Output.
    private AudioPlaybackEngine?                      _monitorEngine;
    private AudioPlaybackEngine?                      _virtualEngine;
    private readonly Dictionary<string, CachedSound> _sounds = new();
    private HotkeyManager? _hotkeys;

    private const int HotkeyId1 = 9001;
    private const int HotkeyId2 = 9002;
    private const int HotkeyId3 = 9003;
    private const int HotkeyId4 = 9004;

    public MainWindow()
    {
        InitializeComponent();
    }

    private void MainWindow_Loaded(object sender, RoutedEventArgs e)
    {
        try
        {
            LoadSounds();
            PopulateDeviceLists();
            RegisterHotkeys();
        }
        catch (Exception ex)
        {
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

    private void PopulateDeviceLists()
    {
        try
        {
            // ── Monitor ──
            // Unhook while setting SelectedIndex so we don't trigger SelectionChanged
            // during initialisation, which would create a second engine unnecessarily.
            MonitorComboBox.SelectionChanged -= MonitorComboBox_SelectionChanged;
            MonitorComboBox.ItemsSource       = AudioDevice.GetAll();
            MonitorComboBox.SelectedIndex     = 0; // Default Output Device
            MonitorComboBox.SelectionChanged += MonitorComboBox_SelectionChanged;

            CreateMonitorEngine(AudioDevice.DefaultDeviceNumber);

            // ── Virtual ──
            VirtualComboBox.SelectionChanged -= VirtualComboBox_SelectionChanged;
            VirtualComboBox.ItemsSource       = AudioDevice.GetAllWithNone();
            VirtualComboBox.SelectedIndex     = 0; // None — no virtual output at startup
            VirtualComboBox.SelectionChanged += VirtualComboBox_SelectionChanged;

            // _virtualEngine stays null until the user picks a real device.
        }
        catch (Exception ex)
        {
            StatusText.Text = $"Device list error: {ex.Message}";
        }
    }

    // ── Monitor selection changed ─────────────────────────────────────────────

    private void MonitorComboBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (MonitorComboBox.SelectedItem is not AudioDevice device)
            return;

        _monitorEngine?.StopAll();
        _monitorEngine?.Dispose();
        _monitorEngine = null;

        // Monitor never has a "None" entry, so IsNone is always false here,
        // but we guard anyway so the pattern is consistent with Virtual.
        if (!device.IsNone)
        {
            CreateMonitorEngine(device.Number);

            if (_monitorEngine is not null)
                StatusText.Text = $"Monitor: {device.Name}";
        }
    }

    // ── Virtual selection changed ─────────────────────────────────────────────

    private void VirtualComboBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (VirtualComboBox.SelectedItem is not AudioDevice device)
            return;

        _virtualEngine?.StopAll();
        _virtualEngine?.Dispose();
        _virtualEngine = null;

        if (device.IsNone)
        {
            StatusText.Text = "Virtual output: None";
            return;
        }

        CreateVirtualEngine(device.Number);

        if (_virtualEngine is not null)
            StatusText.Text = $"Virtual: {device.Name}";
    }

    // ── Engine creation helpers ───────────────────────────────────────────────

    private void CreateMonitorEngine(int deviceNumber)
    {
        try
        {
            _monitorEngine = new AudioPlaybackEngine(deviceNumber);
        }
        catch (Exception ex)
        {
            _monitorEngine = null;
            StatusText.Text = $"Monitor device error: {ex.Message}";
        }
    }

    private void CreateVirtualEngine(int deviceNumber)
    {
        try
        {
            _virtualEngine = new AudioPlaybackEngine(deviceNumber);
        }
        catch (Exception ex)
        {
            _virtualEngine = null;
            StatusText.Text = $"Virtual device error: {ex.Message}";
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

            // Only show "Ready" when sounds loaded AND monitor device is working.
            if (_sounds.Count == 4 && _monitorEngine is not null)
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
        _monitorEngine?.StopAll();
        _virtualEngine?.StopAll();
        StatusText.Text = "Stopped";
    }

    private void Window_Closing(object sender, System.ComponentModel.CancelEventArgs e)
    {
        _hotkeys?.Dispose();
        _monitorEngine?.Dispose();
        _virtualEngine?.Dispose();
    }

    // ── Core playback ─────────────────────────────────────────────────────────

    private void PlayCachedSound(string fileName)
    {
        if (!_sounds.TryGetValue(fileName, out var sound))
        {
            StatusText.Text = "Sound not loaded.";
            return;
        }

        if (_monitorEngine is null && _virtualEngine is null)
        {
            StatusText.Text = "No output device selected.";
            return;
        }

        try
        {
            _monitorEngine?.Play(sound);

            // Guard against double-play.
            // AreSameOutputDevice resolves WAVE_MAPPER (-1 / "Default") to its real
            // device index before comparing, so Default == headphones is caught even
            // when the user picks them via different ComboBox entries.
            var monitorDevice = MonitorComboBox.SelectedItem as AudioDevice;
            var virtualDevice = VirtualComboBox.SelectedItem as AudioDevice;

            bool sameDevice = _virtualEngine is not null
                           && AudioDevice.AreSameOutputDevice(monitorDevice, virtualDevice);

            if (sameDevice)
            {
                StatusText.Text = $"Playing: {fileName} — Virtual = Monitor, playing once";
            }
            else
            {
                _virtualEngine?.Play(sound);
                StatusText.Text = $"Playing: {fileName}";
            }
        }
        catch (Exception ex)
        {
            StatusText.Text = $"Playback error: {ex.Message}";
        }
    }
}
