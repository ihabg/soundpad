using NAudio.Wave;
using NAudio.Wave.SampleProviders;

namespace SoundPad.App.Audio;

// Decodes one sound file to a plain float array that lives in RAM.
// The file is opened exactly once (in the constructor); after that
// the disk is never touched again for this sound.
public class CachedSound
{
    // All sounds are decoded to this format so they can all feed the
    // same mixer without format-mismatch errors.
    public static readonly WaveFormat TargetFormat =
        WaveFormat.CreateIeeeFloatWaveFormat(44100, 2);

    public float[]    AudioData  { get; }
    public WaveFormat WaveFormat => TargetFormat;

    public CachedSound(string filePath)
    {
        using var reader = new AudioFileReader(filePath);

        // AudioFileReader gives us IEEE float samples, but the sample rate
        // and channel count depend on the source file.  We normalise both:

        ISampleProvider provider = reader;

        // Step 1 — if the file is mono, duplicate it to stereo.
        if (reader.WaveFormat.Channels == 1)
            provider = new MonoToStereoSampleProvider(provider);

        // Step 2 — if the sample rate is not 44100 Hz, resample it.
        if (reader.WaveFormat.SampleRate != 44100)
            provider = new WdlResamplingSampleProvider(provider, 44100);

        // Step 3 — read every decoded sample into a List, then freeze as an array.
        // Buffer size = 1 second of stereo audio at 44100 Hz = 88 200 floats.
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
}
