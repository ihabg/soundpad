using NAudio.Wave;

namespace SoundPad.App.Audio;

// Represents one Windows audio output device shown in a ComboBox.
// WPF calls ToString() on each item to display it, so we override it.
public class AudioDevice
{
    // -1 = WAVE_MAPPER: always routes to whatever Windows has set as default.
    public const int DefaultDeviceNumber = -1;

    // int.MinValue is used as a sentinel for the "None" option.
    // It is far enough from any real device index that it will never collide.
    public const int NoneDeviceNumber = int.MinValue;

    public int    Number { get; }
    public string Name   { get; }

    // True when this entry means "do not play to this output".
    public bool IsNone => Number == NoneDeviceNumber;

    // The single "None" entry prepended to the Virtual Output list.
    public static readonly AudioDevice None = new AudioDevice(NoneDeviceNumber, "None");

    public AudioDevice(int number, string name)
    {
        Number = number;
        Name   = name;
    }

    public override string ToString() => Name;

    // Used for Monitor Output: Default device first, then all real devices.
    public static List<AudioDevice> GetAll()
    {
        var list = new List<AudioDevice>();

        list.Add(new AudioDevice(DefaultDeviceNumber, "Default Output Device"));

        for (int n = 0; n < WaveOut.DeviceCount; n++)
        {
            var caps = WaveOut.GetCapabilities(n);
            list.Add(new AudioDevice(n, caps.ProductName));
        }

        return list;
    }

    // Used for Virtual Output: "None" first, then all the same entries as GetAll().
    public static List<AudioDevice> GetAllWithNone()
    {
        var list = new List<AudioDevice>();

        list.Add(None);          // selecting this means no virtual output
        list.AddRange(GetAll()); // all regular devices follow

        return list;
    }

    // Resolves WAVE_MAPPER (-1) to the real device index it currently points to.
    //
    // Windows exposes the default device under the special index -1 (WAVE_MAPPER).
    // To compare it against a named device we need the real index.
    // Strategy: ask Windows for the product name of device -1, then scan the
    // numbered devices until we find one with the same name.
    //
    // Returns -1 if resolution fails (no devices, driver error, etc.).
    // In that case two "Default" entries still compare equal (-1 == -1), so
    // double-play is still prevented.
    public static int ResolveDefaultDeviceNumber()
    {
        try
        {
            string defaultName = WaveOut.GetCapabilities(DefaultDeviceNumber).ProductName;

            for (int n = 0; n < WaveOut.DeviceCount; n++)
            {
                if (WaveOut.GetCapabilities(n).ProductName == defaultName)
                    return n;
            }
        }
        catch
        {
            // Swallow: no audio hardware, driver error, etc.
        }

        return DefaultDeviceNumber; // could not resolve; leave as -1
    }

    // Returns true when both devices will route audio to the same physical output.
    //
    // This is necessary because Windows exposes -1 (WAVE_MAPPER / "Default Output
    // Device") as an alias for whatever the real default device is.  Comparing
    // device numbers directly misses the case where Default == some named device.
    public static bool AreSameOutputDevice(AudioDevice? monitor, AudioDevice? virtualOut)
    {
        if (monitor is null || virtualOut is null || virtualOut.IsNone)
            return false;

        // Resolve -1 to a real index before comparing.
        int monitorIndex = monitor.Number == DefaultDeviceNumber
            ? ResolveDefaultDeviceNumber()
            : monitor.Number;

        int virtualIndex = virtualOut.Number == DefaultDeviceNumber
            ? ResolveDefaultDeviceNumber()
            : virtualOut.Number;

        return monitorIndex == virtualIndex;
    }
}
