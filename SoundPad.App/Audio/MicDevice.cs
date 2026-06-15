using NAudio.Wave;

namespace SoundPad.App.Audio;

// Represents one Windows audio input device (microphone) shown in a ComboBox.
// WPF calls ToString() on each item to display it, so we override it.
public class MicDevice
{
    public int    Number { get; }
    public string Name   { get; }

    public MicDevice(int number, string name)
    {
        Number = number;
        Name   = name;
    }

    public override string ToString() => Name;

    // Returns all available microphone/recording devices.
    // Device indices here are WaveIn device numbers (0, 1, 2, ...).
    public static List<MicDevice> GetAll()
    {
        var list = new List<MicDevice>();

        for (int n = 0; n < WaveIn.DeviceCount; n++)
        {
            var caps = WaveIn.GetCapabilities(n);
            list.Add(new MicDevice(n, caps.ProductName));
        }

        return list;
    }
}
