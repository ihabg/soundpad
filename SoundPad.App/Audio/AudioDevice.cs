using NAudio.Wave;

namespace SoundPad.App.Audio;



public class AudioDevice
{
    
    public const int DefaultDeviceNumber = -1;

    
    
    public const int NoneDeviceNumber = int.MinValue;

    public int    Number { get; }
    public string Name   { get; }

    
    public bool IsNone => Number == NoneDeviceNumber;

    
    public static readonly AudioDevice None = new AudioDevice(NoneDeviceNumber, "None");

    public AudioDevice(int number, string name)
    {
        Number = number;
        Name   = name;
    }

    public override string ToString() => Name;

    
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

    
    public static List<AudioDevice> GetAllWithNone()
    {
        var list = new List<AudioDevice>();

        list.Add(None);          
        list.AddRange(GetAll()); 

        return list;
    }

    
    
    
    
    
    
    
    
    
    
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
            
        }

        return DefaultDeviceNumber; 
    }

    
    
    
    
    
    public static bool AreSameOutputDevice(AudioDevice? monitor, AudioDevice? virtualOut)
    {
        if (monitor is null || virtualOut is null || virtualOut.IsNone)
            return false;

        
        int monitorIndex = monitor.Number == DefaultDeviceNumber
            ? ResolveDefaultDeviceNumber()
            : monitor.Number;

        int virtualIndex = virtualOut.Number == DefaultDeviceNumber
            ? ResolveDefaultDeviceNumber()
            : virtualOut.Number;

        return monitorIndex == virtualIndex;
    }
}
