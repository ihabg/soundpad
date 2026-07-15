namespace SoundPad.App.Models;





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

    
    
    
    
    public List<string> CustomCategories { get; set; } = new();

    
    public Guid? ActiveDeckId { get; set; }

    
    
    public string LibraryView { get; set; } = "List";

    
    
    public string GridPadSize { get; set; } = "Medium";

    
    
    public bool GridCompact { get; set; } = false;

    
    
    public string LibrarySortOrder { get; set; } = "Manual";

    
    
    public bool    MiniAlwaysOnTop   { get; set; } = true;
    public bool    MiniOpenOnStartup { get; set; } = false;
    public double? MiniWindowLeft    { get; set; }
    public double? MiniWindowTop     { get; set; }
    public double? MiniWindowWidth   { get; set; }
    public double? MiniWindowHeight  { get; set; }

    
    
    public bool           InstantReplayEnabled        { get; set; } = false;
    public int            InstantReplayMinutes         { get; set; } = 1;
    public HotkeyBinding? InstantReplayClipHotkey     { get; set; }
    public HotkeyBinding? InstantReplayToggleHotkey   { get; set; }
    
    public string?        InstantReplayCaptureDeviceId { get; set; }

    
    
    public bool    InstantReplayIncludeMicrophone { get; set; } = false;
    public string? InstantReplayMicDeviceName     { get; set; }
    public int?    InstantReplayMicDeviceNumber   { get; set; }
    public float   InstantReplayMicVolume         { get; set; } = 1.0f;
}
