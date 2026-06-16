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
using System.Windows.Input;
using System.Windows.Media;
using ControlAppearance = Wpf.Ui.Controls.ControlAppearance;
using SymbolRegular = Wpf.Ui.Controls.SymbolRegular;
using UiBadge = Wpf.Ui.Controls.Badge;
using UiButton = Wpf.Ui.Controls.Button;
using UiCard = Wpf.Ui.Controls.Card;
using UiSymbolIcon = Wpf.Ui.Controls.SymbolIcon;

namespace SoundPad.App;

public partial class MainWindow : Wpf.Ui.Controls.FluentWindow
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
    private HotkeyService?  _hotkeyService;
    private MicPassthrough? _micPassthrough;

    // ── Constructor ────────────────────────────────────────────────────────────
    public MainWindow()
    {
        InitializeComponent();
    }

    // ── OnSourceInitialized: earliest safe point for hotkeys ──────────────────
    // WPF guarantees both the Win32 HWND and its HwndSource exist here.
    // Library hotkeys aren't loaded yet at this point, so registration happens
    // later in LoadLibrary(), once _library is populated.
    protected override void OnSourceInitialized(EventArgs e)
    {
        base.OnSourceInitialized(e);
        try
        {
            _hotkeys = new HotkeyManager(this);
            _hotkeyService = new HotkeyService(_hotkeys);
            _hotkeyService.HotkeyTriggered += OnSoundHotkeyTriggered;
        }
        catch (Exception ex)
        {
            _hotkeys = null;
            _hotkeyService = null;
            Debug.WriteLine($"[Hotkeys] Setup failed: {ex.Message}");
        }
    }

    private void OnSoundHotkeyTriggered(Guid soundId)
    {
        var item = _library.FirstOrDefault(x => x.Id == soundId);
        if (item is not null)
            PlayLibraryItem(item);
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

        bool hotkeysMigrated = MigrateDefaultHotkeys();

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

        if (hotkeysMigrated)
            SoundLibraryService.Save(_library);

        RefreshCategoryFilter();
        FilterSoundsPanel();

        var failedHotkeys = _hotkeyService?.ReregisterAll(_library) ?? new HashSet<Guid>();

        int loaded = _cachedSounds.Count;
        StatusText.Text = failedHotkeys.Count > 0
            ? $"Library: {loaded} sound(s) loaded — {failedHotkeys.Count} hotkey(s) could not be registered"
            : loaded > 0
                ? $"Library: {loaded} sound(s) loaded"
                : "Library empty — click '+ Add Sound'";
    }

    // One-time migration: the first four sounds keep their original default
    // hotkeys (Ctrl+Alt+1..4) the first time hotkey data is seen for them.
    // HotkeyInitialized guards against re-seeding after a deliberate user clear.
    private bool MigrateDefaultHotkeys()
    {
        bool changed = false;

        for (int i = 0; i < _library.Count; i++)
        {
            var item = _library[i];
            if (item.HotkeyInitialized)
                continue;

            if (i < 4)
                item.Hotkey = CreateDefaultHotkey(i);

            item.HotkeyInitialized = true;
            changed = true;
        }

        return changed;
    }

    private static HotkeyBinding CreateDefaultHotkey(int slotIndex)
    {
        uint vk = (uint)(0x31 + slotIndex); // '1'..'4'
        return new HotkeyBinding
        {
            Modifiers   = (uint)(ModifierKeys.Control | ModifierKeys.Alt),
            Key         = vk,
            DisplayText = $"Ctrl+Alt+{slotIndex + 1}"
        };
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

        foreach (var item in _library)
        {
            bool matchSearch   = string.IsNullOrEmpty(search)
                              || item.DisplayName.ToLowerInvariant().Contains(search);
            bool matchCategory = category == "All" || item.Category == category;

            if (!matchSearch || !matchCategory)
                continue;

            SoundsPanel.Children.Add(BuildSoundRow(item));
        }

        bool panelEmpty = SoundsPanel.Children.Count == 0;
        EmptyLibraryText.Visibility = panelEmpty ? Visibility.Visible : Visibility.Collapsed;
        EmptyLibraryText.Text = _library.Count == 0
            ? "No sounds yet — click \"+ Add Sound\" to import an audio file."
            : "No sounds match your search or filter.";

        if (LibraryCountText is not null)
            LibraryCountText.Content = $"{SoundsPanel.Children.Count}";
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

    // ── Sound row builder (details/list view) ───────────────────────────────────

    // Column widths shared by the static XAML header and every row built here,
    // so the two stay pixel-aligned: Play | Name | Category | Hotkey | Volume | Created | Actions
    private static void AddRowColumns(Grid grid)
    {
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(48) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(110) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(140) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(170) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(95) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(84) });
    }

    // Creates one details-list row for a SoundItem. Hotkey display now comes
    // from the item's own HotkeyBinding, not its position in the library.
    private Border BuildSoundRow(SoundItem item)
    {
        var capturedItem = item;
        var categoryText = string.IsNullOrWhiteSpace(item.Category) ? "General" : item.Category;
        var accentBrush  = (Brush)Application.Current.Resources["SystemAccentColorPrimaryBrush"];

        var grid = new Grid();
        AddRowColumns(grid);

        // ── Col 0: Play ──────────────────────────────────────────────────
        var playBtn = new UiButton
        {
            Appearance          = ControlAppearance.Transparent,
            Padding             = new Thickness(8),
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment   = VerticalAlignment.Center,
            Icon                = new UiSymbolIcon { Symbol = SymbolRegular.Play20 }
        };
        playBtn.Click += (_, _) => PlayLibraryItem(capturedItem);
        Grid.SetColumn(playBtn, 0);
        grid.Children.Add(playBtn);

        // ── Col 1: Name ──────────────────────────────────────────────────
        var nameBlock = new TextBlock
        {
            Text              = item.DisplayName,
            Foreground        = (Brush)Application.Current.Resources["TextFillColorPrimaryBrush"],
            FontSize          = 13,
            FontWeight        = FontWeights.SemiBold,
            TextTrimming      = TextTrimming.CharacterEllipsis,
            VerticalAlignment = VerticalAlignment.Center,
            Margin            = new Thickness(4, 0, 8, 0)
        };
        Grid.SetColumn(nameBlock, 1);
        grid.Children.Add(nameBlock);

        // ── Col 2: Category ──────────────────────────────────────────────
        var catBadge = new UiBadge
        {
            Content             = categoryText,
            Appearance          = ControlAppearance.Success,
            FontSize            = 9,
            HorizontalAlignment = HorizontalAlignment.Left,
            VerticalAlignment   = VerticalAlignment.Center
        };
        Grid.SetColumn(catBadge, 2);
        grid.Children.Add(catBadge);

        // ── Col 3: Hotkey (click to open the capture dialog) ──────────────
        var hotkeyContent = new TextBlock
        {
            Text         = item.Hotkey?.DisplayText ?? "No hotkey",
            TextTrimming = TextTrimming.CharacterEllipsis,
            MaxWidth     = 118
        };
        var hotkeyBtn = new UiButton
        {
            Content                    = hotkeyContent,
            Appearance                 = ControlAppearance.Transparent,
            FontSize                   = 11,
            FontWeight                 = item.Hotkey is not null ? FontWeights.SemiBold : FontWeights.Normal,
            Foreground                 = item.Hotkey is not null ? accentBrush : (Brush)Application.Current.Resources["TextFillColorTertiaryBrush"],
            HorizontalAlignment        = HorizontalAlignment.Left,
            HorizontalContentAlignment = HorizontalAlignment.Left,
            Padding                    = new Thickness(8, 4, 8, 4),
            ToolTip                    = "Click to set or change this sound's hotkey"
        };
        hotkeyBtn.Click += (_, _) => OpenHotkeyCapture(capturedItem);
        Grid.SetColumn(hotkeyBtn, 3);
        grid.Children.Add(hotkeyBtn);

        // ── Col 4: Volume ────────────────────────────────────────────────
        int initPct = (int)Math.Round(item.Volume * 100);

        var volPct = new TextBlock
        {
            Text              = $"{initPct}%",
            Foreground        = (Brush)Application.Current.Resources["TextFillColorTertiaryBrush"],
            FontSize          = 10,
            Width             = 34,
            TextAlignment     = TextAlignment.Right,
            VerticalAlignment = VerticalAlignment.Center
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

        var volRow = new DockPanel { VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 12, 0) };
        DockPanel.SetDock(volPct, Dock.Right);
        volRow.Children.Add(volPct);
        volRow.Children.Add(volSlider);
        Grid.SetColumn(volRow, 4);
        grid.Children.Add(volRow);

        // ── Col 5: Created ───────────────────────────────────────────────
        var createdText = new TextBlock
        {
            Text              = item.CreatedAt.ToLocalTime().ToString("MMM d, yyyy"),
            FontSize          = 11,
            Foreground        = (Brush)Application.Current.Resources["TextFillColorTertiaryBrush"],
            VerticalAlignment = VerticalAlignment.Center
        };
        Grid.SetColumn(createdText, 5);
        grid.Children.Add(createdText);

        // ── Col 6: Actions (subtle icon buttons) ────────────────────────
        var editBtn = new UiButton
        {
            Appearance = ControlAppearance.Transparent,
            Padding    = new Thickness(6),
            ToolTip    = "Edit",
            Icon       = new UiSymbolIcon { Symbol = SymbolRegular.Edit16 }
        };
        editBtn.Click += (_, _) => EditSound(capturedItem);

        var removeBtn = new UiButton
        {
            Appearance = ControlAppearance.Transparent,
            Padding    = new Thickness(6),
            Margin     = new Thickness(4, 0, 0, 0),
            ToolTip    = "Remove",
            Foreground = (Brush)Application.Current.Resources["SystemFillColorCriticalBrush"],
            Icon       = new UiSymbolIcon { Symbol = SymbolRegular.Delete16 }
        };
        removeBtn.Click += (_, _) => RemoveSound(capturedItem);

        var actionsPanel = new StackPanel
        {
            Orientation         = Orientation.Horizontal,
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment   = VerticalAlignment.Center
        };
        actionsPanel.Children.Add(editBtn);
        actionsPanel.Children.Add(removeBtn);
        Grid.SetColumn(actionsPanel, 6);
        grid.Children.Add(actionsPanel);

        // ── Row container: hover highlight + double-click to play ──────────
        var row = new Border
        {
            Padding         = new Thickness(14, 8, 14, 8),
            Background      = Brushes.Transparent,
            BorderBrush     = (Brush)Application.Current.Resources["CardBorderBrush"],
            BorderThickness = new Thickness(0, 0, 0, 1),
            Child           = grid
        };
        var hoverBg = (Brush)Application.Current.Resources["ControlFillColorDefaultBrush"];
        row.MouseEnter += (_, _) => row.Background = hoverBg;
        row.MouseLeave += (_, _) => row.Background = Brushes.Transparent;
        row.MouseLeftButtonDown += (_, e) =>
        {
            if (e.ClickCount == 2)
                PlayLibraryItem(capturedItem);
        };
        return row;
    }

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

        var hotkeyDisplay = item.Hotkey?.DisplayText ?? "";

        var dlg = new EditSoundDialog(this, item.DisplayName, item.Category, categories, hotkeyDisplay);
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

    // ── Hotkey assignment ────────────────────────────────────────────────────

    private void OpenHotkeyCapture(SoundItem item)
    {
        if (_hotkeyService is null)
        {
            MessageBox.Show("Hotkeys are not available in this session.", "Hotkeys",
                             MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        var dlg = new HotkeyCaptureDialog(this, item.DisplayName, item.Hotkey);
        if (dlg.ShowDialog() != true)
            return; // cancelled — nothing changes

        if (dlg.WasCleared)
        {
            item.Hotkey = null;
            _hotkeyService.ReregisterAll(_library);
            SoundLibraryService.Save(_library);
            FilterSoundsPanel();
            StatusText.Text = $"Hotkey cleared: {item.DisplayName}";
            return;
        }

        var newBinding = dlg.ResultBinding;
        if (newBinding is null)
            return;

        // In-app conflict: another sound already uses this exact combination.
        var conflict = _library.FirstOrDefault(x => x.Id != item.Id && x.Hotkey is not null
                           && x.Hotkey.Modifiers == newBinding.Modifiers && x.Hotkey.Key == newBinding.Key);
        if (conflict is not null)
        {
            MessageBox.Show(
                $"\"{newBinding.DisplayText}\" is already assigned to \"{conflict.DisplayName}\".\n\n" +
                "Choose a different combination.",
                "Hotkey already in use", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        var previousBinding = item.Hotkey;
        item.Hotkey = newBinding;

        var failed = _hotkeyService.ReregisterAll(_library);
        if (failed.Contains(item.Id))
        {
            // Roll back and restore the previously-working hotkey set.
            item.Hotkey = previousBinding;
            _hotkeyService.ReregisterAll(_library);
            MessageBox.Show(
                $"Windows could not register \"{newBinding.DisplayText}\".\n\n" +
                "It may already be used by another application or by Windows itself. " +
                "Choose a different combination.",
                "Hotkey unavailable", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        SoundLibraryService.Save(_library);
        FilterSoundsPanel();
        StatusText.Text = $"Hotkey set: {item.DisplayName} → {newBinding.DisplayText}";
    }

    // ── Search and category filter ─────────────────────────────────────────────

    private void SearchBox_TextChanged(object sender, TextChangedEventArgs e)
        => FilterSoundsPanel();

    private void CategoryFilter_SelectionChanged(object sender, SelectionChangedEventArgs e)
        => FilterSoundsPanel();

    // ══════════════════════════════════════════════════════════════════════════
    //  PLAYBACK
    // ══════════════════════════════════════════════════════════════════════════

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

    // ── Window closing ────────────────────────────────────────────────────────

    private void Window_Closing(object sender, System.ComponentModel.CancelEventArgs e)
    {
        _hotkeyService?.UnregisterAll();
        _hotkeys?.Dispose();
        _micPassthrough?.Dispose();
        _monitorEngine?.Dispose();
        _virtualEngine?.Dispose();
    }

}
