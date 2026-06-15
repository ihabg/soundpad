using SoundPad.App.Audio;
using SoundPad.App.Hotkeys;
using System.Diagnostics;
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
    private HotkeyManager?  _hotkeys;
    private MicPassthrough? _micPassthrough;

    // ── Hotkey IDs ────────────────────────────────────────────────────────────
    //
    // Primary set: Ctrl + Alt + 1..4
    // On keyboards with an AltGr key (most non-US layouts), AltGr is identical
    // to Ctrl+Alt at the Win32 level.  This means AltGr+1..4 may already be
    // claimed by the keyboard driver for special characters (€, @, ¹, etc.),
    // so RegisterHotKey returns false even though no other app is "using" them.
    //
    // Fallback set: Ctrl + Shift + F1..F4
    // These are almost never claimed, so at least one set should always work.
    private const int HotkeyId1 = 9001;
    private const int HotkeyId2 = 9002;
    private const int HotkeyId3 = 9003;
    private const int HotkeyId4 = 9004;

    private const int HotkeyIdFallback1 = 9011;  // Ctrl+Shift+F1
    private const int HotkeyIdFallback2 = 9012;  // Ctrl+Shift+F2
    private const int HotkeyIdFallback3 = 9013;  // Ctrl+Shift+F3
    private const int HotkeyIdFallback4 = 9014;  // Ctrl+Shift+F4

    public MainWindow()
    {
        InitializeComponent();
    }

    // ── SourceInitialized: earliest safe point for hotkey setup ───────────────
    //
    // WPF fires OnSourceInitialized immediately after creating the Win32 HWND
    // and its HwndSource.  This is the correct — and earliest — place to:
    //   • read the window handle (Handle is guaranteed non-zero here)
    //   • call HwndSource.AddHook (the source object exists now)
    //   • call RegisterHotKey (valid handle to register against)
    //
    // Using Loaded instead can work, but SourceInitialized is more reliable
    // because Loaded can be delayed by heavy UI work and the HWND lifecycle is
    // explicitly tied to SourceInitialized, not Loaded.
    protected override void OnSourceInitialized(EventArgs e)
    {
        base.OnSourceInitialized(e);
        RegisterHotkeys();
    }

    // ── Loaded: sound loading, device population, mic list ───────────────────
    //
    // Hotkeys are NOT registered here — they were already set up in
    // OnSourceInitialized above.  Sound loading and device enumeration happen
    // here because they do not depend on the HWND.
    private void MainWindow_Loaded(object sender, RoutedEventArgs e)
    {
        try
        {
            LoadSounds();
            PopulateDeviceLists();
            PopulateMicList();
        }
        catch (Exception ex)
        {
            // Safety net for any unexpected exception escaping the inner try/catch.
            Debug.WriteLine($"[Startup] Unexpected error: {ex.Message}");
            StatusText.Text = $"Startup error: {ex.Message}";
        }

        // Show a final "Ready" message only when everything is working.
        // If hotkeys had an error, _hotkeys will be null and we leave the
        // hotkey error visible in StatusText.
        if (_hotkeys is not null && _sounds.Count == 4 && _monitorEngine is not null)
            StatusText.Text = "Ready — Ctrl+Alt+1..4 or Ctrl+Shift+F1..F4 active";
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

    // ── Microphone device population ──────────────────────────────────────────

    private void PopulateMicList()
    {
        try
        {
            var mics = MicDevice.GetAll();

            // MicPassthroughCheckBox is unchecked at startup, so even if
            // SelectionChanged fires during ItemsSource assignment it will exit early.
            MicComboBox.ItemsSource = mics;

            if (mics.Count > 0)
                MicComboBox.SelectedIndex = 0;
            else
                StatusText.Text = "No microphone devices found.";
        }
        catch (Exception ex)
        {
            StatusText.Text = $"Mic list error: {ex.Message}";
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

        // Stop mic passthrough before disposing the engine it is connected to.
        // We will reconnect it to the new engine further down if it was enabled.
        StopMicPassthrough();

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
        {
            StatusText.Text = $"Virtual: {device.Name}";

            // If the checkbox is still checked, reconnect mic passthrough to the new engine.
            if (MicPassthroughCheckBox.IsChecked == true)
                StartMicPassthrough();
        }
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

    // ── Microphone passthrough ────────────────────────────────────────────────

    private void MicComboBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        // Restart with the newly selected mic only if passthrough was already running.
        if (MicPassthroughCheckBox.IsChecked == true)
            RestartMicPassthrough();
    }

    private void MicPassthroughCheckBox_Checked(object sender, RoutedEventArgs e)
    {
        StartMicPassthrough();
    }

    private void MicPassthroughCheckBox_Unchecked(object sender, RoutedEventArgs e)
    {
        StopMicPassthrough();
    }

    private void MicVolumeSlider_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        // _micPassthrough is null until the checkbox is checked — that is fine.
        _micPassthrough?.SetVolume((float)(MicVolumeSlider.Value / 100.0));
    }

    private void StartMicPassthrough()
    {
        if (_virtualEngine is null)
        {
            StatusText.Text = "Select a Virtual Output before enabling mic passthrough.";
            MicPassthroughCheckBox.IsChecked = false; // revert checkbox; fires Unchecked → StopMicPassthrough (no-op)
            return;
        }

        if (MicComboBox.SelectedItem is not MicDevice mic)
        {
            StatusText.Text = "No microphone selected.";
            MicPassthroughCheckBox.IsChecked = false;
            return;
        }

        StopMicPassthrough(); // ensure any lingering instance is cleaned up

        try
        {
            _micPassthrough = new MicPassthrough(_virtualEngine);
            _micPassthrough.SetVolume((float)(MicVolumeSlider.Value / 100.0));
            _micPassthrough.Start(mic.Number);
            StatusText.Text = $"Mic active: {mic.Name} → Virtual Output";
        }
        catch (Exception ex)
        {
            _micPassthrough = null;
            StatusText.Text = $"Mic error: {ex.Message}";
            MicPassthroughCheckBox.IsChecked = false;
        }
    }

    private void StopMicPassthrough()
    {
        _micPassthrough?.Stop();
        _micPassthrough = null;
    }

    private void RestartMicPassthrough()
    {
        StopMicPassthrough();
        StartMicPassthrough();
    }

    // ── Hotkey registration ───────────────────────────────────────────────────
    //
    // Called from OnSourceInitialized so the HWND and HwndSource are guaranteed
    // to exist.  Two sets are registered in parallel:
    //
    //   Primary:  Ctrl+Alt+1..4    (VK 0x31..0x34, IDs 9001..9004)
    //   Fallback: Ctrl+Shift+F1..4 (VK 0x70..0x73, IDs 9011..9014)
    //
    // Both sets call the same OnHotkeyPressed handler, which plays the correct
    // sound based on the ID.  Registering both means a keyboard-layout conflict
    // on the Ctrl+Alt set (very common with AltGr keyboards) still leaves the
    // Ctrl+Shift+F set working so the user can confirm the hook itself is fine.
    private void RegisterHotkeys()
    {
        try
        {
            _hotkeys = new HotkeyManager(this);
            _hotkeys.HotkeyPressed += OnHotkeyPressed;

            Debug.WriteLine("[Hotkeys] HotkeyManager created — HWND and HwndSource OK");

            // ── Primary: Ctrl + Alt + 1 / 2 / 3 / 4 ─────────────────────────
            // Virtual-key codes for the number row: 0x31='1', 0x32='2', etc.
            var primary = new (int Id, uint Vk, string Label)[]
            {
                (HotkeyId1, 0x31, "Ctrl+Alt+1"),
                (HotkeyId2, 0x32, "Ctrl+Alt+2"),
                (HotkeyId3, 0x33, "Ctrl+Alt+3"),
                (HotkeyId4, 0x34, "Ctrl+Alt+4"),
            };

            int primaryOk = 0;
            foreach (var (Id, Vk, Label) in primary)
            {
                bool ok = _hotkeys.Register(Id, HotkeyManager.CtrlAlt, Vk);
                if (ok)
                {
                    primaryOk++;
                    Debug.WriteLine($"[Hotkeys] Registered: {Label} (ID={Id})");
                    StatusText.Text = $"Hotkey registered: {Label}";
                }
                else
                {
                    Debug.WriteLine($"[Hotkeys] FAILED: {Label} (ID={Id}) — AltGr conflict or another app owns it");
                    StatusText.Text = $"Hotkey failed: {Label}";
                }
            }

            // ── Fallback: Ctrl + Shift + F1 / F2 / F3 / F4 ──────────────────
            // Virtual-key codes for function keys: 0x70=F1, 0x71=F2, etc.
            // These are almost never claimed by keyboard drivers or other apps.
            var fallback = new (int Id, uint Vk, string Label)[]
            {
                (HotkeyIdFallback1, 0x70, "Ctrl+Shift+F1"),
                (HotkeyIdFallback2, 0x71, "Ctrl+Shift+F2"),
                (HotkeyIdFallback3, 0x72, "Ctrl+Shift+F3"),
                (HotkeyIdFallback4, 0x73, "Ctrl+Shift+F4"),
            };

            int fallbackOk = 0;
            foreach (var (Id, Vk, Label) in fallback)
            {
                bool ok = _hotkeys.Register(Id, HotkeyManager.CtrlShift, Vk);
                if (ok)
                {
                    fallbackOk++;
                    Debug.WriteLine($"[Hotkeys] Registered fallback: {Label} (ID={Id})");
                }
                else
                {
                    Debug.WriteLine($"[Hotkeys] FAILED fallback: {Label} (ID={Id})");
                }
            }

            // Log the final summary so the Output window tells the whole story.
            Debug.WriteLine($"[Hotkeys] Summary: primary={primaryOk}/4 registered, fallback={fallbackOk}/4 registered");

            if (primaryOk == 0 && fallbackOk == 0)
                StatusText.Text = "Hotkey error: all registrations failed — check Debug Output";
            else if (primaryOk < 4)
                StatusText.Text = $"Hotkeys: {primaryOk}/4 Ctrl+Alt registered — use Ctrl+Shift+F1..F4 as fallback";
            else
                StatusText.Text = "Hotkeys registered: Ctrl+Alt+1..4 and Ctrl+Shift+F1..F4 active";
        }
        catch (Exception ex)
        {
            // If HotkeyManager construction itself threw (HWND or HwndSource failure),
            // _hotkeys stays null and we show a clear error.
            _hotkeys = null;
            Debug.WriteLine($"[Hotkeys] Exception during setup: {ex.Message}");
            StatusText.Text = $"Hotkey error: {ex.Message}";
        }
    }

    // Called by the HotkeyManager on the UI thread when any registered key is pressed.
    private void OnHotkeyPressed(int id)
    {
        // Log to the Debug Output window — this appears in Visual Studio Output pane
        // even when StatusText is overwritten by PlayCachedSound immediately after.
        Debug.WriteLine($"[Hotkeys] WM_HOTKEY received: ID={id}");

        switch (id)
        {
            case HotkeyId1:
            case HotkeyIdFallback1:
                Debug.WriteLine("[Hotkeys] → Sound 1");
                StatusText.Text = "Hotkey received: 1";
                PlayCachedSound("sound1.mp3");
                break;

            case HotkeyId2:
            case HotkeyIdFallback2:
                Debug.WriteLine("[Hotkeys] → Sound 2");
                StatusText.Text = "Hotkey received: 2";
                PlayCachedSound("sound2.mp3");
                break;

            case HotkeyId3:
            case HotkeyIdFallback3:
                Debug.WriteLine("[Hotkeys] → Sound 3");
                StatusText.Text = "Hotkey received: 3";
                PlayCachedSound("sound3.mp3");
                break;

            case HotkeyId4:
            case HotkeyIdFallback4:
                Debug.WriteLine("[Hotkeys] → Sound 4");
                StatusText.Text = "Hotkey received: 4";
                PlayCachedSound("sound4.mp3");
                break;

            default:
                Debug.WriteLine($"[Hotkeys] Unknown ID: {id} — no sound mapped");
                break;
        }
    }

    // ── Button click handlers ─────────────────────────────────────────────────

    private void Sound1_Click(object sender, RoutedEventArgs e) => PlayCachedSound("sound1.mp3");
    private void Sound2_Click(object sender, RoutedEventArgs e) => PlayCachedSound("sound2.mp3");
    private void Sound3_Click(object sender, RoutedEventArgs e) => PlayCachedSound("sound3.mp3");
    private void Sound4_Click(object sender, RoutedEventArgs e) => PlayCachedSound("sound4.mp3");

    private void StopButton_Click(object sender, RoutedEventArgs e)
    {
        // Stops sound-effect playback only.  Mic passthrough intentionally keeps running.
        _monitorEngine?.StopAll();
        _virtualEngine?.StopAll();
        StatusText.Text = "Stopped";
    }

    private void Window_Closing(object sender, System.ComponentModel.CancelEventArgs e)
    {
        _hotkeys?.Dispose();
        _micPassthrough?.Dispose();
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
