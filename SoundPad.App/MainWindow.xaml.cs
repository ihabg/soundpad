using Microsoft.Win32;
using NAudio.Wave.SampleProviders;
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

    // ── Sound library / decks ─────────────────────────────────────────────────
    // _decks holds all decks; _activeDeck is the currently visible one.
    // _library is a computed shorthand for _activeDeck.Sounds so the rest of
    // the code continues to work without a mass rename.
    // _cachedSounds maps each SoundItem's Guid to its preloaded float samples.
    private List<Deck>                             _decks        = new();
    private Deck                                   _activeDeck   = null!;
    private List<SoundItem>                        _library      => _activeDeck.Sounds;
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

    // Tracks an in-progress test tone so it can be stopped by Stop All or replaced
    // when the user clicks Test Discord Output again. Null when no tone is playing.
    private OffsetSampleProvider? _virtualTestToneProvider;

    // URL of the latest GitHub release, populated when an update check returns UpdateAvailable.
    private string? _lastReleaseUrl;

    // In-app updater state — populated when an update with a downloadable asset is found.
    private string?                  _lastUpdateTag;
    private string?                  _installerAssetUrl;
    private string?                  _installerAssetName;
    private CancellationTokenSource? _downloadCts;
    private bool                     _isDownloading;

    // ── Active playback tracking ──────────────────────────────────────────────
    private sealed record RowControls(Action<bool> SetActive);

    private sealed class ActivePlayback
    {
        public Guid            SoundId       { get; init; }
        public PlaybackHandle? MonitorHandle { get; init; }
        public PlaybackHandle? VirtualHandle { get; init; }

        public bool IsFinished =>
            (MonitorHandle is null || MonitorHandle.IsFinished) &&
            (VirtualHandle is null || VirtualHandle.IsFinished);
    }

    private readonly Dictionary<Guid, ActivePlayback> _activePlaybacks = new();
    private readonly Dictionary<Guid, RowControls>    _rowControls     = new();
    private DispatcherTimer?                           _playbackMonitor;

    // ── Drag reorder state ────────────────────────────────────────────────────
    private const  string  InternalReorderFormat = "SoundPadInternalReorder";
    private        Point   _dragStartPoint;
    private        bool    _dragReady;
    private        Guid    _dragSourceId   = Guid.Empty; // Sound being dragged
    private        Guid    _pendingPlayId  = Guid.Empty; // Pad card press pending play/stop
    private        Border? _dropIndicator;
    private        int     _dropIndicatorIndex = -1;     // -1 = not in any panel

    // ── Mini Mode ─────────────────────────────────────────────────────────────
    // Events allow MiniWindow to react to playback and deck changes without
    // MainWindow needing any direct reference to the Mini window's internals.
    internal event Action<Guid, bool>? PlaybackStateChanged;
    internal event Action<Deck>?       ActiveDeckChanged;
    private  MiniWindow?               _miniWindow;

    // ── Constructor ────────────────────────────────────────────────────────────
    // Settings are loaded before InitializeComponent so the saved window
    // bounds can be applied before the window is ever shown (no visible jump).
    public MainWindow()
    {
        StartupLogger.Log("Constructor begin");

        try
        {
            _settings = AppSettingsService.Load();
            StartupLogger.Log("Settings loaded");
        }
        catch (Exception ex)
        {
            StartupLogger.Log($"Settings load FAILED ({ex.GetType().Name}): {ex.Message} — using defaults");
            _settings = new AppSettings();
        }

        try
        {
            StartupLogger.Log("InitializeComponent begin");
            InitializeComponent();
            StartupLogger.Log("InitializeComponent done");
        }
        catch (Exception ex)
        {
            StartupLogger.Log($"InitializeComponent FAILED ({ex.GetType().Name}): {ex.Message}");
            StartupLogger.Log($"  Stack: {ex.StackTrace}");
            throw; // propagate so App_Startup shows an error dialog
        }

        RestoreWindowBounds();
        StartupLogger.Log("Constructor end");
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
        InterruptSoundsSwitch.Checked   -= InterruptSoundsSwitch_Checked;
        InterruptSoundsSwitch.Unchecked -= InterruptSoundsSwitch_Unchecked;
        InterruptSoundsSwitch.IsChecked  = _settings.InterruptPreviousSounds;
        InterruptSoundsSwitch.Checked   += InterruptSoundsSwitch_Checked;
        InterruptSoundsSwitch.Unchecked += InterruptSoundsSwitch_Unchecked;

        AutoUpdateSwitch.Checked   -= AutoUpdateSwitch_Checked;
        AutoUpdateSwitch.Unchecked -= AutoUpdateSwitch_Unchecked;
        AutoUpdateSwitch.IsChecked  = _settings.EnableAutoUpdateChecks;
        AutoUpdateSwitch.Checked   += AutoUpdateSwitch_Checked;
        AutoUpdateSwitch.Unchecked += AutoUpdateSwitch_Unchecked;

        MinimizeToTraySwitch.Checked   -= MinimizeToTraySwitch_Checked;
        MinimizeToTraySwitch.Unchecked -= MinimizeToTraySwitch_Unchecked;
        MinimizeToTraySwitch.IsChecked  = _settings.MinimizeToTray;
        MinimizeToTraySwitch.Checked   += MinimizeToTraySwitch_Checked;
        MinimizeToTraySwitch.Unchecked += MinimizeToTraySwitch_Unchecked;

        CloseToTraySwitch.Checked   -= CloseToTraySwitch_Checked;
        CloseToTraySwitch.Unchecked -= CloseToTraySwitch_Unchecked;
        CloseToTraySwitch.IsChecked  = _settings.CloseToTray;
        CloseToTraySwitch.Checked   += CloseToTraySwitch_Checked;
        CloseToTraySwitch.Unchecked += CloseToTraySwitch_Unchecked;

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

    private void InterruptSoundsSwitch_Checked(object sender, RoutedEventArgs e)
    {
        _settings.InterruptPreviousSounds = true;
        SaveSettings();
    }

    private void InterruptSoundsSwitch_Unchecked(object sender, RoutedEventArgs e)
    {
        _settings.InterruptPreviousSounds = false;
        SaveSettings();
    }

    private void AutoUpdateSwitch_Checked(object sender, RoutedEventArgs e)
    {
        _settings.EnableAutoUpdateChecks = true;
        SaveSettings();
    }

    private void AutoUpdateSwitch_Unchecked(object sender, RoutedEventArgs e)
    {
        _settings.EnableAutoUpdateChecks = false;
        SaveSettings();
    }

    private async void CheckUpdates_Click(object sender, RoutedEventArgs e)
    {
        CheckUpdatesButton.IsEnabled = false;
        StatusText.Text = "Checking for updates...";

        var result = await UpdateCheckService.CheckAsync(GetAppVersion());

        switch (result.Status)
        {
            case UpdateStatus.UpdateAvailable:
                _lastReleaseUrl = result.ReleaseUrl;
                StatusText.Text = $"Update available: {result.LatestTag}";
                ShowUpdateAvailableUI(result);
                break;
            case UpdateStatus.UpToDate:
                StatusText.Text = "SoundPad is up to date.";
                HideUpdateAvailablePanel();
                break;
            default:
                StatusText.Text = "Could not check for updates. Check your internet connection.";
                break;
        }

        if (!_isDownloading)
            CheckUpdatesButton.IsEnabled = true;
    }

    private void OpenReleases_Click(object sender, RoutedEventArgs e)
    {
        var url = _lastReleaseUrl ?? "https://github.com/ihabg/soundpad/releases";
        try
        {
            Process.Start(new ProcessStartInfo(url) { UseShellExecute = true });
        }
        catch { }
    }

    private async Task RunAutoUpdateCheckAsync()
    {
        StartupLogger.Log("Auto update check begin");
        try
        {
            var result = await UpdateCheckService.CheckAsync(GetAppVersion());

            // Continuation runs back on the UI thread (SynchronizationContext captured at call site).
            _settings.LastUpdateCheckUtc = DateTime.UtcNow;
            SaveSettings();

            StartupLogger.Log($"Auto update check result: {result.Status}");

            if (result.Status == UpdateStatus.UpdateAvailable)
            {
                _lastReleaseUrl = result.ReleaseUrl;
                StatusText.Text = $"Update available: {result.LatestTag}";
                ShowUpdateAvailableUI(result);
            }
        }
        catch (Exception ex)
        {
            StartupLogger.Log($"Auto update check unexpected error: {ex.Message}");
        }
    }

    private void ShowUpdateAvailableUI(UpdateCheckResult result)
    {
        _lastUpdateTag       = result.LatestTag;
        _installerAssetUrl   = result.InstallerAssetUrl;
        _installerAssetName  = result.InstallerAssetName;

        UpdateVersionText.Text = $"Version {result.LatestTag} is available";

        var title = result.ReleaseName;
        if (!string.IsNullOrWhiteSpace(title) &&
            !string.Equals(title, result.LatestTag, StringComparison.OrdinalIgnoreCase))
        {
            UpdateTitleText.Text       = title;
            UpdateTitleText.Visibility = Visibility.Visible;
        }
        else
        {
            UpdateTitleText.Visibility = Visibility.Collapsed;
        }

        var notes = TruncateNotes(result.ReleaseBody, 400);
        if (!string.IsNullOrWhiteSpace(notes))
        {
            UpdateBodyText.Text         = notes;
            UpdateBodyScroll.Visibility = Visibility.Visible;
        }
        else
        {
            UpdateBodyScroll.Visibility = Visibility.Collapsed;
        }

        if (!string.IsNullOrEmpty(_installerAssetUrl))
        {
            DownloadInstallButton.Visibility = Visibility.Visible;
            NoAssetText.Visibility           = Visibility.Collapsed;
        }
        else
        {
            DownloadInstallButton.Visibility = Visibility.Collapsed;
            NoAssetText.Visibility           = Visibility.Visible;
        }

        if (!_isDownloading)
            DownloadProgressPanel.Visibility = Visibility.Collapsed;
        UpdateAvailablePanel.Visibility = Visibility.Visible;
    }

    private void HideUpdateAvailablePanel()
    {
        _downloadCts?.Cancel();
        UpdateAvailablePanel.Visibility = Visibility.Collapsed;
    }

    private void LaterUpdate_Click(object sender, RoutedEventArgs e) => HideUpdateAvailablePanel();

    private async void DownloadInstall_Click(object sender, RoutedEventArgs e)
    {
        if (string.IsNullOrEmpty(_installerAssetUrl))
            return;

        var fileName = !string.IsNullOrEmpty(_installerAssetName)
            ? _installerAssetName
            : $"SoundPad-Setup-{_lastUpdateTag?.TrimStart('v') ?? "latest"}.exe";

        _downloadCts?.Dispose();
        _downloadCts   = new CancellationTokenSource();
        _isDownloading = true;

        CheckUpdatesButton.IsEnabled     = false;
        DownloadInstallButton.IsEnabled  = false;
        LaterButton.IsEnabled            = false;
        DownloadProgressPanel.Visibility = Visibility.Visible;
        DownloadStatusText.Text          = "Starting download…";
        DownloadProgressBar.Value        = 0;
        DownloadProgressBar.IsIndeterminate = false;

        var progress = new Progress<(long Downloaded, long Total)>(UpdateDownloadProgress);

        string localPath;
        try
        {
            localPath = await UpdateDownloadService.DownloadAsync(
                _installerAssetUrl,
                fileName,
                progress,
                _downloadCts.Token);
        }
        catch (OperationCanceledException)
        {
            _isDownloading = false;
            _downloadCts?.Dispose();
            _downloadCts = null;
            DownloadProgressPanel.Visibility = Visibility.Collapsed;
            DownloadInstallButton.IsEnabled  = true;
            LaterButton.IsEnabled            = true;
            CheckUpdatesButton.IsEnabled     = true;
            DownloadStatusText.Text          = "";
            return;
        }
        catch (Exception ex)
        {
            _isDownloading = false;
            _downloadCts?.Dispose();
            _downloadCts = null;
            DownloadProgressPanel.Visibility = Visibility.Collapsed;
            DownloadInstallButton.IsEnabled  = true;
            LaterButton.IsEnabled            = true;
            CheckUpdatesButton.IsEnabled     = true;
            MessageBox.Show(
                $"Download failed: {ex.Message}\n\nUse Open Release Page to download manually.",
                "Download Failed",
                MessageBoxButton.OK,
                MessageBoxImage.Warning);
            return;
        }

        DownloadStatusText.Text = "Download complete. Ready to install.";

        var confirm = MessageBox.Show(
            $"SoundPad will close and the installer will launch.\n\nInstall {_lastUpdateTag} now?",
            "Install Update",
            MessageBoxButton.YesNo,
            MessageBoxImage.Question);

        if (confirm != MessageBoxResult.Yes)
        {
            _isDownloading = false;
            _downloadCts?.Dispose();
            _downloadCts = null;
            DownloadProgressPanel.Visibility = Visibility.Collapsed;
            DownloadInstallButton.IsEnabled  = true;
            LaterButton.IsEnabled            = true;
            CheckUpdatesButton.IsEnabled     = true;
            return;
        }

        LaunchInstallerAndExit(localPath);
        // Only reached when Process.Start failed — ExitApp() closes the app on success.
        _isDownloading = false;
        _downloadCts?.Dispose();
        _downloadCts = null;
        DownloadProgressPanel.Visibility = Visibility.Collapsed;
        DownloadInstallButton.IsEnabled  = true;
        LaterButton.IsEnabled            = true;
        CheckUpdatesButton.IsEnabled     = true;
    }

    private void CancelDownload_Click(object sender, RoutedEventArgs e) => _downloadCts?.Cancel();

    private void UpdateDownloadProgress((long Downloaded, long Total) t)
    {
        if (t.Total > 0)
        {
            DownloadProgressBar.IsIndeterminate = false;
            DownloadProgressBar.Value = (double)t.Downloaded / t.Total * 100.0;
            DownloadStatusText.Text   = $"Downloading… {FormatBytes(t.Downloaded)} / {FormatBytes(t.Total)}";
        }
        else
        {
            DownloadProgressBar.IsIndeterminate = true;
            DownloadStatusText.Text             = $"Downloading… {FormatBytes(t.Downloaded)}";
        }
    }

    private void LaunchInstallerAndExit(string path)
    {
        try
        {
            Process.Start(new ProcessStartInfo(path) { UseShellExecute = true });
        }
        catch (Exception ex)
        {
            MessageBox.Show(
                $"Could not launch installer: {ex.Message}\n\nThe file is at:\n{path}",
                "Launch Failed",
                MessageBoxButton.OK,
                MessageBoxImage.Error);
            return;
        }
        ExitApp();
    }

    private static string FormatBytes(long bytes)
    {
        if (bytes >= 1_048_576)
            return $"{bytes / 1_048_576.0:F1} MB";
        if (bytes >= 1_024)
            return $"{bytes / 1_024.0:F0} KB";
        return $"{bytes} B";
    }

    private static string? TruncateNotes(string? body, int maxLen)
    {
        if (string.IsNullOrWhiteSpace(body)) return null;
        body = body.Trim();
        return body.Length <= maxLen ? body : body[..maxLen] + "…";
    }

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
        StartupLogger.Log("OnSourceInitialized begin");
        base.OnSourceInitialized(e);
        try
        {
            var helper = new System.Windows.Interop.WindowInteropHelper(this);
            var hwnd   = helper.Handle;
            StartupLogger.Log($"HWND = 0x{hwnd:X16} {(hwnd == IntPtr.Zero ? "— ZERO, hotkeys will fail" : "OK")}");

            var src = System.Windows.Interop.HwndSource.FromHwnd(hwnd);
            StartupLogger.Log($"HwndSource = {(src is null ? "NULL — hotkeys will fail" : "OK")}");

            _hotkeys = new HotkeyManager(this);
            StartupLogger.Log("HotkeyManager created");

            _hotkeyService = new HotkeyService(_hotkeys);
            _hotkeyService.HotkeyTriggered        += OnSoundHotkeyTriggered;
            _hotkeyService.StopAllHotkeyTriggered += OnStopAllHotkeyTriggered;
            StartupLogger.Log("HotkeyService ready");
        }
        catch (Exception ex)
        {
            _hotkeys       = null;
            _hotkeyService = null;
            StartupLogger.Log($"Hotkey init FAILED ({ex.GetType().Name}): {ex.Message}");
            StartupLogger.Log($"  Stack: {ex.StackTrace}");
        }
        StartupLogger.Log("OnSourceInitialized end");
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
        StartupLogger.Log("MainWindow_Loaded begin");
        try
        {
            StartupLogger.Log("LoadLibrary begin");
            LoadLibrary();
            StartupLogger.Log("LoadLibrary done");

            // Attach panel-level drag handlers once (panels are XAML singletons).
            SoundsPanel.PreviewMouseMove += SoundsPanel_PreviewMouseMove;
            GridPanel.PreviewMouseMove   += GridPanel_PreviewMouseMove;

            StartupLogger.Log("PopulateDeviceLists begin");
            PopulateDeviceLists();
            StartupLogger.Log("PopulateDeviceLists done");

            StartupLogger.Log("PopulateMicList begin");
            PopulateMicList();
            StartupLogger.Log("PopulateMicList done");

            StartupLogger.Log("RestoreMicPassthroughState begin");
            RestoreMicPassthroughState();
            StartupLogger.Log("RestoreSelectedTab begin");
            RestoreSelectedTab();
            StartupLogger.Log("RestoreBehaviorSettings begin");
            RestoreBehaviorSettings();
            RestoreAudioPerformancePreset();
            StartupLogger.Log("SyncStartupRegistryPath begin");
            SyncStartupRegistryPath();
            StartupLogger.Log("InitializeTrayIcon begin");
            InitializeTrayIcon();
            StartupLogger.Log("InitializeTrayIcon done");
            AboutVersionText.Text    = $"Version {GetAppVersion()}";
            AboutDataFolderText.Text = AppPaths.AppDataDir;

            StartupLogger.Log("MainWindow_Loaded end — queuing deferred hotkey registration");
            Dispatcher.BeginInvoke(DispatcherPriority.ContextIdle, new Action(() =>
            {
                StartupLogger.Log("Deferred hotkey registration begin");
                try
                {
                    ReregisterAllHotkeysAndReport("startup");
                    StartupLogger.Log("Deferred hotkey registration done");
                }
                catch (Exception ex)
                {
                    StartupLogger.Log($"Deferred hotkey registration FAILED ({ex.GetType().Name}): {ex.Message}");
                    StartupLogger.Log($"  Stack: {ex.StackTrace}");
                    try { StatusText.Text = $"Hotkey startup error: {ex.Message}"; } catch { }
                }

                if (_settings.MiniOpenOnStartup)
                    OpenMiniMode();
            }));

            // Auto update check: only when enabled, and only once per 24 hours.
            // Runs at ApplicationIdle so it never competes with startup rendering.
            if (_settings.EnableAutoUpdateChecks)
            {
                var lastCheck = _settings.LastUpdateCheckUtc;
                if (lastCheck is null || (DateTime.UtcNow - lastCheck.Value).TotalHours >= 24)
                {
                    Dispatcher.BeginInvoke(DispatcherPriority.ApplicationIdle,
                        new Action(() => _ = RunAutoUpdateCheckAsync()));
                }
            }
        }
        catch (Exception ex)
        {
            StartupLogger.Log($"MainWindow_Loaded FAILED ({ex.GetType().Name}): {ex.Message}");
            StartupLogger.Log($"  Stack: {ex.StackTrace}");
            try { StatusText.Text = $"Startup error: {ex.Message}"; } catch { }
        }
        StartupLogger.Log("MainWindow_Loaded handler returning");
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

    // Loads decks from decks.json (or migrates sounds.json on first run),
    // sets the active deck, preloads audio, then renders the library UI.
    private void LoadLibrary()
    {
        _decks      = DeckService.Load(_settings);
        _activeDeck = _decks.Find(d => d.Id == _settings.ActiveDeckId) ?? _decks[0];

        // On first launch the active deck has no sounds — seed from bundled samples.
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
            DeckService.Save(_decks);

        RebuildDeckBar();
        RefreshCategoryFilter();
        FilterSoundsPanel();
        ApplyLibraryViewButtons();
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
            DeckService.Save(_decks);
    }

    // ── Deck management ───────────────────────────────────────────────────────

    private void RebuildDeckBar()
    {
        DeckChipsPanel.Children.Clear();

        foreach (var deck in _decks)
        {
            var capturedDeck = deck;
            var chip = new UiButton
            {
                Content    = deck.Name,
                Appearance = deck.Id == _activeDeck.Id
                    ? ControlAppearance.Primary
                    : ControlAppearance.Secondary,
                Margin  = new Thickness(0, 0, 4, 0),
                Padding = new Thickness(14, 4, 14, 4)
            };
            chip.Click += (_, _) => SwitchToDeck(capturedDeck);

            var renameItem = new MenuItem { Header = "Rename…" };
            renameItem.Click += (_, _) => RenameDeck(capturedDeck);

            var duplicateItem = new MenuItem { Header = "Duplicate" };
            duplicateItem.Click += (_, _) => DuplicateDeck(capturedDeck);

            var deleteItem = new MenuItem
            {
                Header     = "Delete",
                Foreground = (Brush)Application.Current.Resources["SystemFillColorCriticalBrush"]
            };
            deleteItem.Click += (_, _) => DeleteDeck(capturedDeck);

            var ctx = new ContextMenu();
            ctx.Items.Add(renameItem);
            ctx.Items.Add(duplicateItem);
            ctx.Items.Add(new Separator());
            ctx.Items.Add(deleteItem);
            chip.ContextMenu = ctx;

            DeckChipsPanel.Children.Add(chip);
        }
    }

    private void SwitchToDeck(Deck deck)
    {
        if (deck.Id == _activeDeck.Id) return;

        StopAllSounds();
        _cachedSounds.Clear();
        _rowControls.Clear();

        _activeDeck            = deck;
        _settings.ActiveDeckId = deck.Id;
        AppSettingsService.Save(_settings);

        foreach (var item in _library.ToList())
        {
            if (!File.Exists(item.FilePath)) continue;
            try   { _cachedSounds[item.Id] = new CachedSound(item.FilePath); }
            catch (Exception ex) { Debug.WriteLine($"[Deck] Could not load '{item.DisplayName}': {ex.Message}"); }
        }

        ReregisterAllHotkeysAndReport("deck-switch");
        RebuildDeckBar();
        RefreshCategoryFilter();
        FilterSoundsPanel();
        StatusText.Text = $"Switched to: {deck.Name}";
        ActiveDeckChanged?.Invoke(_activeDeck);
    }

    private void AddDeck_Click(object sender, RoutedEventArgs e)
    {
        var existingNames = _decks.Select(d => d.Name).ToList();
        var dlg           = new Dialogs.DeckNameDialog(this, "", existingNames);
        if (dlg.ShowDialog() != true) return;

        var newDeck = new Deck { Name = dlg.ResultName };
        _decks.Add(newDeck);
        DeckService.Save(_decks);
        SwitchToDeck(newDeck);
    }

    private void RenameDeck(Deck deck)
    {
        var existingNames = _decks.Where(d => d.Id != deck.Id).Select(d => d.Name).ToList();
        var dlg           = new Dialogs.DeckNameDialog(this, deck.Name, existingNames);
        if (dlg.ShowDialog() != true) return;

        deck.Name = dlg.ResultName;
        DeckService.Save(_decks);
        RebuildDeckBar();
        StatusText.Text = $"Deck renamed to: {deck.Name}";
        if (deck.Id == _activeDeck.Id)
            ActiveDeckChanged?.Invoke(_activeDeck);
    }

    private void DuplicateDeck(Deck source)
    {
        var existingNames = _decks.Select(d => d.Name).ToList();
        var finalName     = source.Name + " (copy)";
        int n = 2;
        while (existingNames.Any(x => string.Equals(x, finalName, StringComparison.OrdinalIgnoreCase)))
            finalName = $"{source.Name} (copy {n++})";

        var copy = new Deck
        {
            Name             = finalName,
            CustomCategories = source.CustomCategories.ToList(),
            Sounds           = source.Sounds.Select(s => new SoundItem
            {
                Id                = Guid.NewGuid(), // new ID per sound
                DisplayName       = s.DisplayName,
                FilePath          = s.FilePath,
                Category          = s.Category,
                Volume            = s.Volume,
                Hotkey            = s.Hotkey,
                HotkeyInitialized = s.HotkeyInitialized,
                IsFavorite        = s.IsFavorite,
                LastPlayedAt      = s.LastPlayedAt,
                TrimStartSeconds  = s.TrimStartSeconds,
                TrimEndSeconds    = s.TrimEndSeconds,
                FadeInSeconds     = s.FadeInSeconds,
                FadeOutSeconds    = s.FadeOutSeconds,
                PadColor          = s.PadColor,
                CreatedAt         = DateTime.UtcNow
            }).ToList()
        };

        _decks.Add(copy);
        DeckService.Save(_decks);
        SwitchToDeck(copy);
    }

    private void DeleteDeck(Deck deck)
    {
        if (_decks.Count <= 1)
        {
            MessageBox.Show("You cannot delete the last deck.", "Delete Deck",
                MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        var confirm = MessageBox.Show(
            $"Delete deck \"{deck.Name}\"?\n\nThis removes the deck from SoundPad. Audio files on disk are not deleted.",
            "Delete Deck", MessageBoxButton.YesNo, MessageBoxImage.Warning);
        if (confirm != MessageBoxResult.Yes) return;

        // If deleting the active deck, switch to another first.
        if (deck.Id == _activeDeck.Id)
            SwitchToDeck(_decks.First(d => d.Id != deck.Id));

        _decks.Remove(deck);
        DeckService.Save(_decks);
        RebuildDeckBar();
        StatusText.Text = $"Deleted deck: {deck.Name}";
    }

    // ── Filter / rebuild ──────────────────────────────────────────────────────

    // Rebuilds SoundsPanel showing only sounds that match the current search
    // text and selected category.  Called after any library or filter change.
    private void FilterSoundsPanel()
    {
        // Cancel any in-progress drag; panels are about to be cleared.
        _dragReady    = false;
        _dragSourceId = Guid.Empty;
        _pendingPlayId = Guid.Empty;
        RemoveDropIndicator();

        var search   = SearchBox?.Text.Trim() ?? "";
        var category = CategoryFilter?.SelectedItem as string ?? "All";

        IEnumerable<SoundItem> source = category == "Recent"
            ? _library.OrderByDescending(x => x.LastPlayedAt ?? DateTime.MinValue)
            : _library;

        _rowControls.Clear();
        SoundsPanel.Children.Clear();
        GridPanel.Children.Clear();

        bool isGrid = _settings.LibraryView == "Grid";

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

            if (isGrid)
                GridPanel.Children.Add(BuildPadCard(item));
            else
                SoundsPanel.Children.Add(BuildSoundRow(item));
        }

        foreach (var id in _activePlaybacks.Keys)
            UpdateRowState(id, active: true);

        int visibleCount = isGrid ? GridPanel.Children.Count : SoundsPanel.Children.Count;
        bool panelEmpty  = visibleCount == 0;
        EmptyLibraryText.Visibility = panelEmpty ? Visibility.Visible : Visibility.Collapsed;
        EmptyLibraryText.Text = _library.Count == 0
            ? "No sounds yet — click \"+ Add Sound\" to import an audio file."
            : category == "Favorites"
                ? "No favorites yet — click the star on a sound to mark it as a favorite."
                : category == "Recent"
                    ? "No sounds played in the last 7 days."
                    : "No sounds match your search or filter.";

        if (LibraryCountText is not null)
            LibraryCountText.Content = $"{visibleCount}";
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

        var virtualNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
            { "All", "Favorites", "Recent" };

        foreach (var cat in _library
            .Select(x => string.IsNullOrWhiteSpace(x.Category) ? "General" : x.Category)
            .Concat(_activeDeck.CustomCategories.Where(c => !virtualNames.Contains(c)))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(c => c, StringComparer.OrdinalIgnoreCase))
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

        // ── Col 0: Play / Stop toggle ────────────────────────────────────
        var playStopBtn = new UiButton
        {
            Appearance          = ControlAppearance.Transparent,
            Padding             = new Thickness(8),
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment   = VerticalAlignment.Center,
            Icon                = new UiSymbolIcon { Symbol = SymbolRegular.Play20 }
        };
        playStopBtn.Click += (_, _) =>
        {
            if (_activePlaybacks.ContainsKey(capturedItem.Id))
                StopSoundById(capturedItem.Id);
            else
                PlayLibraryItem(capturedItem);
        };
        Grid.SetColumn(playStopBtn, 0);
        grid.Children.Add(playStopBtn);

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
            DeckService.Save(_decks);
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
            DeckService.Save(_decks);
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
        // When PadColor is set: a 4px full-height stripe is docked to the left.
        // Row left padding is zeroed so the stripe touches the row edge;
        // the grid compensates with its own top/bottom margin.
        UIElement rowContent = grid;
        var rowPadding       = new Thickness(14, 8, 14, 8);
        if (item.PadColor is not null)
        {
            try
            {
                var stripeColor = (Color)ColorConverter.ConvertFromString(item.PadColor);
                var stripe      = new Border
                {
                    Width      = 4,
                    Background = new SolidColorBrush(stripeColor),
                    Margin     = new Thickness(0, 0, 10, 0)
                };
                grid.Margin = new Thickness(0, 8, 0, 8);
                var dp      = new DockPanel { LastChildFill = true };
                DockPanel.SetDock(stripe, Dock.Left);
                dp.Children.Add(stripe);
                dp.Children.Add(grid);
                rowContent = dp;
                rowPadding = new Thickness(0, 0, 14, 0);
            }
            catch { }
        }

        var row = new Border
        {
            Padding         = rowPadding,
            Background      = Brushes.Transparent,
            BorderBrush     = (Brush)Application.Current.Resources["CardBorderBrush"],
            BorderThickness = new Thickness(0, 0, 0, 1),
            Child           = rowContent
        };
        var hoverBg = (Brush)Application.Current.Resources["ControlFillColorDefaultBrush"];
        row.MouseEnter += (_, _) => { if (!_activePlaybacks.ContainsKey(capturedItem.Id)) row.Background = hoverBg; };
        row.MouseLeave += (_, _) => { if (!_activePlaybacks.ContainsKey(capturedItem.Id)) row.Background = Brushes.Transparent; };

        // Record drag-start info; skip when the press originates on a button or slider
        // so interactive controls (play, edit, hotkey, volume) still work normally.
        row.PreviewMouseLeftButtonDown += (_, e) =>
        {
            _dragReady = false;
            var src = e.OriginalSource as DependencyObject;
            while (src is not null && src != row)
            {
                if (src is System.Windows.Controls.Primitives.ButtonBase || src is Slider)
                    return;
                src = VisualTreeHelper.GetParent(src);
            }
            _dragSourceId   = capturedItem.Id;
            _dragStartPoint = e.GetPosition(SoundsPanel);
            _dragReady      = true;
        };

        row.MouseLeftButtonDown += (_, e) =>
        {
            if (e.ClickCount == 2)
                PlayLibraryItem(capturedItem);
        };

        // ── SetActive callback ────────────────────────────────────────────────
        Action<bool> setActive = active =>
        {
            if (active)
            {
                var accent = (Application.Current.Resources["SystemAccentColorPrimaryBrush"] as SolidColorBrush)?.Color ?? _fallbackAccent;
                row.Background = new SolidColorBrush(accent) { Opacity = 0.15 };
            }
            else
            {
                row.Background = Brushes.Transparent;
            }
            playStopBtn.Icon = new UiSymbolIcon { Symbol = active ? SymbolRegular.Stop20 : SymbolRegular.Play20 };
        };
        _rowControls[item.Id] = new RowControls(setActive);

        // ── Context menu ──────────────────────────────────────────────────
        row.ContextMenu = BuildSoundContextMenu(capturedItem);

        return row;
    }

    // ── Grid / Pad View ───────────────────────────────────────────────────────

    private void ListViewButton_Click(object sender, RoutedEventArgs e) => SetLibraryView("List");
    private void GridViewButton_Click(object sender, RoutedEventArgs e) => SetLibraryView("Grid");

    private void GridSizeCombo_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        var selected = (GridSizeCombo.SelectedItem as ComboBoxItem)?.Content as string ?? "Medium";
        if (_settings.GridPadSize == selected) return;
        _settings.GridPadSize = selected;
        SaveSettings();
        FilterSoundsPanel();
    }

    private void GridCompactButton_Click(object sender, RoutedEventArgs e)
    {
        _settings.GridCompact        = !_settings.GridCompact;
        GridCompactButton.Appearance = _settings.GridCompact
            ? ControlAppearance.Primary
            : ControlAppearance.Secondary;
        SaveSettings();
        FilterSoundsPanel();
    }

    private void SetLibraryView(string view)
    {
        if (_settings.LibraryView == view) return;
        _settings.LibraryView = view;
        SaveSettings();
        ApplyLibraryViewButtons();
        FilterSoundsPanel();
    }

    private void ApplyLibraryViewButtons()
    {
        bool isGrid       = _settings.LibraryView == "Grid";
        var  gridVis      = isGrid ? Visibility.Visible : Visibility.Collapsed;

        if (ListViewButton is not null)
            ListViewButton.Appearance = isGrid ? ControlAppearance.Secondary : ControlAppearance.Primary;
        if (GridViewButton is not null)
            GridViewButton.Appearance = isGrid ? ControlAppearance.Primary : ControlAppearance.Secondary;
        if (ColumnHeaderBorder is not null)
            ColumnHeaderBorder.Visibility = isGrid ? Visibility.Collapsed : Visibility.Visible;
        if (SoundsAreaBorder is not null)
        {
            SoundsAreaBorder.CornerRadius    = isGrid ? new CornerRadius(6) : new CornerRadius(0, 0, 6, 6);
            SoundsAreaBorder.BorderThickness = isGrid ? new Thickness(1) : new Thickness(1, 0, 1, 1);
        }
        if (GridSizeCombo is not null)
        {
            GridSizeCombo.Visibility = gridVis;
            if (isGrid) SyncGridSizeCombo();
        }
        if (GridCompactButton is not null)
        {
            GridCompactButton.Visibility = gridVis;
            GridCompactButton.Appearance = _settings.GridCompact
                ? ControlAppearance.Primary
                : ControlAppearance.Secondary;
        }
    }

    private void SyncGridSizeCombo()
    {
        GridSizeCombo.SelectionChanged -= GridSizeCombo_SelectionChanged;
        GridSizeCombo.SelectedIndex     = _settings.GridPadSize switch
        {
            "Small" => 0,
            "Large" => 2,
            _       => 1,
        };
        GridSizeCombo.SelectionChanged += GridSizeCombo_SelectionChanged;
    }

    internal static Brush GetPadBackground(string? padColor)
    {
        if (padColor is not null)
        {
            try
            {
                var color = (Color)ColorConverter.ConvertFromString(padColor);
                return new SolidColorBrush(color) { Opacity = 0.75 };
            }
            catch { }
        }
        return (Brush)Application.Current.Resources["CardBackgroundFillColorDefaultBrush"];
    }

    private (int Width, int Height) GetPadDimensions() => _settings.GridPadSize switch
    {
        "Small" => (120, 100),
        "Large" => (210, 170),
        _       => (160, 130),
    };

    // ── Drag reorder: panel-level mouse-move handlers ─────────────────────────
    // Attached once in MainWindow_Loaded; fire for any mouse move within the panel.

    private void SoundsPanel_PreviewMouseMove(object sender, MouseEventArgs e)
    {
        if (!_dragReady || e.LeftButton != MouseButtonState.Pressed || _dragSourceId == Guid.Empty) return;
        var pos  = Mouse.GetPosition(SoundsPanel);
        var diff = pos - _dragStartPoint;
        if (Math.Abs(diff.X) < SystemParameters.MinimumHorizontalDragDistance &&
            Math.Abs(diff.Y) < SystemParameters.MinimumVerticalDragDistance)
            return;

        _dragReady    = false;
        var dragId    = _dragSourceId;
        _dragSourceId = Guid.Empty;

        if (!CanReorder(out var blocked)) { StatusText.Text = blocked; return; }

        DragDrop.DoDragDrop(SoundsPanel, new DataObject(InternalReorderFormat, dragId.ToString()), DragDropEffects.Move);
        RemoveDropIndicator();
    }

    private void GridPanel_PreviewMouseMove(object sender, MouseEventArgs e)
    {
        if (!_dragReady || e.LeftButton != MouseButtonState.Pressed || _dragSourceId == Guid.Empty) return;
        var pos  = Mouse.GetPosition(GridPanel);
        var diff = pos - _dragStartPoint;
        if (Math.Abs(diff.X) < SystemParameters.MinimumHorizontalDragDistance &&
            Math.Abs(diff.Y) < SystemParameters.MinimumVerticalDragDistance)
            return;

        _dragReady     = false;
        _pendingPlayId = Guid.Empty; // Prevent click-to-play from firing on mouse-up
        var dragId     = _dragSourceId;
        _dragSourceId  = Guid.Empty;

        if (!CanReorder(out var blocked)) { StatusText.Text = blocked; return; }

        DragDrop.DoDragDrop(GridPanel, new DataObject(InternalReorderFormat, dragId.ToString()), DragDropEffects.Move);
        RemoveDropIndicator();
    }

    // ── Drag reorder: helpers ─────────────────────────────────────────────────

    private static bool IsInternalReorder(IDataObject data)
        => data.GetDataPresent(InternalReorderFormat);

    private bool CanReorder(out string reason)
    {
        var cat    = CategoryFilter?.SelectedItem as string ?? "All";
        var search = SearchBox?.Text.Trim() ?? "";
        if (cat != "All" || !string.IsNullOrEmpty(search))
        {
            reason = "Reorder is only available in All view with no search filter.";
            return false;
        }
        reason = "";
        return true;
    }

    private void EnsureDropIndicator(bool isGrid)
    {
        bool isCurrentlyGrid = _dropIndicator?.Tag is "Grid";
        if (_dropIndicator is not null && isCurrentlyGrid == isGrid) return;

        // Type changed or not yet created — remove stale and recreate.
        if (_dropIndicator is not null)
        {
            foreach (var p in new Panel[] { SoundsPanel, GridPanel })
                if (p.Children.Contains(_dropIndicator))
                    p.Children.Remove(_dropIndicator);
            _dropIndicatorIndex = -1;
        }

        var accentBrush = (Brush)Application.Current.Resources["SystemAccentColorPrimaryBrush"];

        if (isGrid)
        {
            var (w, h)     = GetPadDimensions();
            var accentColor = (accentBrush as SolidColorBrush)?.Color ?? _fallbackAccent;
            _dropIndicator = new Border
            {
                Tag             = "Grid",
                Width           = w,
                Height          = h,
                Margin          = new Thickness(4),
                CornerRadius    = new CornerRadius(8),
                BorderBrush     = accentBrush,
                BorderThickness = new Thickness(2),
                Background      = new SolidColorBrush(accentColor) { Opacity = 0.12 },
            };
        }
        else
        {
            _dropIndicator = new Border
            {
                Tag        = "List",
                Height     = 2,
                Background = accentBrush,
                Margin     = new Thickness(0),
            };
        }
    }

    private void RemoveDropIndicator()
    {
        if (_dropIndicator is null) return;
        foreach (var p in new Panel[] { SoundsPanel, GridPanel })
            if (p.Children.Contains(_dropIndicator))
                p.Children.Remove(_dropIndicator);
        _dropIndicatorIndex = -1;
    }

    private void UpdateDropIndicator(DragEventArgs e, Panel panel)
    {
        bool isGrid  = panel is WrapPanel;
        EnsureDropIndicator(isGrid);

        var mousePos = e.GetPosition(panel);
        int newIndex = isGrid ? GetGridDropIndex(mousePos) : GetListDropIndex(mousePos);

        if (_dropIndicatorIndex == newIndex) return; // No change needed

        if (_dropIndicatorIndex >= 0 && panel.Children.Contains(_dropIndicator))
            panel.Children.Remove(_dropIndicator);

        int insertAt = Math.Clamp(newIndex, 0, panel.Children.Count);
        panel.Children.Insert(insertAt, _dropIndicator!);
        _dropIndicatorIndex = newIndex;
    }

    // Returns the logical insert index (among real children, excluding indicator)
    // for the List View StackPanel based on vertical mouse position.
    private int GetListDropIndex(Point mouseInPanel)
    {
        var real = SoundsPanel.Children.Cast<UIElement>()
            .Where(c => c != _dropIndicator)
            .OfType<FrameworkElement>()
            .ToList();

        for (int i = 0; i < real.Count; i++)
        {
            var bounds = real[i].TransformToAncestor(SoundsPanel)
                                 .TransformBounds(new Rect(real[i].RenderSize));
            if (mouseInPanel.Y < bounds.Top + bounds.Height / 2)
                return i;
        }
        return real.Count;
    }

    // Returns the logical insert index for the Grid View WrapPanel.
    // Iterates cards in order; inserts before the first card whose visual
    // position is "after" the cursor (left half or row below).
    private int GetGridDropIndex(Point mouseInPanel)
    {
        var real = GridPanel.Children.Cast<UIElement>()
            .Where(c => c != _dropIndicator)
            .OfType<FrameworkElement>()
            .ToList();

        for (int i = 0; i < real.Count; i++)
        {
            var bounds = real[i].TransformToAncestor(GridPanel)
                                  .TransformBounds(new Rect(real[i].RenderSize));
            // Cursor is within this card's row and in its left half → insert before it
            if (mouseInPanel.Y < bounds.Bottom &&
                mouseInPanel.X < bounds.Left + bounds.Width / 2)
                return i;
            // Cursor is in the gap above this card → insert before it
            if (mouseInPanel.Y < bounds.Top)
                return i;
        }
        return real.Count;
    }

    private void ExecuteReorder(DragEventArgs e)
    {
        if (!e.Data.GetDataPresent(InternalReorderFormat)) return;
        if (!Guid.TryParse(e.Data.GetData(InternalReorderFormat) as string, out var draggedId)) return;

        int fromIndex = _library.FindIndex(x => x.Id == draggedId);
        if (fromIndex < 0) return; // Item no longer in active deck (e.g., deck was switched)

        bool isGrid      = _settings.LibraryView == "Grid";
        var  panel       = isGrid ? (Panel)GridPanel : SoundsPanel;
        var  mouseInPanel = e.GetPosition(panel);
        int  dropIndex   = isGrid ? GetGridDropIndex(mouseInPanel) : GetListDropIndex(mouseInPanel);

        // Compute the adjusted target after the item is removed
        int targetIndex = Math.Clamp(dropIndex, 0, _library.Count);
        int adjusted    = fromIndex < targetIndex ? targetIndex - 1 : targetIndex;
        if (fromIndex == adjusted) return; // No actual move

        var item = _library[fromIndex];
        _library.RemoveAt(fromIndex);
        _library.Insert(adjusted, item);

        DeckService.Save(_decks);
        FilterSoundsPanel();
        StatusText.Text = $"Moved: {item.DisplayName}";
    }

    // ── Mini Mode ─────────────────────────────────────────────────────────────

    // Called from the toolbar button and from MiniOpenOnStartup at startup.
    private void OpenMiniMode()
    {
        if (_miniWindow is null)
        {
            _miniWindow = new MiniWindow(this);
            _miniWindow.ApplySettings(_settings);
            _miniWindow.InitializeDeck(_activeDeck);
        }

        if (_miniWindow.IsVisible)
            _miniWindow.Activate();
        else
        {
            _miniWindow.Show();
            _miniWindow.Activate();
        }
    }

    private void MiniModeButton_Click(object sender, RoutedEventArgs e) => OpenMiniMode();

    // Queried by MiniWindow to decide play vs stop on pad click.
    internal bool IsActivePlayback(Guid soundId) => _activePlaybacks.ContainsKey(soundId);

    // Returns a snapshot of currently active sound IDs so MiniWindow can
    // sync active highlights when it first opens or rebuilds after a deck switch.
    internal IReadOnlyList<Guid> GetActivePlaybackIds() => _activePlaybacks.Keys.ToList();

    // Called by MiniWindow pin button to persist the always-on-top preference.
    internal void SaveMiniAlwaysOnTop(bool value)
    {
        _settings.MiniAlwaysOnTop = value;
        SaveSettings();
    }

    // Called by MiniWindow close button so position is persisted on hide.
    internal void SaveMiniPositionFrom(MiniWindow mini)
    {
        mini.SavePositionTo(_settings);
        SaveSettings();
    }

    private Border BuildPadCard(SoundItem item)
    {
        var capturedItem = item;
        var accentBrush  = (Brush)Application.Current.Resources["SystemAccentColorPrimaryBrush"];
        var categoryText = string.IsNullOrWhiteSpace(item.Category) ? "General" : item.Category;

        // ── Pad dimensions and compact mode ──────────────────────────────────
        var (cardW, cardH) = GetPadDimensions();
        bool compact       = _settings.GridCompact;
        double nameFontSz  = _settings.GridPadSize switch
        {
            "Small" => compact ? 13.0 : 11.0,
            "Large" => compact ? 17.0 : 15.0,
            _       => compact ? 15.0 : 13.0,
        };

        // ── Playing indicator ────────────────────────────────────────────────
        var playingIndicator = new TextBlock
        {
            Text              = "▶",
            FontSize          = 10,
            Foreground        = accentBrush,
            VerticalAlignment = VerticalAlignment.Center,
            Visibility        = Visibility.Collapsed
        };

        // ── Top row: favorite (left, hidden in compact) + hotkey (right) ─────
        var hotkeyBlock = new TextBlock
        {
            Text              = item.Hotkey?.DisplayText ?? "",
            FontSize          = 9,
            Foreground        = accentBrush,
            FontWeight        = FontWeights.SemiBold,
            TextTrimming      = TextTrimming.CharacterEllipsis,
            VerticalAlignment = VerticalAlignment.Center
        };
        var topRow = new DockPanel();
        DockPanel.SetDock(hotkeyBlock, Dock.Right);
        topRow.Children.Add(hotkeyBlock);
        if (!compact)
        {
            var favBlock = new TextBlock
            {
                Text              = "★",
                FontSize          = 11,
                Foreground        = item.IsFavorite
                    ? accentBrush
                    : (Brush)Application.Current.Resources["TextFillColorTertiaryBrush"],
                VerticalAlignment = VerticalAlignment.Center
            };
            topRow.Children.Add(favBlock);
        }

        // ── Center: sound name ───────────────────────────────────────────────
        var nameBlock = new TextBlock
        {
            Text                = item.DisplayName,
            FontSize            = nameFontSz,
            FontWeight          = FontWeights.SemiBold,
            TextWrapping        = TextWrapping.Wrap,
            TextAlignment       = TextAlignment.Center,
            VerticalAlignment   = VerticalAlignment.Center,
            HorizontalAlignment = HorizontalAlignment.Center,
            Foreground          = (Brush)Application.Current.Resources["TextFillColorPrimaryBrush"],
            Margin              = new Thickness(0, 4, 0, 4)
        };

        // ── Bottom row: category badge (hidden in compact) + playing indicator
        var bottomRow = new DockPanel();
        DockPanel.SetDock(playingIndicator, Dock.Right);
        bottomRow.Children.Add(playingIndicator);
        if (!compact)
        {
            var catBadge = new UiBadge
            {
                Content           = categoryText,
                Appearance        = ControlAppearance.Success,
                FontSize          = 9,
                VerticalAlignment = VerticalAlignment.Center
            };
            bottomRow.Children.Add(catBadge);
        }

        // ── Layout ───────────────────────────────────────────────────────────
        var layout = new Grid();
        layout.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        layout.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
        layout.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        Grid.SetRow(topRow,    0);
        Grid.SetRow(nameBlock, 1);
        Grid.SetRow(bottomRow, 2);
        layout.Children.Add(topRow);
        layout.Children.Add(nameBlock);
        layout.Children.Add(bottomRow);

        // ── Card border ──────────────────────────────────────────────────────
        var card = new Border
        {
            Width           = cardW,
            Height          = cardH,
            Margin          = new Thickness(4),
            CornerRadius    = new CornerRadius(8),
            Padding         = new Thickness(10),
            Background      = GetPadBackground(item.PadColor),
            BorderBrush     = (Brush)Application.Current.Resources["CardBorderBrush"],
            BorderThickness = new Thickness(1),
            Cursor          = Cursors.Hand,
            Child           = layout
        };

        // ── SetActive callback ────────────────────────────────────────────────
        Action<bool> setActive = active =>
        {
            if (active)
            {
                var accent = (Application.Current.Resources["SystemAccentColorPrimaryBrush"] as SolidColorBrush)?.Color ?? _fallbackAccent;
                card.Background = new SolidColorBrush(accent) { Opacity = 0.30 };
                playingIndicator.Visibility = Visibility.Visible;
            }
            else
            {
                card.Background = GetPadBackground(capturedItem.PadColor);
                playingIndicator.Visibility = Visibility.Collapsed;
            }
        };
        _rowControls[item.Id] = new RowControls(setActive);

        // ── Click: play / stop ───────────────────────────────────────────────
        // Press sets a pending play intent; release executes it only if no drag occurred.
        // The panel-level PreviewMouseMove clears _pendingPlayId when a drag initiates.
        card.MouseLeftButtonDown += (_, _) =>
        {
            _dragSourceId   = capturedItem.Id;
            _dragStartPoint = Mouse.GetPosition(GridPanel);
            _dragReady      = true;
            _pendingPlayId  = capturedItem.Id;
        };
        card.MouseLeftButtonUp += (_, _) =>
        {
            if (_pendingPlayId != capturedItem.Id) return;
            _pendingPlayId = Guid.Empty;
            _dragReady     = false;
            if (_activePlaybacks.ContainsKey(capturedItem.Id))
                StopSoundById(capturedItem.Id);
            else
                PlayLibraryItem(capturedItem);
        };

        // ── Hover ────────────────────────────────────────────────────────────
        card.MouseEnter += (_, _) => { if (!_activePlaybacks.ContainsKey(capturedItem.Id)) card.Opacity = 0.85; };
        card.MouseLeave += (_, _) =>
        {
            card.Opacity = 1.0;
            // Cancel pending play if the mouse leaves before releasing (likely a drag)
            if (_pendingPlayId == capturedItem.Id) _pendingPlayId = Guid.Empty;
        };

        // ── Context menu ─────────────────────────────────────────────────────
        card.ContextMenu = BuildSoundContextMenu(capturedItem);

        return card;
    }

    private ContextMenu BuildSoundContextMenu(SoundItem item)
    {
        var editMenuItem = new MenuItem { Header = "Edit…" };
        editMenuItem.Click += (_, _) => EditSound(item);

        var favMenuItem = new MenuItem();
        favMenuItem.Click += (_, _) =>
        {
            item.IsFavorite = !item.IsFavorite;
            DeckService.Save(_decks);
            FilterSoundsPanel();
        };

        var dupMenuItem = new MenuItem { Header = "Duplicate" };
        dupMenuItem.Click += (_, _) => DuplicateSound(item);

        var revealMenuItem = new MenuItem { Header = "Reveal in Folder" };
        revealMenuItem.Click += (_, _) => RevealInFolder(item);

        var removeMenuItem = new MenuItem
        {
            Header     = "Remove",
            Foreground = (Brush)Application.Current.Resources["SystemFillColorCriticalBrush"]
        };
        removeMenuItem.Click += (_, _) => RemoveSound(item);

        var ctx = new ContextMenu();
        ctx.Items.Add(editMenuItem);
        ctx.Items.Add(favMenuItem);
        ctx.Items.Add(dupMenuItem);
        ctx.Items.Add(BuildColorMenu(item));
        ctx.Items.Add(revealMenuItem);
        ctx.Items.Add(new Separator());
        ctx.Items.Add(removeMenuItem);
        ctx.Opened += (_, _) => favMenuItem.Header = item.IsFavorite ? "Unfavorite" : "Favorite";
        return ctx;
    }

    private MenuItem BuildColorMenu(SoundItem item)
    {
        var colorMenu = new MenuItem { Header = "Color" };

        var presets = new (string? Hex, string Label)[]
        {
            (null,      "Default"),
            ("#E53935", "Red"),
            ("#F4511E", "Orange"),
            ("#F9AB00", "Yellow"),
            ("#0F9D58", "Green"),
            ("#039BE5", "Blue"),
            ("#7B1FA2", "Purple"),
            ("#D81B60", "Pink"),
            ("#546E7A", "Gray"),
        };

        foreach (var (hex, label) in presets)
        {
            var swatchBg = hex is not null
                ? (Brush)new SolidColorBrush((Color)ColorConverter.ConvertFromString(hex))
                : (Brush)Application.Current.Resources["ControlFillColorDefaultBrush"];
            var swatch = new Border
            {
                Width             = 12,
                Height            = 12,
                CornerRadius      = new CornerRadius(2),
                Background        = swatchBg,
                BorderBrush       = hex is null
                    ? (Brush)Application.Current.Resources["CardBorderBrush"]
                    : Brushes.Transparent,
                BorderThickness   = new Thickness(hex is null ? 1 : 0),
                Margin            = new Thickness(0, 0, 6, 0),
                VerticalAlignment = VerticalAlignment.Center
            };
            var header = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
            header.Children.Add(swatch);
            header.Children.Add(new TextBlock { Text = label, VerticalAlignment = VerticalAlignment.Center });

            var capturedHex = hex;
            var colorItem   = new MenuItem { Header = header };
            colorItem.Click += (_, _) =>
            {
                item.PadColor = capturedHex;
                DeckService.Save(_decks);
                FilterSoundsPanel();
            };
            colorMenu.Items.Add(colorItem);
        }

        return colorMenu;
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
            DeckService.Save(_decks);
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
        if (!_cachedSounds.TryGetValue(item.Id, out var sound)) return;

        var categories = _library
            .Select(x => string.IsNullOrWhiteSpace(x.Category) ? "General" : x.Category)
            .Concat(_activeDeck.CustomCategories)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(c => c, StringComparer.OrdinalIgnoreCase)
            .ToList();

        var takenHotkeys = _library
            .Where(x => x.Id != item.Id && x.Hotkey is not null)
            .Select(x => (x.Hotkey!.Modifiers, x.Hotkey.Key))
            .ToHashSet();
        if (_settings.StopAllHotkey is not null)
            takenHotkeys.Add((_settings.StopAllHotkey.Modifiers, _settings.StopAllHotkey.Key));

        var dlg = new EditSoundDialog(this, item, sound, categories, takenHotkeys, _monitorEngine);
        if (dlg.ShowDialog() != true) return;

        item.DisplayName      = dlg.ResultName;
        item.Category         = dlg.ResultCategory;
        item.Volume           = dlg.ResultVolume;
        item.TrimStartSeconds = dlg.ResultTrimStart;
        item.TrimEndSeconds   = dlg.ResultTrimEnd;
        item.FadeInSeconds    = dlg.ResultFadeIn;
        item.FadeOutSeconds   = dlg.ResultFadeOut;

        if (dlg.WasHotkeyChanged)
        {
            var previousHotkey = item.Hotkey;
            item.Hotkey = dlg.ResultHotkey;

            if (item.Hotkey is not null)
            {
                var result = ReregisterAllHotkeysAndReport("hotkey-edit");
                if (result.FailedSoundIds.Contains(item.Id))
                {
                    item.Hotkey = previousHotkey;
                    ReregisterAllHotkeysAndReport("hotkey-edit-rollback");
                    MessageBox.Show(
                        "Windows could not register the selected hotkey.\n\n" +
                        "It may be in use by another application. Choose a different combination.",
                        "Hotkey unavailable", MessageBoxButton.OK, MessageBoxImage.Warning);
                }
            }
            else
            {
                ReregisterAllHotkeysAndReport("hotkey-clear");
            }
        }

        DeckService.Save(_decks);
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
        DeckService.Save(_decks);
        RefreshCategoryFilter();
        FilterSoundsPanel();
        StatusText.Text = $"Removed: {name}";
    }

    // ── Duplicate Sound ───────────────────────────────────────────────────────

    private void DuplicateSound(SoundItem source)
    {
        if (!_cachedSounds.TryGetValue(source.Id, out var cachedSound)) return;

        var copy = new SoundItem
        {
            DisplayName      = source.DisplayName + " (copy)",
            FilePath         = source.FilePath,
            Category         = source.Category,
            Volume           = source.Volume,
            TrimStartSeconds = source.TrimStartSeconds,
            TrimEndSeconds   = source.TrimEndSeconds,
            FadeInSeconds    = source.FadeInSeconds,
            FadeOutSeconds   = source.FadeOutSeconds,
            PadColor         = source.PadColor,
            CreatedAt        = DateTime.UtcNow
            // Hotkey intentionally not copied — would create a conflict
        };
        _cachedSounds[copy.Id] = cachedSound;
        _library.Add(copy);
        DeckService.Save(_decks);
        RefreshCategoryFilter();
        FilterSoundsPanel();
        StatusText.Text = $"Duplicated: {copy.DisplayName}";
    }

    // ── Reveal in Folder ─────────────────────────────────────────────────────

    private static void RevealInFolder(SoundItem item)
    {
        if (!File.Exists(item.FilePath)) return;
        try { Process.Start("explorer.exe", $"/select,\"{item.FilePath}\""); }
        catch { }
    }

    // ── Category Manager ─────────────────────────────────────────────────────

    private void CategoryManager_Click(object sender, RoutedEventArgs e)
    {
        var dlg = new Dialogs.CategoryManagerDialog(this, _library, _activeDeck.CustomCategories);
        if (dlg.ShowDialog() != true) return;

        if (dlg.SoundCategoryRemap.Count > 0)
        {
            foreach (var item in _library)
            {
                var cat = string.IsNullOrWhiteSpace(item.Category) ? "General" : item.Category;
                string resolved = cat;
                int guard = 0;
                while (dlg.SoundCategoryRemap.TryGetValue(resolved, out var next) && ++guard < 50)
                    resolved = next;
                if (!string.Equals(resolved, cat, StringComparison.OrdinalIgnoreCase))
                    item.Category = resolved;
            }
        }

        // Persist empty categories that have no sounds assigned yet.
        _activeDeck.CustomCategories = dlg.FinalCategories
            .Where(c => !_library.Any(s =>
                string.Equals(s.Category, c, StringComparison.OrdinalIgnoreCase)))
            .ToList();

        DeckService.Save(_decks);
        RefreshCategoryFilter();
        FilterSoundsPanel();
        StatusText.Text = "Categories updated.";
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
            DeckService.Save(_decks);
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

        DeckService.Save(_decks);
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

    // ── Active playback helpers ────────────────────────────────────────────────

    private void EnsurePlaybackMonitor()
    {
        if (_playbackMonitor is not null)
            return;

        _playbackMonitor = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(100) };
        _playbackMonitor.Tick += PlaybackMonitor_Tick;
        _playbackMonitor.Start();
    }

    private void PlaybackMonitor_Tick(object? sender, EventArgs e)
    {
        var finished = _activePlaybacks
            .Where(kv => kv.Value.IsFinished)
            .Select(kv => kv.Key)
            .ToList();

        foreach (var id in finished)
        {
            _activePlaybacks.Remove(id);
            UpdateRowState(id, active: false);
        }

        if (_activePlaybacks.Count == 0)
        {
            _playbackMonitor?.Stop();
            _playbackMonitor = null;
        }
    }

    internal static readonly Color _fallbackAccent = Color.FromRgb(0, 120, 215);

    private void UpdateRowState(Guid soundId, bool active)
    {
        if (_rowControls.TryGetValue(soundId, out var ctrl))
            ctrl.SetActive(active);
        PlaybackStateChanged?.Invoke(soundId, active);
    }

    internal void StopSoundById(Guid soundId)
    {
        if (!_activePlaybacks.TryGetValue(soundId, out var playback))
            return;

        if (playback.MonitorHandle is not null)
            _monitorEngine?.StopOne(playback.MonitorHandle.TopProvider);
        if (playback.VirtualHandle is not null)
            _virtualEngine?.StopOne(playback.VirtualHandle.TopProvider);

        _activePlaybacks.Remove(soundId);
        UpdateRowState(soundId, active: false);

        if (_activePlaybacks.Count == 0)
        {
            _playbackMonitor?.Stop();
            _playbackMonitor = null;
        }
    }

    private void ClearAllActivePlaybacks()
    {
        _monitorEngine?.StopAll();
        _virtualEngine?.StopAll();

        var ids = _activePlaybacks.Keys.ToList();
        _activePlaybacks.Clear();
        foreach (var id in ids)
            UpdateRowState(id, active: false);

        _playbackMonitor?.Stop();
        _playbackMonitor = null;
    }

    internal void PlayLibraryItem(SoundItem item)
    {
        if (!_cachedSounds.TryGetValue(item.Id, out var sound))
        {
            StatusText.Text = $"Not loaded: {item.DisplayName}";
            return;
        }

        item.LastPlayedAt = DateTime.UtcNow;
        DeckService.Save(_decks);

        if (CategoryFilter?.SelectedItem as string == "Recent")
            FilterSoundsPanel();

        if (_settings.InterruptPreviousSounds)
            ClearAllActivePlaybacks();

        PlaySound(item.Id, sound, item);
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

    private void PlaySound(Guid soundId, CachedSound sound, SoundItem item)
    {
        if (_monitorEngine is null && _virtualEngine is null)
        {
            StatusText.Text = "No output device selected.";
            return;
        }

        try
        {
            float volume = ConvertUiVolumeToGain(item.Volume);
            var (startS, endS, fadeInS, fadeOutS) = GetTrimFadeParams(sound, item);
            bool hasTrimFade = startS != 0 || endS >= 0 || fadeInS != 0 || fadeOutS != 0;

            PlaybackHandle? monitorHandle = hasTrimFade
                ? _monitorEngine?.Play(sound, volume, startS, endS, fadeInS, fadeOutS)
                : _monitorEngine?.Play(sound, volume);

            var monitorDevice = MonitorComboBox.SelectedItem as AudioDevice;
            var virtualDevice = VirtualComboBox.SelectedItem as AudioDevice;

            bool sameDevice = _virtualEngine is not null
                           && AudioDevice.AreSameOutputDevice(monitorDevice, virtualDevice);

            PlaybackHandle? virtualHandle = sameDevice ? null
                : hasTrimFade
                    ? _virtualEngine?.Play(sound, volume, startS, endS, fadeInS, fadeOutS)
                    : _virtualEngine?.Play(sound, volume);

            // Stop the previous instance of this same sound before registering the new one.
            if (_activePlaybacks.TryGetValue(soundId, out var previous))
            {
                if (previous.MonitorHandle is not null)
                    _monitorEngine?.StopOne(previous.MonitorHandle.TopProvider);
                if (previous.VirtualHandle is not null)
                    _virtualEngine?.StopOne(previous.VirtualHandle.TopProvider);
            }

            if (monitorHandle is not null || virtualHandle is not null)
            {
                _activePlaybacks[soundId] = new ActivePlayback
                {
                    SoundId       = soundId,
                    MonitorHandle = monitorHandle,
                    VirtualHandle = virtualHandle,
                };
                UpdateRowState(soundId, active: true);
                EnsurePlaybackMonitor();
            }

            StatusText.Text = sameDevice
                ? $"▶ {item.DisplayName}  (Virtual = Monitor, playing once)"
                : $"▶ {item.DisplayName}";
        }
        catch (Exception ex)
        {
            StatusText.Text = $"Playback error: {ex.Message}";
        }
    }

    private static (int Start, int End, int FadeIn, int FadeOut) GetTrimFadeParams(
        CachedSound sound, SoundItem item)
    {
        int sr = sound.SampleRate * sound.Channels;
        int start    = item.TrimStartSeconds.HasValue ? (int)(item.TrimStartSeconds.Value * sr) : 0;
        int end      = item.TrimEndSeconds.HasValue   ? (int)(item.TrimEndSeconds.Value   * sr) : -1;
        int fadeIn   = item.FadeInSeconds.HasValue    ? (int)(item.FadeInSeconds.Value    * sr) : 0;
        int fadeOut  = item.FadeOutSeconds.HasValue   ? (int)(item.FadeOutSeconds.Value   * sr) : 0;
        return (start, end, fadeIn, fadeOut);
    }

    // ── Stop All ──────────────────────────────────────────────────────────────

    private void StopButton_Click(object sender, RoutedEventArgs e) => StopAllSounds();

    // Public stop-all entry point: clears active sounds, stops the test tone,
    // and updates the status bar.  Mic passthrough is unaffected (it uses
    // AddMixerInput, not the engine's tracked _active list).
    internal void StopAllSounds()
    {
        ClearAllActivePlaybacks();
        StopVirtualTestTone();
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

            RefreshRoutingWizard();
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

        ClearAllActivePlaybacks();
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
        RefreshRoutingWizard();
    }

    private void VirtualComboBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (VirtualComboBox.SelectedItem is not AudioDevice device)
            return;

        StopMicPassthrough();
        StopVirtualTestTone();

        ClearAllActivePlaybacks();
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

        RefreshRoutingWizard();
    }

    private void CreateMonitorEngine(int deviceNumber)
    {
        var (latency, buffers) = GetPresetParams();
        try   { _monitorEngine = new AudioPlaybackEngine(deviceNumber, latency, buffers); }
        catch (Exception ex)
        {
            _monitorEngine  = null;
            StatusText.Text = $"Monitor device error: {ex.Message}";
        }
    }

    private void CreateVirtualEngine(int deviceNumber)
    {
        var (latency, buffers) = GetPresetParams();
        try   { _virtualEngine = new AudioPlaybackEngine(deviceNumber, latency, buffers); }
        catch (Exception ex)
        {
            _virtualEngine  = null;
            StatusText.Text = $"Virtual device error: {ex.Message}";
        }
    }

    // ══════════════════════════════════════════════════════════════════════════
    //  AUDIO PERFORMANCE PRESETS
    // ══════════════════════════════════════════════════════════════════════════

    private (int desiredLatency, int numberOfBuffers) GetPresetParams() =>
        _settings.AudioPerformancePreset switch
        {
            "Stable"      => (300, 3),
            "Low Latency" => (60,  2),
            _             => (100, 2),  // "Balanced" and unknown values
        };

    private void UpdatePresetDescription(string preset)
    {
        if (PresetDescriptionText is null) return;
        PresetDescriptionText.Text = preset switch
        {
            "Stable"      => "300 ms buffer — most reliable, slight delay before sound starts.",
            "Low Latency" => "60 ms buffer — fastest response, may crackle on slow systems.",
            _             => "100 ms buffer — recommended for most systems.",
        };
    }

    private void RestoreAudioPerformancePreset()
    {
        PresetStableRadio.Checked      -= PresetRadio_Checked;
        PresetBalancedRadio.Checked    -= PresetRadio_Checked;
        PresetLowLatencyRadio.Checked  -= PresetRadio_Checked;

        PresetStableRadio.IsChecked     = _settings.AudioPerformancePreset == "Stable";
        PresetBalancedRadio.IsChecked   = _settings.AudioPerformancePreset == "Balanced";
        PresetLowLatencyRadio.IsChecked = _settings.AudioPerformancePreset == "Low Latency";

        if (!PresetStableRadio.IsChecked.GetValueOrDefault() &&
            !PresetBalancedRadio.IsChecked.GetValueOrDefault() &&
            !PresetLowLatencyRadio.IsChecked.GetValueOrDefault())
            PresetBalancedRadio.IsChecked = true;

        UpdatePresetDescription(_settings.AudioPerformancePreset);

        PresetStableRadio.Checked      += PresetRadio_Checked;
        PresetBalancedRadio.Checked    += PresetRadio_Checked;
        PresetLowLatencyRadio.Checked  += PresetRadio_Checked;
    }

    private void PresetRadio_Checked(object sender, RoutedEventArgs e)
    {
        if (sender is not System.Windows.Controls.RadioButton rb) return;
        string newPreset = rb.Tag as string ?? "Balanced";
        ApplyAudioPerformancePreset(newPreset);
    }

    private void ApplyAudioPerformancePreset(string newPreset)
    {
        string previousPreset = _settings.AudioPerformancePreset;
        if (newPreset == previousPreset) return;

        // Remember mic passthrough state; stop it before touching engines.
        bool micWasActive = _micPassthrough is not null;
        if (micWasActive) StopMicPassthrough();

        // Stop all playing sounds and the test tone.
        ClearAllActivePlaybacks();
        StopVirtualTestTone();

        // Commit the new preset so GetPresetParams() picks it up.
        _settings.AudioPerformancePreset = newPreset;
        UpdatePresetDescription(newPreset);

        // Recreate monitor engine.
        int? monitorNum = (_monitorEngine is not null)
            ? (MonitorComboBox.SelectedItem as AudioDevice)?.Number
            : null;
        _monitorEngine?.StopAll();
        _monitorEngine?.Dispose();
        _monitorEngine = null;

        if (monitorNum.HasValue)
            CreateMonitorEngine(monitorNum.Value);

        // Recreate virtual engine.
        int? virtualNum = (_virtualEngine is not null)
            ? (VirtualComboBox.SelectedItem as AudioDevice)?.Number
            : null;
        _virtualEngine?.StopAll();
        _virtualEngine?.Dispose();
        _virtualEngine = null;

        if (virtualNum.HasValue)
            CreateVirtualEngine(virtualNum.Value);

        // If either required engine failed to create, roll back.
        bool monitorFailed = monitorNum.HasValue && _monitorEngine is null;
        bool virtualFailed = virtualNum.HasValue && _virtualEngine is null;

        if (monitorFailed || virtualFailed)
        {
            StatusText.Text = $"Preset '{newPreset}' failed — reverting to '{previousPreset}'.";
            _settings.AudioPerformancePreset = previousPreset;

            // Recreate with original preset values.
            if (monitorNum.HasValue)
            {
                _monitorEngine?.Dispose();
                _monitorEngine = null;
                CreateMonitorEngine(monitorNum.Value);
            }
            if (virtualNum.HasValue)
            {
                _virtualEngine?.Dispose();
                _virtualEngine = null;
                CreateVirtualEngine(virtualNum.Value);
            }

            RestoreAudioPerformancePreset();
        }
        else
        {
            SaveSettings();
        }

        // Restore mic passthrough if it was running.
        if (micWasActive && _virtualEngine is not null)
            StartMicPassthrough();
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
            RefreshRoutingWizard();
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

        RefreshRoutingWizard();
    }

    private void MicPassthroughCheckBox_Checked(object sender, RoutedEventArgs e)
    {
        StartMicPassthrough();
        _settings.MicPassthroughEnabled = MicPassthroughCheckBox.IsChecked == true;
        SaveSettings();
        RefreshRoutingWizard();
    }

    private void MicPassthroughCheckBox_Unchecked(object sender, RoutedEventArgs e)
    {
        StopMicPassthrough();
        _settings.MicPassthroughEnabled = MicPassthroughCheckBox.IsChecked == true;
        SaveSettings();
        RefreshRoutingWizard();
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

    // ── Import / Export Library Backup ────────────────────────────────────────

    private void ExportBackup_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new SaveFileDialog
        {
            Title           = "Export Sound Library Backup",
            Filter          = "SoundPad Backup (*.zip)|*.zip",
            FileName        = $"SoundPad-Backup-{DateTime.Now:yyyy-MM-dd}.zip",
            DefaultExt      = ".zip",
            AddExtension    = true,
            OverwritePrompt = true
        };

        if (dialog.ShowDialog() != true) return;

        try
        {
            LibraryBackupService.Export(_decks, dialog.FileName);
            StatusText.Text = $"Backup exported: {Path.GetFileName(dialog.FileName)}";
        }
        catch (Exception ex)
        {
            StatusText.Text = $"Export failed: {ex.Message}";
        }
    }

    private void ImportBackup_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new OpenFileDialog
        {
            Title  = "Import Sound Library Backup",
            Filter = "SoundPad Backup (*.zip)|*.zip"
        };

        if (dialog.ShowDialog() != true) return;

        ImportResult result;
        try
        {
            result = LibraryBackupService.Import(dialog.FileName, _decks, _activeDeck, _settings.StopAllHotkey);
        }
        catch (Exception ex)
        {
            StatusText.Text = $"Import failed: {ex.Message}";
            return;
        }

        // Preload audio for all newly added sounds (already inserted into decks by the service).
        foreach (var item in result.AllNewSounds)
        {
            if (File.Exists(item.FilePath))
            {
                try   { _cachedSounds[item.Id] = new CachedSound(item.FilePath); }
                catch (Exception ex) { Debug.WriteLine($"[Import] Could not preload '{item.DisplayName}': {ex.Message}"); }
            }
        }

        if (result.AllNewSounds.Count > 0 || result.DecksAdded > 0)
        {
            DeckService.Save(_decks);
            RebuildDeckBar();
            RefreshCategoryFilter();
            FilterSoundsPanel();
            ReregisterAllHotkeysAndReport("import");
        }

        var parts = new List<string>();
        if (result.DecksAdded > 0)        parts.Add($"added {result.DecksAdded} deck(s)");
        if (result.DecksMerged > 0)       parts.Add($"merged {result.DecksMerged} deck(s)");
        if (result.AllNewSounds.Count > 0) parts.Add($"imported {result.AllNewSounds.Count} sound(s)");
        if (result.SkippedDuplicates > 0) parts.Add($"skipped {result.SkippedDuplicates} duplicate(s)");
        if (result.ClearedHotkeys > 0)    parts.Add($"cleared {result.ClearedHotkeys} conflicting hotkey(s)");

        StatusText.Text = (parts.Count > 0 ? string.Join(", ", parts) : "Nothing to import") + ".";
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
        if (IsInternalReorder(e.Data))
        {
            e.Effects = DragDropEffects.Move;
            Panel panel = _settings.LibraryView == "Grid" ? GridPanel : SoundsPanel;
            UpdateDropIndicator(e, panel);
        }
        else if (HasAudioFiles(e.Data))
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
        if (IsInternalReorder(e.Data))
        {
            e.Effects = DragDropEffects.Move;
            Panel panel = _settings.LibraryView == "Grid" ? GridPanel : SoundsPanel;
            UpdateDropIndicator(e, panel);
        }
        else
        {
            e.Effects = HasAudioFiles(e.Data) ? DragDropEffects.Copy : DragDropEffects.None;
        }
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
            RemoveDropIndicator();
        }
    }

    private void SoundsArea_Drop(object sender, DragEventArgs e)
    {
        SoundsAreaBorder.BorderBrush = (Brush)Application.Current.Resources["CardBorderBrush"];
        RemoveDropIndicator();

        if (IsInternalReorder(e.Data))
        {
            ExecuteReorder(e);
            return;
        }

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
            DeckService.Save(_decks);
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

    // ══════════════════════════════════════════════════════════════════════════
    //  DISCORD / GAME ROUTING WIZARD
    // ══════════════════════════════════════════════════════════════════════════

    // Updates every wizard UI element to reflect the current routing state.
    // Safe to call at any time; the null-guard on WizardMonitorDot protects
    // against edge-case calls before InitializeComponent completes.
    private void RefreshRoutingWizard()
    {
        if (WizardMonitorDot is null) return;

        var successBrush = (Brush)Application.Current.Resources["SystemFillColorSuccessBrush"];
        var cautionBrush  = (Brush)Application.Current.Resources["SystemFillColorCautionBrush"];

        // ── Monitor status ──────────────────────────────────────────────────
        var monitorDevice = MonitorComboBox.SelectedItem as AudioDevice;
        WizardMonitorDot.Foreground  = successBrush;
        WizardMonitorStatusText.Text = monitorDevice?.Name ?? "Not selected";

        // ── Virtual output status ───────────────────────────────────────────
        var virtualDevice  = VirtualComboBox.SelectedItem as AudioDevice;
        bool virtualIsNone = virtualDevice is null || virtualDevice.IsNone;

        WizardVirtualDot.Foreground  = virtualIsNone ? cautionBrush : successBrush;
        WizardVirtualStatusText.Text = virtualIsNone ? "None" : virtualDevice!.Name;

        // ── Mic passthrough status ──────────────────────────────────────────
        bool micEnabled = MicPassthroughCheckBox.IsChecked == true;
        WizardMicStatusRow.Visibility = micEnabled ? Visibility.Visible : Visibility.Collapsed;
        if (micEnabled)
        {
            var micDevice = MicComboBox.SelectedItem as MicDevice;
            bool micOk    = micDevice is not null;
            WizardMicDot.Foreground  = micOk ? successBrush : cautionBrush;
            WizardMicStatusText.Text = micOk ? micDevice!.Name : "No microphone selected";
        }

        // ── Warning banners and recommended panel ───────────────────────────
        bool sameDevice  = !virtualIsNone && AudioDevice.AreSameOutputDevice(monitorDevice, virtualDevice);
        bool micNoDevice = micEnabled && MicComboBox.SelectedItem is null;
        var  best        = FindBestVirtualCableDevice();
        bool noCable     = best is null;

        WizardNoVirtualWarning.Visibility   = virtualIsNone ? Visibility.Visible : Visibility.Collapsed;
        WizardConflictWarning.Visibility    = sameDevice    ? Visibility.Visible : Visibility.Collapsed;
        WizardMicNoDeviceWarning.Visibility = micNoDevice   ? Visibility.Visible : Visibility.Collapsed;
        WizardNoCableWarning.Visibility     = noCable       ? Visibility.Visible : Visibility.Collapsed;

        WizardRecommendedPanel.Visibility = best is not null ? Visibility.Visible : Visibility.Collapsed;
        if (best is not null)
            WizardRecommendedDeviceName.Text = $"Will select: {best.Name}";
    }

    // Returns the highest-priority virtual cable device found in the current
    // VirtualComboBox list, or null when no virtual cable is installed.
    private AudioDevice? FindBestVirtualCableDevice()
    {
        if (VirtualComboBox.ItemsSource is not IEnumerable<AudioDevice> devices)
            return null;

        var list = devices.Where(d => !d.IsNone).ToList();

        var found = list.FirstOrDefault(d =>
            d.Name.Contains("CABLE Input", StringComparison.OrdinalIgnoreCase));
        if (found is not null) return found;

        found = list.FirstOrDefault(d =>
            d.Name.Contains("Voicemeeter Input", StringComparison.OrdinalIgnoreCase));
        if (found is not null) return found;

        found = list.FirstOrDefault(d =>
            d.Name.Contains("VoiceMeeter VAIO", StringComparison.OrdinalIgnoreCase) ||
            d.Name.Contains("Voicemeeter AUX",  StringComparison.OrdinalIgnoreCase));
        if (found is not null) return found;

        found = list.FirstOrDefault(d =>
            d.Name.Contains("CABLE", StringComparison.OrdinalIgnoreCase));
        if (found is not null) return found;

        return list.FirstOrDefault(d =>
            d.Name.Contains("Voicemeeter", StringComparison.OrdinalIgnoreCase) ||
            d.Name.Contains("VoiceMeeter", StringComparison.OrdinalIgnoreCase));
    }

    private void UseRecommendedSetup_Click(object sender, RoutedEventArgs e)
    {
        var best = FindBestVirtualCableDevice();
        if (best is null)
        {
            StatusText.Text = "No virtual cable device found.";
            return;
        }

        // Setting SelectedItem triggers VirtualComboBox_SelectionChanged which
        // recreates the virtual engine, saves settings, and calls RefreshRoutingWizard.
        VirtualComboBox.SelectedItem = best;
        StatusText.Text = $"Virtual Output set to: {best.Name}";
    }

    // Removes the current test tone from the virtual engine's mixer.
    // Safe to call when no tone is active or when the tone has already
    // self-terminated (MixingSampleProvider ignores unknown inputs).
    private void StopVirtualTestTone()
    {
        if (_virtualTestToneProvider is null) return;
        _virtualEngine?.RemoveMixerInput(_virtualTestToneProvider);
        _virtualTestToneProvider = null;
    }

    private void TestVirtualOutput_Click(object sender, RoutedEventArgs e)
    {
        if (_virtualEngine is null)
        {
            StatusText.Text = "Virtual Output is None — select a virtual cable device first.";
            return;
        }

        StopVirtualTestTone(); // stop / replace any previous test tone

        var generator = new SignalGenerator(CachedSound.TargetFormat.SampleRate,
                                             CachedSound.TargetFormat.Channels)
        {
            Type      = SignalGeneratorType.Sin,
            Frequency = 440,
            Gain      = 0.25f
        };

        // 1.5 seconds of interleaved stereo samples at 48 kHz
        int totalSamples = (int)(CachedSound.TargetFormat.SampleRate * 1.5 *
                                  CachedSound.TargetFormat.Channels);
        _virtualTestToneProvider = new OffsetSampleProvider(generator) { TakeSamples = totalSamples };

        _virtualEngine.AddMixerInput(_virtualTestToneProvider);
        StatusText.Text = "Test tone playing to Virtual Output...";
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

        // Persist Mini Mode position/topmost before SaveSettings writes to disk.
        if (_miniWindow is not null)
        {
            _miniWindow.SavePositionTo(_settings);
            _settings.MiniAlwaysOnTop = _miniWindow.Topmost;
        }
        SaveSettings();

        _downloadCts?.Cancel();
        _downloadCts?.Dispose();
        _downloadCts = null;

        // Close Mini Mode before disposing audio engines.
        _miniWindow?.ForceClose();
        _miniWindow = null;

        _trayIcon?.Dispose();
        _trayIcon = null;
        _hotkeyService?.UnregisterAll();
        _hotkeys?.Dispose();
        _micPassthrough?.Dispose();
        _monitorEngine?.Dispose();
        _virtualEngine?.Dispose();
    }

}
