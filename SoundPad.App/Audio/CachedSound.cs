using NAudio.Wave;
using NAudio.Wave.SampleProviders;
using System.IO;

namespace SoundPad.App.Audio;




public class CachedSound
{
    
    
    
    public static readonly WaveFormat TargetFormat =
        WaveFormat.CreateIeeeFloatWaveFormat(48000, 2);

    public float[]    AudioData  { get; }
    public WaveFormat WaveFormat => TargetFormat;

    
    
    
    public int      SampleRate   => WaveFormat.SampleRate;
    public int      Channels     => WaveFormat.Channels;
    public int      TotalSamples => AudioData.Length;
    public int      TotalFrames  => AudioData.Length / WaveFormat.Channels;
    public TimeSpan Duration     => TimeSpan.FromSeconds(
        (double)AudioData.Length / ((double)WaveFormat.SampleRate * WaveFormat.Channels));

    
    
    
    
    private const long MaxSourceFileSizeBytes = 200L * 1024 * 1024;

    public CachedSound(string filePath)
    {
        var fileSize = new FileInfo(filePath).Length;
        if (fileSize > MaxSourceFileSizeBytes)
            throw new InvalidOperationException(
                $"Audio file is too large ({fileSize / (1024 * 1024)} MB). " +
                $"Limit is {MaxSourceFileSizeBytes / (1024 * 1024)} MB. " +
                "Trim the file to a shorter clip before importing.");

        using var reader = new AudioFileReader(filePath);

        
        

        ISampleProvider provider = reader;

        
        if (reader.WaveFormat.Channels == 1)
            provider = new MonoToStereoSampleProvider(provider);

        
        if (reader.WaveFormat.SampleRate != TargetFormat.SampleRate)
            provider = new WdlResamplingSampleProvider(provider, TargetFormat.SampleRate);

        
        
        var samples = new List<float>();
        var buffer  = new float[TargetFormat.SampleRate * TargetFormat.Channels];
        int read;

        while ((read = provider.Read(buffer, 0, buffer.Length)) > 0)
        {
            for (int i = 0; i < read; i++)
                samples.Add(buffer[i]);
        }

        AudioData = samples.ToArray();
    }

    
    
    
    public CachedSound(float[] processedData)
    {
        AudioData = processedData;
    }
}
