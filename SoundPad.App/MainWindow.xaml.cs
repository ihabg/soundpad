using Microsoft.Win32;
using SoundPad.App.Audio;
using SoundPad.App.Hotkeys;
using SoundPad.App.Models;
using SoundPad.App.Services;
using System.Diagnostics;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;

namespace SoundPad.App;

public partial class MainWindow : Window
{
    // ── Audio engines ──────────────────────────────────────────────────────────
    private AudioPlaybackEngine? _monitorEngine;
    private AudioPlaybackEngine? _virtualEngine;

    // ── Sound library ──────────────────────────────────────────────────────────
    // _library is the ordered list; index 0–3 map to the four hotkeys.
    private List<SoundItem>                 _library      = new List<SoundItem>();
    private readonly Dictionary<Guid, CachedSound> _cachedSounds = new Dictionary<Guid, CachedSound>();

    // ── Supporting objects ─────────────────────────────────────────────────────
    private HotkeyManager?  _hotkeys;
    private MicPassthrough? _micPassthrough;

    // ── Hotkey IDs ─────────────────────────────────────────────────────────────
    private const int HotkeyId1 = 9001, HotkeyId2 = 9002, HotkeyId3 = 9003, HotkeyId4 = 9004;
    private const int HotkeyIdFallback1 = 9011, HotkeyIdFallback2 = 9012;
    private const int HotkeyIdFallback3 = 9013, HotkeyIdFallback4 = 9014;

    private static readonly string[] HotkeyLabels =
    {
        "Ctrl+Alt+1  /  Ctrl+Shift+F1",
        "Ctrl+Alt+2  /  Ctrl+Shift+F2",
        "Ctrl+Alt+3  /  Ctrl+Shift+F3",
        "Ctrl+Alt+4  /  Ctrl+Shift+F4",
    };

    // ── Constructor ────────────────────────────────────────────────────────────
    public MainWindow()
    {
        InitializeComponent();
    }

    // ── OnSourceInitialized: earliest safe point for hotkeys ──────────────────
    protected override void OnSourceInitialized(EventArgs e)
    {
        base.OnSourceInitialized(e);
        RegisterHotkeys();
    }

    // ── Loaded: sound library + device lists ──────────────────────────────────
    private void MainWindow_Loaded(object sender, RoutedEventArgs e)
    {
        try
        {
            LoadLibrary();
            PopulateDeviceLists();
            PopulateMicList();
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[Startup] Unexpected error: {ex.Message}");
            StatusText.Text = $"Startup error: {ex.Message}";
        }
    }

    // ── Sound library ──────────────────────────────────────────────────────────

    private void LoadLibrary()
    {
        _library = SoundLibraryService.Load();

        if (_library.Count == 0)
            SeedDefaultSounds();

        foreach (var item in _library.ToList())
        {
            if (!File.Exists(item.FilePath))
            {
                Debug.WriteLine($"[Library] Missing file: {item.FilePath}");
                continue;
            }

            try
            {
                _cachedSounds[item.Id] = new CachedSound(item.FilePath);
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[Library] Load error for '{item.DisplayName}': {ex.Message}");
            }
        }

        RebuildSoundsPanel();

        int loaded = _cachedSounds.Count;
        StatusText.Text = loaded > 0 ? $"Library loaded — {loaded} sound(s)" : "Library empty";
    }

    // Copies built-in sample sounds from the app Sounds folder on first launch.
    private void SeedDefaultSounds()
    {
        var builtinDir = Path.Combine(AppContext.BaseDirectory, "Sounds");
        if (!Directory.Exists(builtinDir))
            return;

        var samples = new[] { "sound1.mp3", "sound2.mp3", "sound3.mp3", "sound4.mp3" };

        foreach (var name in samples)
        {
            var src = Path.Combine(builtinDir, name);
            if (!File.Exists(src))
                continue;

            try
            {
                var dest = SoundLibraryService.ImportFile(src);
                _library.Add(new SoundItem
                {
                    DisplayName = Path.GetFileNameWithoutExtension(name),
                    FilePath    = dest,
                    CreatedAt   = DateTime.UtcNow
                });
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[Seed] Failed to import {name}: {ex.Message}");
            }
        }

        if (_library.Count > 0)
            SoundLibraryService.Save(_library);
    }

