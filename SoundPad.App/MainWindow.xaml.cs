using Microsoft.Win32;
using SoundPad.App.Audio;
using SoundPad.App.Dialogs;
using SoundPad.App.Hotkeys;
using SoundPad.App.Models;
using SoundPad.App.Services;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace SoundPad.App;

public partial class MainWindow : Window
{
    // ── Audio engines ──────────────────────────────────────────────────────────
    // Two independent engines: one per physical output device.
    // _virtualEngine is null when "None" is selected for Virtual Output.
    private AudioPlaybackEngine? _monitorEngine;
    private AudioPlaybackEngine? _virtualEngine;

    // ── Sound library ──────────────────────────────────────────────────────────
    // _library is the ordered list; the first four items map to hotkeys 0–3.
    // _cachedSounds maps each SoundItem's Guid to its preloaded float samples.
    private List<SoundItem>                        _library      = new List<SoundItem>();
    private readonly Dictionary<Guid, CachedSound> _cachedSounds = new Dictionary<Guid, CachedSound>();

    // ── Supporting objects ─────────────────────────────────────────────────────
    private HotkeyManager?  _hotkeys;
    private MicPassthrough? _micPassthrough;

    // ── Hotkey IDs ─────────────────────────────────────────────────────────────
    //
    // Primary: Ctrl+Alt+1..4 (may be blocked by AltGr keyboards)
    // Fallback: Ctrl+Shift+F1..F4 (almost never taken by other apps)
    // Both sets map to the same library indices 0–3.
    private const int HotkeyId1 = 9001, HotkeyId2 = 9002, HotkeyId3 = 9003, HotkeyId4 = 9004;
    private const int HotkeyIdFallback1 = 9011, HotkeyIdFallback2 = 9012;
    private const int HotkeyIdFallback3 = 9013, HotkeyIdFallback4 = 9014;

    // Short hotkey labels shown on the first four sound cards.
    private static readonly string[] HotkeyLabels = new string[]
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
    // WPF guarantees both the Win32 HWND and its HwndSource exist here.
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

    // ══════════════════════════════════════════════════════════════════════════
    //  SOUND LIBRARY
    // ══════════════════════════════════════════════════════════════════════════

