using NAudio.Wave;
using NAudio.Wave.SampleProviders;

namespace SoundPad.App.Audio;

// Decodes one sound file to a plain float array that lives in RAM.
// The file is opened exactly once (in the constructor); after that
// the disk is never touched again for this sound.
public class CachedSound
{
    // All sounds are decoded to this format so they can all feed the same mixer
    // without format-mismatch errors.  48 kHz matches VB-CABLE's and Discord's
    // native rate, so the mic passthrough chain needs no resampling step.
    public static readonly WaveFormat TargetFormat =
        WaveFormat.CreateIeeeFloatWaveFormat(48000, 2);

    public float[]    AudioData  { get; }
    public WaveFormat WaveFormat => TargetFormat;

    // Format-derived helpers used by trim/fade calculations.
    // All values come from the actual decoded WaveFormat rather than
    // hard-coded constants so they stay correct if TargetFormat ever changes.
    public int      SampleRate   => WaveFormat.SampleRate;
    public int      Channels     => WaveFormat.Channels;
    public int      TotalSamples => AudioData.Length;
    public int      TotalFrames  => AudioData.Length / WaveFormat.Channels;
    public TimeSpan Duration     => TimeSpan.FromSeconds(
        (double)AudioData.Length / ((double)WaveFormat.SampleRate * WaveFormat.Channels));

    public CachedSound(string filePath)
    {
        using var reader = new AudioFileReader(filePath);

        // AudioFileReader gives us IEEE float samples, but the sample rate
        // and channel count depend on the source file.  We normalise both:

        ISampleProvider provider = reader;

        // Step 1 — if the file is mono, duplicate it to stereo.
        if (reader.WaveFormat.Channels == 1)
            provider = new MonoToStereoSampleProvider(provider);

        // Step 2 — resample to the target rate if the source file differs.
        if (reader.WaveFormat.SampleRate != TargetFormat.SampleRate)
            provider = new WdlResamplingSampleProvider(provider, TargetFormat.SampleRate);

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

    // Wraps a pre-computed float[] without file I/O.
    // data must already be in TargetFormat (48 kHz, stereo, IEEE float).
    // Used by EffectProcessor to wrap rendered audio for playback and export.
    public CachedSound(float[] processedData)
    {
        AudioData = processedData;
    }
}