    // Rebuilds the WrapPanel contents from _library.
    private void RebuildSoundsPanel()
    {
        SoundsPanel.Children.Clear();

        for (int i = 0; i < _library.Count; i++)
        {
            var card = BuildSoundCard(_library[i], i);
            SoundsPanel.Children.Add(card);
        }

        EmptyLibraryText.Visibility = _library.Count == 0
            ? Visibility.Visible
            : Visibility.Collapsed;
    }

    // Builds one sound card: a rounded border with a play button and remove button.
    private UIElement BuildSoundCard(SoundItem item, int index)
    {
        var capturedItem = item;

        // ── Hotkey label (index 0–3 only) ──
        var hotkeyBlock = new TextBlock
        {
            Text                = index < 4 ? HotkeyLabels[index] : "",
            Foreground          = new SolidColorBrush(Color.FromRgb(0x66, 0x66, 0x66)),
            FontSize            = 9,
            HorizontalAlignment = HorizontalAlignment.Center,
            Margin              = new Thickness(6, 6, 6, 0),
            Visibility          = index < 4 ? Visibility.Visible : Visibility.Collapsed
        };

        // ── Play button ──
        var playBtn = new Button
        {
            Content         = item.DisplayName,
            Foreground      = new SolidColorBrush(Colors.White),
            Background      = Brushes.Transparent,
            BorderThickness = new Thickness(0),
            FontSize        = 13,
            FontWeight      = FontWeights.SemiBold,
            Cursor          = Cursors.Hand,
            Padding         = new Thickness(8, 6, 8, 6),
            HorizontalAlignment = HorizontalAlignment.Stretch
        };
        playBtn.Click += (_, _) => PlayLibraryItem(capturedItem);

        // ── Remove button ──
        var removeBtn = new Button
        {
            Content         = "Remove",
            Foreground      = new SolidColorBrush(Color.FromRgb(0xBB, 0x55, 0x55)),
            Background      = Brushes.Transparent,
            BorderThickness = new Thickness(0),
            FontSize        = 10,
            Cursor          = Cursors.Hand,
            Padding         = new Thickness(8, 4, 8, 6),
            HorizontalAlignment = HorizontalAlignment.Stretch
        };
        removeBtn.Click += (_, _) => RemoveSound(capturedItem);

        // ── Separator ──
        var sep = new Border
        {
            BorderBrush     = new SolidColorBrush(Color.FromRgb(0x44, 0x44, 0x44)),
            BorderThickness = new Thickness(0, 1, 0, 0),
            Margin          = new Thickness(6, 0, 6, 0)
        };

        // ── Inner layout ──
        var stack = new StackPanel();
        stack.Children.Add(hotkeyBlock);
        stack.Children.Add(playBtn);
        stack.Children.Add(sep);
        stack.Children.Add(removeBtn);

        // ── Card border ──
        var border = new Border
        {
            Width           = 160,
            Background      = new SolidColorBrush(Color.FromRgb(0x2E, 0x2E, 0x2E)),
            BorderBrush     = new SolidColorBrush(Color.FromRgb(0x44, 0x44, 0x44)),
            BorderThickness = new Thickness(1),
            CornerRadius    = new CornerRadius(6),
            Margin          = new Thickness(0, 0, 8, 8),
            Child           = stack
        };

        return border;
    }

    // ── Add Sound ─────────────────────────────────────────────────────────────

    private void AddSound_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new OpenFileDialog
        {
            Title  = "Select an audio file",
            Filter = "Audio Files|*.mp3;*.wav;*.ogg;*.flac;*.aac|All Files|*.*"
        };

        if (dialog.ShowDialog() != true)
            return;