    // Loads sounds.json, preloads all audio files into CachedSound objects,
    // then renders the sound cards.
    private void LoadLibrary()
    {
        _library = SoundLibraryService.Load();

        // If the library is empty (first launch or cleared), copy the built-in
        // sample sounds from the app's Sounds folder into AppData.
        if (_library.Count == 0)
            SeedDefaultSounds();

        // Preload each sound.  Skip missing or unreadable files gracefully.
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
                Debug.WriteLine($"[Library] Could not load '{item.DisplayName}': {ex.Message}");
            }
        }

        RefreshCategoryFilter();
        FilterSoundsPanel();

        int loaded = _cachedSounds.Count;
        StatusText.Text = loaded > 0
            ? $"Library: {loaded} sound(s) loaded"
            : "Library empty — click '+ Add Sound'";
    }

    // On first launch, copies bundled sample sounds into %AppData%\SoundPad\Sounds.
    private void SeedDefaultSounds()
    {
        var builtinDir = Path.Combine(AppContext.BaseDirectory, "Sounds");
        if (!Directory.Exists(builtinDir))
            return;

        var samples = new string[] { "sound1.mp3", "sound2.mp3", "sound3.mp3", "sound4.mp3" };
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
                    Category    = "General",
                    Volume      = 1.0f,
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

    // ── Filter / rebuild ──────────────────────────────────────────────────────

    // Rebuilds SoundsPanel showing only sounds that match the current search
    // text and selected category.  Called after any library or filter change.
    private void FilterSoundsPanel()
    {
        // SearchBox and CategoryFilter may not exist yet during early startup calls.
        var search   = SearchBox?.Text.Trim().ToLowerInvariant() ?? "";
        var category = CategoryFilter?.SelectedItem as string ?? "All";

        SoundsPanel.Children.Clear();

        for (int i = 0; i < _library.Count; i++)
        {
            var item = _library[i];

            bool matchSearch   = string.IsNullOrEmpty(search)
                              || item.DisplayName.ToLowerInvariant().Contains(search);
            bool matchCategory = category == "All" || item.Category == category;

            if (!matchSearch || !matchCategory)
                continue;

            SoundsPanel.Children.Add(BuildSoundCard(item, i));
        }

        bool panelEmpty = SoundsPanel.Children.Count == 0;
        EmptyLibraryText.Visibility = panelEmpty ? Visibility.Visible : Visibility.Collapsed;
        EmptyLibraryText.Text = _library.Count == 0
            ? "No sounds yet — click \"+ Add Sound\" to import an audio file."
            : "No sounds match your search or filter.";
    }

    // Repopulates the Category ComboBox from the distinct categories in the
    // library.  Keeps the current selection when it still exists.
    private void RefreshCategoryFilter()
    {
        if (CategoryFilter is null) return;
        var current = CategoryFilter.SelectedItem as string ?? "All";

        CategoryFilter.SelectionChanged -= CategoryFilter_SelectionChanged;
        CategoryFilter.Items.Clear();
        CategoryFilter.Items.Add("All");

        foreach (var cat in _library
            .Select(x => string.IsNullOrWhiteSpace(x.Category) ? "General" : x.Category)
            .Distinct()
            .OrderBy(c => c))
        {
            if (!CategoryFilter.Items.Contains(cat))
                CategoryFilter.Items.Add(cat);
        }

        CategoryFilter.SelectedItem = CategoryFilter.Items.Contains(current) ? current : "All";
        CategoryFilter.SelectionChanged += CategoryFilter_SelectionChanged;
    }

    // ── Sound card builder ────────────────────────────────────────────────────

    // Creates one visual card for a SoundItem.
    // libraryIndex is the item's position in _library (not the filtered view),
    // so hotkey labels are always accurate.
    private Border BuildSoundCard(SoundItem item, int libraryIndex)
    {
        var capturedItem = item;

        // ── Play button ────────────────────────────────────────────────────
        var playBtn = new Button
        {
            Content             = item.DisplayName,
            Style               = (Style)FindResource("CardPlayButton"),
            HorizontalAlignment = HorizontalAlignment.Stretch
        };
        playBtn.Click += (_, _) => PlayLibraryItem(capturedItem);

        // ── Hotkey + category line ─────────────────────────────────────────
        var hotkeyText   = libraryIndex < HotkeyLabels.Length ? HotkeyLabels[libraryIndex] : "No hotkey";
        var categoryText = string.IsNullOrWhiteSpace(item.Category) ? "General" : item.Category;

        var infoLine = new TextBlock
        {
            Text         = $"{hotkeyText}  ·  {categoryText}",
            Foreground   = new SolidColorBrush(Color.FromRgb(0x50, 0x50, 0x50)),
            FontSize     = 9,
            Margin       = new Thickness(12, 0, 12, 8),
            TextWrapping = TextWrapping.Wrap
        };

        // ── Volume row ─────────────────────────────────────────────────────
        int initPct = (int)Math.Round(item.Volume * 100);

        var volPct = new TextBlock
        {
            Text              = $"{initPct}%",
            Foreground        = new SolidColorBrush(Color.FromRgb(0x70, 0x70, 0x70)),
            FontSize          = 10,
            Width             = 32,
            TextAlignment     = TextAlignment.Right,
            VerticalAlignment = VerticalAlignment.Center,
            Margin            = new Thickness(0, 0, 8, 0)
        };

        var volSlider = new Slider
        {
            Minimum           = 0,
            Maximum           = 100,
            Value             = initPct,
            VerticalAlignment = VerticalAlignment.Center
        };
        volSlider.ValueChanged += (_, e) =>
        {
            int pct             = (int)Math.Round(e.NewValue);
            volPct.Text         = $"{pct}%";
            capturedItem.Volume = pct / 100f;
            SoundLibraryService.Save(_library);
        };

        var volLabel = new TextBlock
        {
            Text              = "Vol",
            Foreground        = new SolidColorBrush(Color.FromRgb(0x70, 0x70, 0x70)),
            FontSize          = 10,
            VerticalAlignment = VerticalAlignment.Center,
            Margin            = new Thickness(8, 0, 0, 0)
        };

        var volRow = new DockPanel { Margin = new Thickness(4, 0, 4, 0) };
        DockPanel.SetDock(volLabel, Dock.Left);
        DockPanel.SetDock(volPct,   Dock.Right);
        volRow.Children.Add(volLabel);
        volRow.Children.Add(volPct);
        volRow.Children.Add(volSlider);

        // ── Edit + Remove buttons ──────────────────────────────────────────
        var editBtn = new Button
        {
            Content             = "Edit",
            Foreground          = new SolidColorBrush(Color.FromRgb(0x80, 0xB8, 0xFF)),
            Style               = (Style)FindResource("CardActionButton"),
            HorizontalAlignment = HorizontalAlignment.Stretch
        };
        editBtn.Click += (_, _) => EditSound(capturedItem);

        var removeBtn = new Button
        {
            Content             = "Remove",
            Foreground          = new SolidColorBrush(Color.FromRgb(0xEF, 0x53, 0x50)),
            Style               = (Style)FindResource("CardActionButton"),
            HorizontalAlignment = HorizontalAlignment.Stretch
        };
        removeBtn.Click += (_, _) => RemoveSound(capturedItem);

        var btnGrid = new Grid { Margin = new Thickness(4, 0, 4, 4) };
        btnGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        btnGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        Grid.SetColumn(editBtn,   0);
        Grid.SetColumn(removeBtn, 1);
        btnGrid.Children.Add(editBtn);
        btnGrid.Children.Add(removeBtn);

        // ── Assemble ───────────────────────────────────────────────────────
        var stack = new StackPanel();
        stack.Children.Add(playBtn);
        stack.Children.Add(infoLine);
        stack.Children.Add(MakeDivider());
        stack.Children.Add(volRow);
        stack.Children.Add(MakeDivider());
        stack.Children.Add(btnGrid);

        return new Border
        {
            Width           = 190,
            Background      = new SolidColorBrush(Color.FromRgb(0x2A, 0x2A, 0x2A)),
            BorderBrush     = new SolidColorBrush(Color.FromRgb(0x3A, 0x3A, 0x3A)),
            BorderThickness = new Thickness(1),
            CornerRadius    = new CornerRadius(6),
            Margin          = new Thickness(0, 0, 8, 8),
            Child           = stack
        };
    }

    private static Border MakeDivider() => new Border
    {
        BorderBrush     = new SolidColorBrush(Color.FromRgb(0x38, 0x38, 0x38)),
        BorderThickness = new Thickness(0, 1, 0, 0)
    };

    // ── Add Sound ──────────────────────────────────────────────────────────────

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
                Category    = "General",
                Volume      = 1.0f,
                CreatedAt   = DateTime.UtcNow
            };

            _cachedSounds[item.Id] = sound;
            _library.Add(item);
            SoundLibraryService.Save(_library);
            RefreshCategoryFilter();
            FilterSoundsPanel();
            StatusText.Text = $"Added: {item.DisplayName}";
        }
        catch (Exception ex)
        {
            StatusText.Text = $"Add error: {ex.Message}";
        }
    }

    // ── Edit Sound ─────────────────────────────────────────────────────────────

    private void EditSound(SoundItem item)
    {
        var categories = _library
            .Select(x => string.IsNullOrWhiteSpace(x.Category) ? "General" : x.Category)
            .Distinct()
            .OrderBy(c => c)
            .ToList();

        var dlg = new EditSoundDialog(this, item.DisplayName, item.Category, categories);
        if (dlg.ShowDialog() != true)
            return;

        item.DisplayName = dlg.ResultName;
        item.Category    = dlg.ResultCategory;
        SoundLibraryService.Save(_library);
        RefreshCategoryFilter();
        FilterSoundsPanel();
        StatusText.Text = $"Updated: {item.DisplayName}";
    }

    // ── Remove Sound ───────────────────────────────────────────────────────────

    private void RemoveSound(SoundItem item)
    {
        var result = MessageBox.Show(
            $"Remove \"{item.DisplayName}\" from the library?\n\nThe audio file will not be deleted.",
            "Confirm Remove",
            MessageBoxButton.YesNo,
            MessageBoxImage.Question);

        if (result != MessageBoxResult.Yes)
            return;

        var name = item.DisplayName;
        _cachedSounds.Remove(item.Id);
        _library.Remove(item);
        SoundLibraryService.Save(_library);
        RefreshCategoryFilter();
        FilterSoundsPanel();
        StatusText.Text = $"Removed: {name}";
    }

    // ── Search and category filter ─────────────────────────────────────────────

    private void SearchBox_TextChanged(object sender, TextChangedEventArgs e)
        => FilterSoundsPanel();

    private void CategoryFilter_SelectionChanged(object sender, SelectionChangedEventArgs e)
        => FilterSoundsPanel();

    // ══════════════════════════════════════════════════════════════════════════
    //  PLAYBACK
    // ══════════════════════════════════════════════════════════════════════════

    // Plays library[index].  Hotkeys always use the library index, not the
    // filtered/visual index, so they remain stable while searching.
    private void PlayByIndex(int index)
    {
        if (index < 0 || index >= _library.Count)
            return;

        PlayLibraryItem(_library[index]);
    }

    private void PlayLibraryItem(SoundItem item)
    {
        if (!_cachedSounds.TryGetValue(item.Id, out var sound))
        {
            StatusText.Text = $"Not loaded: {item.DisplayName}";
            return;
        }

        PlaySound(sound, item.DisplayName, item.Volume);
    }

    private void PlaySound(CachedSound sound, string label, float volume = 1.0f)
    {
        if (_monitorEngine is null && _virtualEngine is null)
        {
            StatusText.Text = "No output device selected.";
            return;
        }

        try
        {
            _monitorEngine?.Play(sound, volume);

            var monitorDevice = MonitorComboBox.SelectedItem as AudioDevice;
            var virtualDevice = VirtualComboBox.SelectedItem as AudioDevice;

            bool sameDevice = _virtualEngine is not null
                           && AudioDevice.AreSameOutputDevice(monitorDevice, virtualDevice);

            if (!sameDevice)
                _virtualEngine?.Play(sound, volume);

            StatusText.Text = sameDevice
                ? $"▶ {label}  (Virtual = Monitor, playing once)"
                : $"▶ {label}";
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

    // ══════════════════════════════════════════════════════════════════════════
    //  OUTPUT DEVICES
    // ══════════════════════════════════════════════════════════════════════════

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
            _monitorEngine  = null;
            StatusText.Text = $"Monitor device error: {ex.Message}";
        }
    }

    private void CreateVirtualEngine(int deviceNumber)
    {
        try   { _virtualEngine = new AudioPlaybackEngine(deviceNumber); }
        catch (Exception ex)
        {
            _virtualEngine  = null;
            StatusText.Text = $"Virtual device error: {ex.Message}";
        }
    }

    // ══════════════════════════════════════════════════════════════════════════
    //  MICROPHONE
    // ══════════════════════════════════════════════════════════════════════════

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
    {
        _micPassthrough?.SetVolume((float)(MicVolumeSlider.Value / 100.0));
        if (MicVolumeLabel is not null)
            MicVolumeLabel.Text = $"{(int)MicVolumeSlider.Value}%";
    }

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

    // ══════════════════════════════════════════════════════════════════════════
    //  HOTKEYS
    // ══════════════════════════════════════════════════════════════════════════

    private void RegisterHotkeys()
    {
        try
        {
            _hotkeys = new HotkeyManager(this);
            _hotkeys.HotkeyPressed += OnHotkeyPressed;
            Debug.WriteLine("[Hotkeys] HotkeyManager created — HWND and HwndSource OK");

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
                    Debug.WriteLine($"[Hotkeys] FAILED: {Label} — likely AltGr conflict");
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
            Debug.WriteLine($"[Hotkeys] Exception during setup: {ex.Message}");
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
