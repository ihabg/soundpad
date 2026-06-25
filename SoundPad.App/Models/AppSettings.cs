namespace SoundPad.App.Models;

// Persistent app-level settings, distinct from the sound library itself.
// Device fields store both Name and Number because device numbers can shift
// between launches (e.g. a USB device enumerated in a different order), while
// the product name usually stays stable for the same physical device.
public class AppSettings
{
    public HotkeyBinding? StopAllHotkey { get; set; }

    public string? MonitorDeviceName   { get; set; }
    public int?    MonitorDeviceNumber { get; set; }

    public string? VirtualDeviceName   { get; set; }
    public int?    VirtualDeviceNumber { get; set; }

    public string? MicDeviceName       { get; set; }
    public int?    MicDeviceNumber     { get; set; }

    public bool MicPassthroughEnabled { get; set; } = false;
    public int  MicVolume             { get; set; } = 80;

    // "Stable" | "Balanced" | "Low Latency"
    // Default "Balanced" applies to both new installs and old settings.json files
    // that pre-date this field (System.Text.Json uses the property initializer).
    public string AudioPerformancePreset { get; set; } = "Balanced";

    public int SelectedTabIndex { get; set; } = 0;

    public bool InterruptPreviousSounds { get; set; } = false;

    public bool      EnableAutoUpdateChecks { get; set; } = false;
    public DateTime? LastUpdateCheckUtc     { get; set; }

    public bool MinimizeToTray   { get; set; } = false;
    public bool CloseToTray      { get; set; } = false;
    public bool StartWithWindows { get; set; } = false;

    public double? WindowLeft   { get; set; }
    public double? WindowTop    { get; set; }
    public double? WindowWidth  { get; set; }
    public double? WindowHeight { get; set; }

    // User-created categories that have no sounds assigned yet.
    // Categories derived from SoundItem.Category are always shown; this list
    // keeps empty/new categories alive between launches.
    // Defaults to empty list so old settings.json files deserialize cleanly.
    public List<string> CustomCategories { get; set; } = new();

    // Tracks which deck was active on last exit; null means use the first deck.
    public Guid? ActiveDeckId { get; set; }

    // "List" | "Grid" — persisted so the chosen library view survives restarts.
    // Default "List" applies to both new installs and old settings.json files.
    public string LibraryView { get; set; } = "List";

    // "Small" | "Medium" | "Large" — pad dimensions in Grid View.
    // Default "Medium" preserves the v1.5 card size for existing users.
    public string GridPadSize { get; set; } = "Medium";

    // true = compact pads (name + hotkey only); false = full (name + category + hotkey + favorite).
    // Default false preserves the v1.5 layout for existing users.
    public bool GridCompact { get; set; } = false;

    // "Manual" | "Name A–Z" | "Name Z–A" | "Newest" | "Oldest"
    // Default "Manual" preserves drag-reorder behavior for existing users.
    public string LibrarySortOrder { get; set; } = "Manual";

    // Mini Mode / Floating Soundboard
    // All nullable/defaulted so old settings.json files deserialize cleanly.
    public bool    MiniAlwaysOnTop   { get; set; } = true;
    public bool    MiniOpenOnStartup { get; set; } = false;
    public double? MiniWindowLeft    { get; set; }
    public double? MiniWindowTop     { get; set; }
    public double? MiniWindowWidth   { get; set; }
    public double? MiniWindowHeight  { get; set; }

    // Instant Replay — OFF by default; no audio is captured unless the user enables it.
    // Old settings.json files without these fields deserialize cleanly via initializers.
    public bool           InstantReplayEnabled        { get; set; } = false;
    public int            InstantReplayMinutes         { get; set; } = 1;
    public HotkeyBinding? InstantReplayClipHotkey     { get; set; }
    public HotkeyBinding? InstantReplayToggleHotkey   { get; set; }
    // null = use Windows default render endpoint; otherwise a WASAPI device ID string.
    public string?        InstantReplayCaptureDeviceId { get; set; }

    // Microphone capture for Instant Replay — OFF by default (privacy default).
    // Only active while Instant Replay is ON and IncludeMicrophone is true.
    public bool    InstantReplayIncludeMicrophone { get; set; } = false;
    public string? InstantReplayMicDeviceName     { get; set; }
    public int?    InstantReplayMicDeviceNumber   { get; set; }
    public float   InstantReplayMicVolume         { get; set; } = 1.0f;
}
