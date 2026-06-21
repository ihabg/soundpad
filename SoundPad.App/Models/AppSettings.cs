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

    public bool MinimizeToTray   { get; set; } = false;
    public bool CloseToTray      { get; set; } = false;
    public bool StartWithWindows { get; set; } = false;

    public double? WindowLeft   { get; set; }
    public double? WindowTop    { get; set; }
    public double? WindowWidth  { get; set; }
    public double? WindowHeight { get; set; }
}