        try
        {
            var destPath = SoundLibraryService.ImportFile(dialog.FileName);
            var sound    = new CachedSound(destPath);
            var item     = new SoundItem
            {
                DisplayName = Path.GetFileNameWithoutExtension(dialog.FileName),
                FilePath    = destPath,
                CreatedAt   = DateTime.UtcNow
            };

            _cachedSounds[item.Id] = sound;
            _library.Add(item);
            SoundLibraryService.Save(_library);
            RebuildSoundsPanel();
            StatusText.Text = $"Added: {item.DisplayName}";
        }
        catch (Exception ex)
        {
            StatusText.Text = $"Add error: {ex.Message}";
        }
    }

    // ── Remove Sound ──────────────────────────────────────────────────────────

    private void RemoveSound(SoundItem item)
    {
        var name = item.DisplayName;
        _cachedSounds.Remove(item.Id);
        _library.Remove(item);
        SoundLibraryService.Save(_library);
        RebuildSoundsPanel();
        StatusText.Text = $"Removed: {name}";
    }

    // ── Playback ──────────────────────────────────────────────────────────────

    private void PlayLibraryItem(SoundItem item)
    {
        if (!_cachedSounds.TryGetValue(item.Id, out var sound))
        {
            StatusText.Text = $"Not loaded: {item.DisplayName}";
            return;
        }

        PlaySound(sound, item.DisplayName);
    }

    private void PlayByIndex(int index)
    {
        if (index < 0 || index >= _library.Count)
            return;

        PlayLibraryItem(_library[index]);
    }

    private void PlaySound(CachedSound sound, string label)
    {
        if (_monitorEngine is null && _virtualEngine is null)
        {
            StatusText.Text = "No output device selected.";
            return;
        }

        try
        {
            _monitorEngine?.Play(sound);

            var monitorDevice = MonitorComboBox.SelectedItem as AudioDevice;
            var virtualDevice = VirtualComboBox.SelectedItem as AudioDevice;

            bool sameDevice = _virtualEngine is not null
                           && AudioDevice.AreSameOutputDevice(monitorDevice, virtualDevice);

            if (!sameDevice)
                _virtualEngine?.Play(sound);

            StatusText.Text = sameDevice
                ? $"Playing: {label}  (Virtual = Monitor, once)"
                : $"Playing: {label}";
        }
        catch (Exception ex)
        {
            StatusText.Text = $"Playback error: {ex.Message}";
        }
    }

    // ── Stop All ──────────────────────────────────────────────────────────────

    private void StopButton_Click(object sender, RoutedEventArgs e)
    {
        _monitorEngine?.StopAll();
        _virtualEngine?.StopAll();
        StatusText.Text = "Stopped";
    }

    // ── Output device lists ───────────────────────────────────────────────────

    private void PopulateDeviceLists()
    {
        try
        {
            MonitorComboBox.SelectionChanged -= MonitorComboBox_SelectionChanged;
            MonitorComboBox.ItemsSource       = AudioDevice.GetAll();
            MonitorComboBox.SelectedIndex     = 0;
            MonitorComboBox.SelectionChanged += MonitorComboBox_SelectionChanged;
            CreateMonitorEngine(AudioDevice.DefaultDeviceNumber);

            VirtualComboBox.SelectionChanged -= VirtualComboBox_SelectionChanged;
            VirtualComboBox.ItemsSource       = AudioDevice.GetAllWithNone();
            VirtualComboBox.SelectedIndex     = 0;
            VirtualComboBox.SelectionChanged += VirtualComboBox_SelectionChanged;
        }
        catch (Exception ex)
        {
            StatusText.Text = $"Device list error: {ex.Message}";
        }
    }

    private void MonitorComboBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (MonitorComboBox.SelectedItem is not AudioDevice device)
            return;

        _monitorEngine?.StopAll();
        _monitorEngine?.Dispose();
        _monitorEngine = null;

        if (!device.IsNone)
        {
            CreateMonitorEngine(device.Number);
            if (_monitorEngine is not null)
                StatusText.Text = $"Monitor: {device.Name}";
        }
    }

    private void VirtualComboBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (VirtualComboBox.SelectedItem is not AudioDevice device)
            return;

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
            if (MicPassthroughCheckBox.IsChecked == true)
                StartMicPassthrough();
        }
    }

    private void CreateMonitorEngine(int deviceNumber)
    {
        try   { _monitorEngine = new AudioPlaybackEngine(deviceNumber); }
        catch (Exception ex)
        {
            _monitorEngine = null;
            StatusText.Text = $"Monitor device error: {ex.Message}";
        }
    }

    private void CreateVirtualEngine(int deviceNumber)
    {
        try   { _virtualEngine = new AudioPlaybackEngine(deviceNumber); }
        catch (Exception ex)
        {
            _virtualEngine = null;
            StatusText.Text = $"Virtual device error: {ex.Message}";
        }
    }

    // ── Microphone ────────────────────────────────────────────────────────────

    private void PopulateMicList()
    {
        try
        {
            var mics = MicDevice.GetAll();
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

    private void MicComboBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (MicPassthroughCheckBox.IsChecked == true)
            RestartMicPassthrough();
    }

    private void MicPassthroughCheckBox_Checked(object sender, RoutedEventArgs e)
        => StartMicPassthrough();

    private void MicPassthroughCheckBox_Unchecked(object sender, RoutedEventArgs e)
        => StopMicPassthrough();

    private void MicVolumeSlider_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
        => _micPassthrough?.SetVolume((float)(MicVolumeSlider.Value / 100.0));

    private void StartMicPassthrough()
    {
        if (_virtualEngine is null)
        {
            StatusText.Text = "Select a Virtual Output before enabling mic passthrough.";
            MicPassthroughCheckBox.IsChecked = false;
            return;
        }

        if (MicComboBox.SelectedItem is not MicDevice mic)
        {
            StatusText.Text = "No microphone selected.";
            MicPassthroughCheckBox.IsChecked = false;
            return;
        }

        StopMicPassthrough();

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

    // ── Hotkeys ───────────────────────────────────────────────────────────────

    private void RegisterHotkeys()
    {
        try
        {
            _hotkeys = new HotkeyManager(this);
            _hotkeys.HotkeyPressed += OnHotkeyPressed;
            Debug.WriteLine("[Hotkeys] HotkeyManager created");

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
                if (_hotkeys.Register(Id, HotkeyManager.CtrlAlt, Vk))
                {
                    primaryOk++;
                    Debug.WriteLine($"[Hotkeys] Registered: {Label}");
                }
                else
                {
                    Debug.WriteLine($"[Hotkeys] FAILED: {Label}");
                }
            }

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
                if (_hotkeys.Register(Id, HotkeyManager.CtrlShift, Vk))
                {
                    fallbackOk++;
                    Debug.WriteLine($"[Hotkeys] Registered fallback: {Label}");
                }
                else
                {
                    Debug.WriteLine($"[Hotkeys] FAILED fallback: {Label}");
                }
            }

            Debug.WriteLine($"[Hotkeys] Summary: primary={primaryOk}/4, fallback={fallbackOk}/4");
        }
        catch (Exception ex)
        {
            _hotkeys = null;
            Debug.WriteLine($"[Hotkeys] Exception: {ex.Message}");
        }
    }

    private void OnHotkeyPressed(int id)
    {
        Debug.WriteLine($"[Hotkeys] WM_HOTKEY ID={id}");
        switch (id)
        {
            case HotkeyId1: case HotkeyIdFallback1: PlayByIndex(0); break;
            case HotkeyId2: case HotkeyIdFallback2: PlayByIndex(1); break;
            case HotkeyId3: case HotkeyIdFallback3: PlayByIndex(2); break;
            case HotkeyId4: case HotkeyIdFallback4: PlayByIndex(3); break;
        }
    }

    // ── Window closing ────────────────────────────────────────────────────────

    private void Window_Closing(object sender, System.ComponentModel.CancelEventArgs e)
    {
        _hotkeys?.Dispose();
        _micPassthrough?.Dispose();
        _monitorEngine?.Dispose();
        _virtualEngine?.Dispose();
    }
}
