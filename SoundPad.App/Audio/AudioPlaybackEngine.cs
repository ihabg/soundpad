using NAudio.Wave;
using NAudio.Wave.SampleProviders;

namespace SoundPad.App.Audio;

// Returned by AudioPlaybackEngine.Play() so callers can check completion
// and stop individual sounds without touching mic passthrough.
public sealed record PlaybackHandle(
    ISampleProvider           TopProvider,
    CachedSoundSampleProvider SourceProvider)
{
    public bool IsFinished => SourceProvider.IsFinished;
}

// Owns a single audio output device (WaveOutEvent) and a mixer
// (MixingSampleProvider).  The output device is started once in the
// constructor and runs continuously, outputting silence when idle.
// Sounds are played by adding a new provider to the mixer.
public class AudioPlaybackEngine : IDisposable
{
    private readonly WaveOutEvent          _outputDevice;
    private readonly MixingSampleProvider  _mixer;

    // Tracks every provider we added so StopAll() can remove them.
    // Stored as ISampleProvider so both plain and volume-wrapped providers fit.
    // MixingSampleProvider auto-removes providers that finish naturally,
    // so calling RemoveMixerInput on an already-finished one is harmless.
    private readonly List<ISampleProvider> _active = new List<ISampleProvider>();

    // deviceNumber:     which Windows output device to use.
    //   -1  = WAVE_MAPPER (always follows the Windows default device)
    //    0+ = specific device index from WaveOut.GetCapabilities()
    // desiredLatency:   total ring-buffer size in milliseconds (default = 300).
    //                   Must be set before Init(); cannot be changed on a live device.
    // numberOfBuffers:  how many sub-buffers to divide desiredLatency into (default = 3).
    //                   Per-buffer time = desiredLatency / numberOfBuffers; smaller
    //                   values reduce start-latency but increase the risk of underruns.
    public AudioPlaybackEngine(int deviceNumber    = AudioDevice.DefaultDeviceNumber,
                                int desiredLatency  = 300,
                                int numberOfBuffers = 3)
    {
        // ReadFully = true makes the mixer output silence when no sounds
        // are active, which keeps the output device from stopping on its own.
        _mixer = new MixingSampleProvider(CachedSound.TargetFormat)
        {
            ReadFully = true
        };

        // All three properties must be set before Init() is called.
        _outputDevice = new WaveOutEvent
        {
            DeviceNumber    = deviceNumber,
            DesiredLatency  = desiredLatency,
            NumberOfBuffers = numberOfBuffers
        };
        _outputDevice.Init(_mixer);
        _outputDevice.Play(); // starts once; stays running until Dispose()
    }

    // Starts playing a cached sound and returns a handle for tracking/stopping it.
    // Previous sounds keep playing (they mix together).
    public PlaybackHandle Play(CachedSound sound)
    {
        var source = new CachedSoundSampleProvider(sound);
        _active.Add(source);
        _mixer.AddMixerInput(source);
        return new PlaybackHandle(source, source);
    }

    // Overload that applies a per-sound volume (0.0 – 1.0).
    // When volume is effectively 1.0 no wrapper is created.
    // Returns a handle whose TopProvider is what's in _active (may be volume-wrapped).
    public PlaybackHandle Play(CachedSound sound, float volume)
    {
        var source = new CachedSoundSampleProvider(sound);
        ISampleProvider top = source;
        if (volume < 0.999f)
            top = new VolumeSampleProvider(source) { Volume = Math.Clamp(volume, 0f, 1f) };
        _active.Add(top);
        _mixer.AddMixerInput(top);
        return new PlaybackHandle(top, source);
    }

    // Overload with non-destructive trim and fade applied at playback time.
    // Sample counts must use the sound's own SampleRate/Channels values.
    // startSample / endSample define the audible region of AudioData.
    // fadeInSamples / fadeOutSamples are linear ramp lengths inside that region.
    public PlaybackHandle Play(CachedSound sound, float volume,
        int startSample, int endSample, int fadeInSamples, int fadeOutSamples)
    {
        var source = new CachedSoundSampleProvider(
            sound, startSample, endSample, fadeInSamples, fadeOutSamples);
        ISampleProvider top = source;
        if (volume < 0.999f)
            top = new VolumeSampleProvider(source) { Volume = Math.Clamp(volume, 0f, 1f) };
        _active.Add(top);
        _mixer.AddMixerInput(top);
        return new PlaybackHandle(top, source);
    }

    // Removes every active provider from the mixer, silencing all sounds.
    // Does NOT affect persistent inputs added via AddMixerInput (e.g. mic passthrough).
    public void StopAll()
    {
        foreach (var provider in _active)
            _mixer.RemoveMixerInput(provider);

        _active.Clear();
    }

    // Stops exactly one sound by its top-level provider.
    // Safe to call if the provider already finished naturally (both Remove calls are no-ops).
    // Mic passthrough uses AddMixerInput (not _active), so it is never reachable here.
    public void StopOne(ISampleProvider topProvider)
    {
        if (_active.Remove(topProvider))
            _mixer.RemoveMixerInput(topProvider);
    }

    // Adds a persistent ISampleProvider directly to the mixer, outside of the
    // _active tracking list.  Used by MicPassthrough so StopAll() does not
    // silence the microphone feed.
    public void AddMixerInput(ISampleProvider provider)
    {
        _mixer.AddMixerInput(provider);
    }

    // Removes a previously added persistent input.
    // Safe to call even if the provider was never added (List.Remove is a no-op).
    public void RemoveMixerInput(ISampleProvider provider)
    {
        _mixer.RemoveMixerInput(provider);
    }

    public void Dispose()
    {
        _outputDevice.Stop();
        _outputDevice.Dispose();
    }
}
