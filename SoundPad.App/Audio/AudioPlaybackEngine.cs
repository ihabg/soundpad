using NAudio.Wave;
using NAudio.Wave.SampleProviders;

namespace SoundPad.App.Audio;

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

    // Starts playing a cached sound immediately by adding a new provider
    // to the running mixer.  Previous sounds keep playing (they mix together).
    public void Play(CachedSound sound)
    {
        var provider = new CachedSoundSampleProvider(sound);
        _active.Add(provider);
        _mixer.AddMixerInput(provider);
    }

    // Overload that applies a per-sound volume (0.0 – 1.0).
    // When volume is effectively 1.0 no wrapper is created.
    public void Play(CachedSound sound, float volume)
    {
        ISampleProvider provider = new CachedSoundSampleProvider(sound);
        if (volume < 0.999f)
            provider = new VolumeSampleProvider(provider) { Volume = Math.Clamp(volume, 0f, 1f) };
        _active.Add(provider);
        _mixer.AddMixerInput(provider);
    }

    // Removes every active provider from the mixer, silencing all sounds.
    // Does NOT affect persistent inputs added via AddMixerInput (e.g. mic passthrough).
    public void StopAll()
    {
        foreach (var provider in _active)
            _mixer.RemoveMixerInput(provider);

        _active.Clear();
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
