using NAudio.Wave;

namespace SoundPad.App.Audio;

// Represents one Windows audio output device shown in the ComboBox.
// WPF calls ToString() on each ComboBox item to display it, which is
// why we override it to return the device name.
public class AudioDevice
{
    // -1 is WAVE_MAPPER: Windows always routes this to whatever the
    // user has set as the default output device in Windows Settings.
    public const int DefaultDeviceNumber = -1;

    public int    Number { get; }
    public string Name   { get; }

    public AudioDevice(int number, string name)
    {
        Number = number;
        Name   = name;
    }

    public override string ToString() => Name;

    // Returns a list of every available output device, with "Default" first.
    // Keeping this here means MainWindow never needs a direct NAudio reference.
    public static List<AudioDevice> GetAll()
    {
        var list = new List<AudioDevice>();

        // The "Default" entry follows the Windows default device automatically.
        list.Add(new AudioDevice(DefaultDeviceNumber, "Default Output Device"));

        // WaveOut.DeviceCount and GetCapabilities() enumerate the real devices.
        for (int n = 0; n < WaveOut.DeviceCount; n++)
        {
            var caps = WaveOut.GetCapabilities(n);
            list.Add(new AudioDevice(n, caps.ProductName));
        }

        return list;
    }
}
