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
using System.Windows.Threading;
using WinForms = System.Windows.Forms;
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

    // ── App-level settings (devices, mic state, hotkeys, window bounds) ─────────
    private AppSettings _settings;

    // ── Supporting objects ─────────────────────────────────────────────────────
    private HotkeyManager?        _hotkeys;
    private HotkeyService?        _hotkeyService;
    private MicPassthrough?       _micPassthrough;
    private WinForms.NotifyIcon?  _trayIcon;

    // Set to true by ExitApp() so Window_Closing knows to proceed with shutdown
    // rather than hide to tray when CloseToTray is enabled.
    private bool _isExiting;

    // ── Constructor ────────────────────────────────────────────────────────────
    // Settings are loaded before InitializeComponent so the saved window
    // bounds can be applied before the window is ever shown (no visible jump).
    public MainWindow()
    {
        Debug.WriteLine("[Startup] Constructor start");
        _settings = AppSettingsService.Load();
        Debug.WriteLine("[Startup] Settings loaded");
        InitializeComponent();
        Debug.WriteLine("[Startup] InitializeComponent done");
        RestoreWindowBounds();
        Debug.WriteLine("[Startup] Constructor end");
    }

    private void RestoreWindowBounds()
    {
        if (_settings.WindowWidth is > 0 && _settings.WindowHeight is > 0)
        {
            Width  = _settings.WindowWidth.Value;
            Height = _settings.WindowHeight.Value;
        }

        if (_settings.WindowLeft is double left && _settings.WindowTop is double top && IsOnScreen(left, top))
        {
            WindowStartupLocation = WindowStartupLocation.Manual;
            Left = left;
            Top  = top;
        }
    }

    // Guards against restoring a position on a monitor that's no longer connected.
    private static bool IsOnScreen(double left, double top)
    {
        double virtualLeft   = SystemParameters.VirtualScreenLeft;
        double virtualTop    = SystemParameters.VirtualScreenTop;
        double virtualRight  = virtualLeft + SystemParameters.VirtualScreenWidth;
        double virtualBottom = virtualTop  + SystemParameters.VirtualScreenHeight;

        return left >= virtualLeft && left < virtualRight
            && top  >= virtualTop  && top  < virtualBottom;
    }

    private void SaveSettings() => AppSettingsService.Save(_settings);

    // Called by the global DispatcherUnhandledException handler in App.xaml.cs
    // so the user sees a status-bar message instead of a silent crash.
    public void ShowFatalError(string message)
    {
        try { StatusText.Text = message; } catch { }
    }

    // ── System tray ────────────────────────────────────────────────────────────

    private void InitializeTrayIcon()
    {
        var appIcon = GetAppIcon();
        SetWindowIcon(appIcon);

        _trayIcon = new WinForms.NotifyIcon
        {
            Text    = "SoundPad",
            Icon    = appIcon,
            Visible = true
        };

        var menu = new WinForms.ContextMenuStrip();
        menu.Items.Add("Show SoundPad",    null, (_, _) => Dispatcher.Invoke(ShowFromTray));
        menu.Items.Add(new WinForms.ToolStripSeparator());
        menu.Items.Add("Stop All Sounds",  null, (_, _) => Dispatcher.Invoke(StopAllSounds));
        menu.Items.Add(new WinForms.ToolStripSeparator());
        menu.Items.Add("Exit",             null, (_, _) => Dispatcher.Invoke(ExitApp));

        _trayIcon.ContextMenuStrip = menu;
        _trayIcon.DoubleClick     += (_, _) => Dispatcher.Invoke(ShowFromTray);
    }

    // Returns the best available icon for both the WPF window and the tray.
    // Priority: bundled Resources\app.ico > exe's own associated icon > OS default.
    private static System.Drawing.Icon GetAppIcon()
    {
        var resourceIco = Path.Combine(AppContext.BaseDirectory, "Resources", "app.ico");
        if (File.Exists(resourceIco))
        {
            try { return new System.Drawing.Icon(resourceIco); }
            catch { }
        }

        try
        {
            var path = Environment.ProcessPath;
            if (!string.IsNullOrEmpty(path))
            {
                var ico = System.Drawing.Icon.ExtractAssociatedIcon(path);
                if (ico is not null) return ico;
            }
        }
        catch { }

        return System.Drawing.SystemIcons.Application;
    }

    // Converts a Win32 HICON to a WPF ImageSource so Window.Icon can be set.
    private void SetWindowIcon(System.Drawing.Icon icon)
    {
        try
        {
            Icon = System.Windows.Interop.Imaging.CreateBitmapSourceFromHIcon(
                icon.Handle,
                System.Windows.Int32Rect.Empty,
                System.Windows.Media.Imaging.BitmapSizeOptions.FromEmptyOptions());
        }
        catch { }
    }

    private void ShowFromTray()
    {
        Show();
        WindowState = WindowState.Normal;
        Activate();
    }

    // Called from the tray "Exit" item and any path that needs a real shutdown.
    // Sets _isExiting so Window_Closing does not cancel the close for CloseToTray.
    private void ExitApp()
    {
        _isExiting = true;
        Close();
    }

    private void Window_StateChanged(object sender, EventArgs e)
    {
        if (WindowState == WindowState.Minimized && _settings.MinimizeToTray)
            Hide();
    }

    private void RestoreBehaviorSettings()
    {
        MinimizeToTraySwitch.IsChecked = _settings.MinimizeToTray;
        CloseToTraySwitch.IsChecked    = _settings.CloseToTray;

        // Sync the toggle with the actual registry state so it stays accurate
        // even if the user manually edited the registry or moved the exe.
        bool actualStartup = ReadStartWithWindowsRegistryState();
        if (_settings.StartWithWindows != actualStartup)
        {
            _settings.StartWithWindows = actualStartup;
            SaveSettings();
        }

        // Unhook while setting IsChecked to avoid triggering a registry write
        // on every startup (the registry is already in the correct state).
        StartWithWindowsSwitch.Checked   -= StartWithWindowsSwitch_Checked;
        StartWithWindowsSwitch.Unchecked -= StartWithWindowsSwitch_Unchecked;
        StartWithWindowsSwitch.IsChecked  = _settings.StartWithWindows;
        StartWithWindowsSwitch.Checked   += StartWithWindowsSwitch_Checked;
        StartWithWindowsSwitch.Unchecked += StartWithWindowsSwitch_Unchecked;
    }

    // ── Behavior toggle handlers ───────────────────────────────────────────────

    private void MinimizeToTraySwitch_Checked(object sender, RoutedEventArgs e)
    {
        _settings.MinimizeToTray = true;
        SaveSettings();
    }

    private void MinimizeToTraySwitch_Unchecked(object sender, RoutedEventArgs e)
    {
        _settings.MinimizeToTray = false;
        SaveSettings();
    }

    private void CloseToTraySwitch_Checked(object sender, RoutedEventArgs e)
    {
        _settings.CloseToTray = true;
        SaveSettings();
    }

    private void CloseToTraySwitch_Unchecked(object sender, RoutedEventArgs e)
    {
        _settings.CloseToTray = false;
        SaveSettings();
    }

    private void StartWithWindowsSwitch_Checked(object sender, RoutedEventArgs e)
    {
        _settings.StartWithWindows = true;
        ApplyStartWithWindows(true);
        SaveSettings();
    }

    private void StartWithWindowsSwitch_Unchecked(object sender, RoutedEventArgs e)
    {
        _settings.StartWithWindows = false;
        ApplyStartWithWindows(false);
        SaveSettings();
    }

    // ── Startup with Windows (HKCU Run key, no admin required) ────────────────

    private const string StartupRegistryPath  = @"Software\Microsoft\Windows\CurrentVersion\Run";
    private const string StartupRegistryValue = "SoundPad";

    private void ApplyStartWithWindows(bool enable)
    {
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(StartupRegistryPath, writable: true);
            if (key is null)
            {
                StatusText.Text = "Could not open startup registry key.";
                return;
            }

            if (enable)
            {
                var exePath = Environment.ProcessPath;
                if (string.IsNullOrEmpty(exePath))
                {
                    StatusText.Text = "Could not determine app path for startup entry.";
                    return;
                }
                key.SetValue(StartupRegistryValue, $"\"{exePath}\"");
                StatusText.Text = "SoundPad will start with Windows.";
            }
            else
            {
                key.DeleteValue(StartupRegistryValue, throwOnMissingValue: false);
                StatusText.Text = "Removed SoundPad from Windows startup.";
            }
        }
        catch (Exception ex)
        {
            StatusText.Text = $"Startup setting failed: {ex.Message}";

            // Revert the toggle and saved setting so they stay consistent.
            _settings.StartWithWindows = !enable;
            SaveSettings();
            StartWithWindowsSwitch.Checked   -= StartWithWindowsSwitch_Checked;
            StartWithWindowsSwitch.Unchecked -= StartWithWindowsSwitch_Unchecked;
            StartWithWindowsSwitch.IsChecked  = _settings.StartWithWindows;
            StartWithWindowsSwitch.Checked   += StartWithWindowsSwitch_Checked;
            StartWithWindowsSwitch.Unchecked += StartWithWindowsSwitch_Unchecked;
        }
    }

    private static bool ReadStartWithWindowsRegistryState()
    {
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(StartupRegistryPath);
            return key?.GetValue(StartupRegistryValue) is not null;
        }
        catch { return false; }
    }

    // If StartWithWindows is on, verifies the registry entry points to the
    // currently running exe. Fixes it silently when it differs (e.g. after
    // publishing replaces the dev-build path). Shows a status message only
    // on failure; success is intentionally silent so it does not obscure the
    // library-loaded message shown just before this runs.
    private void SyncStartupRegistryPath()
    {
        if (!_settings.StartWithWindows) return;

        var exePath = Environment.ProcessPath;
        if (string.IsNullOrEmpty(exePath)) return;

        var expected = $"\"{exePath}\"";

        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(StartupRegistryPath, writable: true);
            if (key is null) return;

            var current = key.GetValue(StartupRegistryValue) as string;
            if (string.Equals(current, expected, StringComparison.OrdinalIgnoreCase)) return;

            key.SetValue(StartupRegistryValue, expected);
        }
        catch (Exception ex)
        {
            StatusText.Text = $"Could not update startup entry: {ex.Message}";
        }
    }

    // ── Helpers ────────────────────────────────────────────────────────────────

    // Returns true for known virtual audio router products so we can show
    // the Discord routing hint when one of these is selected as Virtual Output.
    private static bool IsVirtualAudioRouterDevice(string name) =>
        name.Contains("CABLE",       StringComparison.OrdinalIgnoreCase) ||
        name.Contains("Voicemeeter", StringComparison.OrdinalIgnoreCase);

    private static string GetAppVersion()
    {
        var v = System.Reflection.Assembly.GetExecutingAssembly().GetName().Version;
        return v is null ? "1.0.0" : $"{v.Major}.{v.Minor}.{v.Build}";
    }

    // Finds the index of a previously-saved device in a freshly-enumerated list.
    // Matches by name first (most stable across restarts), falling back to the
    // saved device number. Returns -1 if neither matches.
    private static int FindDeviceIndex<T>(IList<T> items, string? savedName, int? savedNumber,
                                           Func<T, string> nameOf, Func<T, int> numberOf)
    {
        if (!string.IsNullOrEmpty(savedName))
        {
            for (int i = 0; i < items.Count; i++)
                if (nameOf(items[i]) == savedName)
                    return i;
        }

        if (savedNumber is not null)
        {
            for (int i = 0; i < items.Count; i++)
                if (numberOf(items[i]) == savedNumber.Value)
                    return i;
        }

        return -1;
    }

    // ── OnSourceInitialized: earliest safe point for hotkeys ──────────────────
    // WPF guarantees both the Win32 HWND and its HwndSource exist here.
    // Library hotkeys aren't loaded yet at this point, so registration is
    // deferred until ContextIdle in MainWindow_Loaded.
    protected override void OnSourceInitialized(EventArgs e)
    {
        Debug.WriteLine("[Startup] OnSourceInitialized start");
        base.OnSourceInitialized(e);
        try
        {
            var helper = new System.Windows.Interop.WindowInteropHelper(this);
            var hwnd   = helper.Handle;
            Debug.WriteLine($"[Startup] HWND = 0x{hwnd:X16} {(hwnd == IntPtr.Zero ? "— ZERO, hotkeys will fail" : "OK")}");

            var src = System.Windows.Interop.HwndSource.FromHwnd(hwnd);
            Debug.WriteLine($"[Startup] HwndSource = {(src is null ? "NULL — hotkeys will fail" : "OK")}");

            _hotkeys = new HotkeyManager(this);
            Debug.WriteLine("[Startup] HotkeyManager created");

            _hotkeyService = new HotkeyService(_hotkeys);
            _hotkeyService.HotkeyTriggered        += OnSoundHotkeyTriggered;
            _hotkeyService.StopAllHotkeyTriggered += OnStopAllHotkeyTriggered;
            Debug.WriteLine("[Startup] HotkeyService ready");
        }
        catch (Exception ex)
        {
            _hotkeys       = null;
            _hotkeyService = null;
            Debug.WriteLine($"[Startup] Hotkey init FAILED ({ex.GetType().Name}): {ex.Message}");
            Debug.WriteLine($"[Startup] {ex.StackTrace}");
        }
        Debug.WriteLine("[Startup] OnSourceInitialized end");
    }

    private void OnSoundHotkeyTriggered(Guid soundId)
    {
        var item = _library.FirstOrDefault(x => x.Id == soundId);
        if (item is not null)
            PlayLibraryItem(item);
    }

    private void OnStopAllHotkeyTriggered() => StopAllSounds();

    // ── Loaded: sound library + device lists ──────────────────────────────────
    private void MainWindow_Loaded(object sender, RoutedEventArgs e)
    {
        Debug.WriteLine("[Startup] Loaded start");
        try
        {
            LoadLibrary();
            Debug.WriteLine("[Startup] LoadLibrary done");

            PopulateDeviceLists();
            Debug.WriteLine("[Startup] PopulateDeviceLists done");

            PopulateMicList();
            Debug.WriteLine("[Startup] PopulateMicList done");

            // Mic passthrough depends on both the virtual engine and the mic
            // device list being ready, so it's restored only after both calls above.
            RestoreMicPassthroughState();
            RestoreSelectedTab();
            RestoreBehaviorSettings();
            SyncStartupRegistryPath();
            InitializeTrayIcon();
            AboutVersionText.Text    = $"Version {GetAppVersion()}";
            AboutDataFolderText.Text = AppPaths.AppDataDir;

            // Defer hotkey registration until the dispatcher is idle so that
            // WPF-UI (Mica backdrop, DWM attributes, title-bar composition) has
            // fully finished its own Loaded/Render work.  RegisterHotKey called
            // during the Loaded burst can fail transiently; ContextIdle fires
            // only after all pending Render/DataBind/Normal priority items drain.
            // The lambda has its own try/catch so an exception here never
            // crashes the process — only a status-bar message is shown.
            Debug.WriteLine("[Startup] Loaded end — queuing deferred hotkey registration");
            Dispatcher.BeginInvoke(DispatcherPriority.ContextIdle, new Action(() =>
            {
                Debug.WriteLine("[Startup] Deferred hotkey registration start");
                try
                {
                    ReregisterAllHotkeysAndReport("startup");
                    Debug.WriteLine("[Startup] Deferred hotkey registration complete");
                }
                catch (Exception ex)
                {
                    Debug.WriteLine($"[Startup] Deferred hotkey registration FAILED ({ex.GetType().Name}): {ex.Message}");
                    Debug.WriteLine($"[Startup] {ex.StackTrace}");
                    try { StatusText.Text = $"Hotkey startup error: {ex.Message}"; } catch { }
                }
            }));
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[Startup] Loaded FAILED ({ex.GetType().Name}): {ex.Message}");
            Debug.WriteLine($"[Startup] {ex.StackTrace}");
            try { StatusText.Text = $"Startup error: {ex.Message}"; } catch { }
        }
        Debug.WriteLine("[Startup] Loaded handler returning");
    }

    private void RestoreMicPassthroughState()
    {
        MicVolumeSlider.Value = Math.Clamp(_settings.MicVolume, 0, 100);
        MicVolumeLabel.Text   = $"{(int)MicVolumeSlider.Value}%";

        if (_settings.MicPassthroughEnabled)
            MicPassthroughCheckBox.IsChecked = true; // triggers MicPassthroughCheckBox_Checked -> StartMicPassthrough()
    }

    private void RestoreSelectedTab()
    {
        if (_settings.SelectedTabIndex >= 0 && _settings.SelectedTabIndex < MainTabControl.Items.Count)
            MainTabControl.SelectedIndex = _settings.SelectedTabIndex;
    }

    private void MainTabControl_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (MainTabControl.SelectedIndex < 0)
            return;

        _settings.SelectedTabIndex = MainTabControl.SelectedIndex;
        SaveSettings();
    }

    // ══════════════════════════════════════════════════════════════════════════
    //  SOUND LIBRARY
    // ══════════════════════════════════════════════════════════════════════════

    // Loads sounds.json, preloads all audio files into CachedSound objects,
    // then renders the sound cards.
    private void LoadLibrary()
    {
        // _settings was already loaded in the constructor (so window bounds
        // could be applied before the window was shown); reused here as-is.
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
        RefreshStopAllHotkeyDisplay();

        int loaded = _cachedSounds.Count;
        StatusText.Text = loaded > 0
            ? $"Library: {loaded} sound(s) loaded"
            : "Library empty — click '+ Add Sound'";
    }

    // Single registration point used by every code path that touches hotkeys:
    // startup (via ContextIdle Dispatcher.BeginInvoke), hotkey capture dialog
    // Save/Clear, and Stop All dialog Save/Clear.
    // Always writes diagnostics to Debug output so failures are visible in the
    // VS Output window even when StatusText is later overridden by a success msg.
    // Sets StatusText only on failure; interactive callers override it on success.
    // Returns the raw result so callers can inspect specific failures for rollback.
    private HotkeyRegistrationResult ReregisterAllHotkeysAndReport(string context)
    {
        int soundCount = _library.Count(x => x.Hotkey is not null);
        Debug.WriteLine($"[Hotkeys:{context}] Starting — {soundCount} sound hotkey(s), " +
                        $"Stop All: {_settings.StopAllHotkey?.DisplayText ?? "none"}");

        if (_hotkeyService is null)
        {
            Debug.WriteLine($"[Hotkeys:{context}] HotkeyService is null — registration skipped.");
            StatusText.Text = "Warning: global hotkeys unavailable (window initialization failed).";
            return new HotkeyRegistrationResult();
        }

        var result = _hotkeyService.ReregisterAll(_library, _settings.StopAllHotkey);

        int total  = soundCount + (_settings.StopAllHotkey is not null ? 1 : 0);
        int failed = result.FailedSoundIds.Count + (result.StopAllFailed ? 1 : 0);
        Debug.WriteLine($"[Hotkeys:{context}] Registered {total - failed}/{total}, {failed} failed.");

        foreach (var failId in result.FailedSoundIds)
        {
            var name = _library.FirstOrDefault(x => x.Id == failId)?.DisplayName ?? failId.ToString("B");
            Debug.WriteLine($"[Hotkeys:{context}] FAILED sound \"{name}\" — Win32 RegisterHotKey returned false");
        }
        if (result.StopAllFailed)
            Debug.WriteLine($"[Hotkeys:{context}] FAILED Stop All hotkey — Win32 RegisterHotKey returned false");

        if (failed > 0)
        {
            var problems = new List<string>();
            if (result.FailedSoundIds.Count > 0)
                problems.Add($"{result.FailedSoundIds.Count} sound hotkey(s)");
            if (result.StopAllFailed)
                problems.Add("Stop All hotkey");
            StatusText.Text = $"Ready — could not register: {string.Join(", ", problems)}";
        }

        return result;
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

            if (i < 4 && item.Hotkey is null)
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
        var search   = SearchBox?.Text.Trim() ?? "";
        var category = CategoryFilter?.SelectedItem as string ?? "All";

        IEnumerable<SoundItem> source = category == "Recent"
            ? _library.OrderByDescending(x => x.LastPlayedAt ?? DateTime.MinValue)
            : _library;

        SoundsPanel.Children.Clear();

        foreach (var item in source)
        {
            bool matchSearch = string.IsNullOrEmpty(search)
                            || item.DisplayName.Contains(search, StringComparison.OrdinalIgnoreCase);
            bool matchCategory = category switch
            {
                "All"       => true,
                "Favorites" => item.IsFavorite,
                "Recent"    => item.LastPlayedAt.HasValue
                            && (DateTime.UtcNow - item.LastPlayedAt.Value).TotalDays <= 7,
                _           => item.Category == category
            };

            if (!matchSearch || !matchCategory)
                continue;

            SoundsPanel.Children.Add(BuildSoundRow(item));
        }

        bool panelEmpty = SoundsPanel.Children.Count == 0;
        EmptyLibraryText.Visibility = panelEmpty ? Visibility.Visible : Visibility.Collapsed;
        EmptyLibraryText.Text = _library.Count == 0
            ? "No sounds yet — click \"+ Add Sound\" to import an audio file."
            : category == "Favorites"
                ? "No favorites yet — click the star on a sound to mark it as a favorite."
                : category == "Recent"
                    ? "No sounds played in the last 7 days."
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
        CategoryFilter.Items.Add("Favorites");
        CategoryFilter.Items.Add("Recent");

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
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(120) });
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
        var favBtn = new UiButton
        {
            Appearance = ControlAppearance.Transparent,
            Padding    = new Thickness(6),
            ToolTip    = item.IsFavorite ? "Remove from favorites" : "Add to favorites",
            Foreground = item.IsFavorite ? accentBrush : (Brush)Application.Current.Resources["TextFillColorTertiaryBrush"],
            Icon       = new UiSymbolIcon { Symbol = item.IsFavorite ? SymbolRegular.Star16 : SymbolRegular.StarOff16 }
        };
        favBtn.Click += (_, _) =>
        {
            capturedItem.IsFavorite = !capturedItem.IsFavorite;
            SoundLibraryService.Save(_library);
            FilterSoundsPanel();
        };

        var editBtn = new UiButton
        {
            Appearance = ControlAppearance.Transparent,
            Padding    = new Thickness(6),
            Margin     = new Thickness(4, 0, 0, 0),
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
        actionsPanel.Children.Add(favBtn);
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
        var dlg = new ConfirmRemoveDialog(this, item.DisplayName);
        if (dlg.ShowDialog() != true)
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

    // Returns the display name of whatever already owns this combination
    // (a sound, or "Stop All Sounds"), or null if the combination is free.
    // excludeSoundId lets a sound's own current binding be excluded from the check.
    private string? FindHotkeyOwner(HotkeyBinding binding, Guid? excludeSoundId)
    {
        var sound = _library.FirstOrDefault(x => (excludeSoundId is null || x.Id != excludeSoundId)
                        && x.Hotkey is not null
                        && x.Hotkey.Modifiers == binding.Modifiers && x.Hotkey.Key == binding.Key);
        if (sound is not null)
            return sound.DisplayName;

        if (_settings.StopAllHotkey is not null
            && _settings.StopAllHotkey.Modifiers == binding.Modifiers
            && _settings.StopAllHotkey.Key == binding.Key)
            return "Stop All Sounds";

        return null;
    }

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
            ReregisterAllHotkeysAndReport("hotkey-clear");
            SoundLibraryService.Save(_library);
            FilterSoundsPanel();
            StatusText.Text = $"Hotkey cleared: {item.DisplayName}";
            return;
        }

        var newBinding = dlg.ResultBinding;
        if (newBinding is null)
            return;

        var owner = FindHotkeyOwner(newBinding, item.Id);
        if (owner is not null)
        {
            MessageBox.Show(
                $"\"{newBinding.DisplayText}\" is already assigned to \"{owner}\".\n\n" +
                "Choose a different combination.",
                "Hotkey already in use", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        var previousBinding = item.Hotkey;
        item.Hotkey = newBinding;

        var result = ReregisterAllHotkeysAndReport("hotkey-save");
        if (result.FailedSoundIds.Contains(item.Id))
        {
            // Roll back and restore the previously-working hotkey set.
            item.Hotkey = previousBinding;
            ReregisterAllHotkeysAndReport("hotkey-save-rollback");
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

    // ── Stop All hotkey assignment ──────────────────────────────────────────

    private void RefreshStopAllHotkeyDisplay()
    {
        if (StopAllHotkeyText is null)
            return;

        StopAllHotkeyText.Text       = _settings.StopAllHotkey?.DisplayText ?? "No hotkey";
        ClearStopAllHotkeyButton.IsEnabled = _settings.StopAllHotkey is not null;
    }

    private void SetStopAllHotkey_Click(object sender, RoutedEventArgs e)
    {
        if (_hotkeyService is null)
        {
            MessageBox.Show("Hotkeys are not available in this session.", "Hotkeys",
                             MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        var dlg = new HotkeyCaptureDialog(this, "Stop All Sounds", _settings.StopAllHotkey);
        if (dlg.ShowDialog() != true)
            return; // cancelled — nothing changes

        if (dlg.WasCleared)
        {
            ClearStopAllHotkey_Click(sender, e);
            return;
        }

        var newBinding = dlg.ResultBinding;
        if (newBinding is null)
            return;

        var owner = FindHotkeyOwner(newBinding, null);
        if (owner is not null)
        {
            MessageBox.Show(
                $"\"{newBinding.DisplayText}\" is already assigned to \"{owner}\".\n\n" +
                "Choose a different combination.",
                "Hotkey already in use", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        var previous = _settings.StopAllHotkey;
        _settings.StopAllHotkey = newBinding;

        var result = ReregisterAllHotkeysAndReport("stop-all-save");
        if (result.StopAllFailed)
        {
            // Roll back and restore the previously-working hotkey set.
            _settings.StopAllHotkey = previous;
            ReregisterAllHotkeysAndReport("stop-all-save-rollback");
            MessageBox.Show(
                $"Windows could not register \"{newBinding.DisplayText}\".\n\n" +
                "It may already be used by another application or by Windows itself. " +
                "Choose a different combination.",
                "Hotkey unavailable", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        AppSettingsService.Save(_settings);
        RefreshStopAllHotkeyDisplay();
        StatusText.Text = $"Stop All hotkey set: {newBinding.DisplayText}";
    }

    private void ClearStopAllHotkey_Click(object sender, RoutedEventArgs e)
    {
        if (_settings.StopAllHotkey is null)
            return;

        _settings.StopAllHotkey = null;
        ReregisterAllHotkeysAndReport("stop-all-clear");
        AppSettingsService.Save(_settings);
        RefreshStopAllHotkeyDisplay();
        StatusText.Text = "Stop All hotkey cleared";
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

        item.LastPlayedAt = DateTime.UtcNow;
        SoundLibraryService.Save(_library);

        if (CategoryFilter?.SelectedItem as string == "Recent")
            FilterSoundsPanel();

        PlaySound(sound, item.DisplayName, ConvertUiVolumeToGain(item.Volume));
    }

    // Maps a 0–1 UI volume to a 0–1 audio gain using a squared (power-2) curve.
    // Human loudness perception is roughly logarithmic, so a linear gain slider
    // feels "flat" — moving from 100% to 50% barely sounds different.
    // Squaring the value maps the midpoint (0.5) to a gain of 0.25 (−12 dB),
    // which matches what most people expect "half volume" to sound like.
    // The stored SoundItem.Volume (0–1) is still the raw UI value; the curve
    // is applied only at playback so existing JSON files are unaffected.
    private static float ConvertUiVolumeToGain(float uiVolume)
        => uiVolume * uiVolume;

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

    private void StopButton_Click(object sender, RoutedEventArgs e) => StopAllSounds();

    // Stops every active sound effect on both engines. Mic passthrough is a
    // persistent mixer input (added via AddMixerInput, not tracked in the
    // engine's _active list), so it is unaffected and keeps working.
    private void StopAllSounds()
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
            var monitorDevices = AudioDevice.GetAll();
            int monitorIndex = FindDeviceIndex(monitorDevices, _settings.MonitorDeviceName, _settings.MonitorDeviceNumber,
                                                d => d.Name, d => d.Number);
            bool monitorMissing = monitorIndex < 0 && !string.IsNullOrEmpty(_settings.MonitorDeviceName);
            if (monitorIndex < 0)
                monitorIndex = 0;

            MonitorComboBox.SelectionChanged -= MonitorComboBox_SelectionChanged;
            MonitorComboBox.ItemsSource       = monitorDevices;
            MonitorComboBox.SelectedIndex     = monitorIndex;
            MonitorComboBox.SelectionChanged += MonitorComboBox_SelectionChanged;
            CreateMonitorEngine(monitorDevices[monitorIndex].Number);

            var virtualDevices = AudioDevice.GetAllWithNone();
            int virtualIndex = FindDeviceIndex(virtualDevices, _settings.VirtualDeviceName, _settings.VirtualDeviceNumber,
                                                d => d.Name, d => d.Number);
            bool virtualMissing = virtualIndex < 0 && !string.IsNullOrEmpty(_settings.VirtualDeviceName);
            if (virtualIndex < 0)
                virtualIndex = 0;

            VirtualComboBox.SelectionChanged -= VirtualComboBox_SelectionChanged;
            VirtualComboBox.ItemsSource       = virtualDevices;
            VirtualComboBox.SelectedIndex     = virtualIndex;
            VirtualComboBox.SelectionChanged += VirtualComboBox_SelectionChanged;

            var selectedVirtual = virtualDevices[virtualIndex];
            if (!selectedVirtual.IsNone)
                CreateVirtualEngine(selectedVirtual.Number);

            if (monitorMissing)
                StatusText.Text = $"Saved monitor device \"{_settings.MonitorDeviceName}\" not found — using default.";
            else if (virtualMissing)
                StatusText.Text = $"Saved virtual device \"{_settings.VirtualDeviceName}\" not found — using None.";
            else if (!selectedVirtual.IsNone && IsVirtualAudioRouterDevice(selectedVirtual.Name))
                StatusText.Text = $"Virtual: {selectedVirtual.Name} — set as Microphone input in Discord to route audio";
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

        _settings.MonitorDeviceName   = device.Name;
        _settings.MonitorDeviceNumber = device.Number;
        SaveSettings();
    }

    private void VirtualComboBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (VirtualComboBox.SelectedItem is not AudioDevice device)
            return;

        StopMicPassthrough();

        _virtualEngine?.StopAll();
        _virtualEngine?.Dispose();
        _virtualEngine = null;

        _settings.VirtualDeviceName   = device.Name;
        _settings.VirtualDeviceNumber = device.Number;
        SaveSettings();

        if (device.IsNone)
        {
            StatusText.Text = "Virtual output: None";
            return;
        }

        CreateVirtualEngine(device.Number);

        if (_virtualEngine is not null)
        {
            StatusText.Text = IsVirtualAudioRouterDevice(device.Name)
                ? $"Virtual: {device.Name} — set as Microphone input in Discord to route audio"
                : $"Virtual: {device.Name}";
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
            MicComboBox.SelectionChanged -= MicComboBox_SelectionChanged;
            MicComboBox.ItemsSource = mics;

            if (mics.Count > 0)
            {
                int micIndex = FindDeviceIndex(mics, _settings.MicDeviceName, _settings.MicDeviceNumber,
                                                d => d.Name, d => d.Number);
                bool micMissing = micIndex < 0 && !string.IsNullOrEmpty(_settings.MicDeviceName);
                if (micIndex < 0)
                    micIndex = 0;

                MicComboBox.SelectedIndex = micIndex;

                if (micMissing)
                    StatusText.Text = $"Saved microphone \"{_settings.MicDeviceName}\" not found — using default.";
            }
            else
            {
                StatusText.Text = "No microphone devices found.";
            }

            MicComboBox.SelectionChanged += MicComboBox_SelectionChanged;
        }
        catch (Exception ex)
        {
            StatusText.Text = $"Mic list error: {ex.Message}";
        }
    }

    private void MicComboBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (MicComboBox.SelectedItem is MicDevice device)
        {
            _settings.MicDeviceName   = device.Name;
            _settings.MicDeviceNumber = device.Number;
            SaveSettings();
        }

        if (MicPassthroughCheckBox.IsChecked == true)
            RestartMicPassthrough();
    }

    private void MicPassthroughCheckBox_Checked(object sender, RoutedEventArgs e)
    {
        StartMicPassthrough();
        _settings.MicPassthroughEnabled = MicPassthroughCheckBox.IsChecked == true;
        SaveSettings();
    }

    private void MicPassthroughCheckBox_Unchecked(object sender, RoutedEventArgs e)
    {
        StopMicPassthrough();
        _settings.MicPassthroughEnabled = MicPassthroughCheckBox.IsChecked == true;
        SaveSettings();
    }

    private void MicVolumeSlider_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        _micPassthrough?.SetVolume((float)(MicVolumeSlider.Value / 100.0));
        if (MicVolumeLabel is not null)
            MicVolumeLabel.Text = $"{(int)MicVolumeSlider.Value}%";

        _settings.MicVolume = (int)MicVolumeSlider.Value;
        SaveSettings();
    }

    private void StartMicPassthrough()
    {
        if (_virtualEngine is null)
        {
            StatusText.Text = "Virtual Output is None — select a virtual device (e.g. CABLE Input) before enabling mic passthrough.";
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

    // ── Drag and drop ──────────────────────────────────────────────────────────

    private static readonly HashSet<string> _audioExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".mp3", ".wav", ".ogg", ".flac", ".aac"
    };

    private static bool HasAudioFiles(IDataObject data)
    {
        if (!data.GetDataPresent(DataFormats.FileDrop)) return false;
        var files = data.GetData(DataFormats.FileDrop) as string[];
        return files?.Any(f => _audioExtensions.Contains(Path.GetExtension(f))) is true;
    }

    private void SoundsArea_DragEnter(object sender, DragEventArgs e)
    {
        if (HasAudioFiles(e.Data))
        {
            e.Effects = DragDropEffects.Copy;
            SoundsAreaBorder.BorderBrush = (Brush)Application.Current.Resources["SystemAccentColorPrimaryBrush"];
        }
        else
        {
            e.Effects = DragDropEffects.None;
        }
        e.Handled = true;
    }

    private void SoundsArea_DragOver(object sender, DragEventArgs e)
    {
        e.Effects = HasAudioFiles(e.Data) ? DragDropEffects.Copy : DragDropEffects.None;
        e.Handled = true;
    }

    private void SoundsArea_DragLeave(object sender, DragEventArgs e)
    {
        var pos = e.GetPosition(SoundsAreaBorder);
        if (pos.X < 0 || pos.Y < 0 ||
            pos.X > SoundsAreaBorder.ActualWidth ||
            pos.Y > SoundsAreaBorder.ActualHeight)
        {
            SoundsAreaBorder.BorderBrush = (Brush)Application.Current.Resources["CardBorderBrush"];
        }
    }

    private void SoundsArea_Drop(object sender, DragEventArgs e)
    {
        SoundsAreaBorder.BorderBrush = (Brush)Application.Current.Resources["CardBorderBrush"];

        if (!e.Data.GetDataPresent(DataFormats.FileDrop)) return;
        var files = e.Data.GetData(DataFormats.FileDrop) as string[];
        if (files is null) return;

        var audioFiles = files.Where(f => _audioExtensions.Contains(Path.GetExtension(f))).ToList();
        if (audioFiles.Count == 0) return;

        int added  = 0;
        var failed = new List<string>();

        foreach (var filePath in audioFiles)
        {
            try
            {
                var destPath = SoundLibraryService.ImportFile(filePath);
                var sound    = new CachedSound(destPath);
                var item     = new SoundItem
                {
                    DisplayName = Path.GetFileNameWithoutExtension(filePath),
                    FilePath    = destPath,
                    Category    = "General",
                    Volume      = 1.0f,
                    CreatedAt   = DateTime.UtcNow
                };

                _cachedSounds[item.Id] = sound;
                _library.Add(item);
                added++;
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[Drop] Failed to import {filePath}: {ex.Message}");
                failed.Add(Path.GetFileName(filePath));
            }
        }

        if (added > 0)
        {
            SoundLibraryService.Save(_library);
            RefreshCategoryFilter();
            FilterSoundsPanel();
        }

        if (failed.Count > 0 && added > 0)
            StatusText.Text = $"Added {added} sound(s). Failed: {string.Join(", ", failed)}";
        else if (failed.Count > 0)
            StatusText.Text = $"Could not import: {string.Join(", ", failed)}";
        else if (added == 1)
            StatusText.Text = $"Added: {Path.GetFileNameWithoutExtension(audioFiles[0])}";
        else
            StatusText.Text = $"Added {added} sound(s)";
    }

    // ── Window closing ────────────────────────────────────────────────────────

    private void Window_Closing(object sender, System.ComponentModel.CancelEventArgs e)
    {
        // If CloseToTray is on and this isn't a deliberate exit (from the tray
        // menu or ExitApp()), cancel the close and just hide to tray instead.
        if (!_isExiting && _settings.CloseToTray)
        {
            e.Cancel = true;
            Hide();
            return;
        }

        // Only persist bounds while Normal; maximized/minimized dimensions
        // aren't meaningful as a restored size for next launch.
        if (WindowState == WindowState.Normal)
        {
            _settings.WindowLeft   = Left;
            _settings.WindowTop    = Top;
            _settings.WindowWidth  = Width;
            _settings.WindowHeight = Height;
        }
        SaveSettings();

        _trayIcon?.Dispose();
        _trayIcon = null;
        _hotkeyService?.UnregisterAll();
        _hotkeys?.Dispose();
        _micPassthrough?.Dispose();
        _monitorEngine?.Dispose();
        _virtualEngine?.Dispose();
    }

}
