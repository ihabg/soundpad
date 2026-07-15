using NAudio.Wave;

namespace SoundPad.App.Audio;



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
