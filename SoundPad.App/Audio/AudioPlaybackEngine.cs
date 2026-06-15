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
    // MixingSampleProvider auto-removes providers that finish naturally,
    // so calling RemoveMixerInput on an already-finished one is harmless.
    private readonly List<CachedSoundSampleProvider> _active = new();

    // deviceNumber: which Windows output device to use.
    //   -1  = WAVE_MAPPER (always follows the Windows default device)
    //    0+ = specific device index from WaveOut.GetCapabilities()
    public AudioPlaybackEngine(int deviceNumber = AudioDevice.DefaultDeviceNumber)
    {
        // ReadFully = true makes the mixer output silence when no sounds
        // are active, which keeps the output device from stopping on its own.
        _mixer = new MixingSampleProvider(CachedSound.TargetFormat)
        {
            ReadFully = true
        };

        // DeviceNumber must be set before Init() is called.
        _outputDevice = new WaveOutEvent { DeviceNumber = deviceNumber };
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

    // Removes every active provider from the mixer, silencing all sounds.
    public void StopAll()
    {
        foreach (var provider in _active)
            _mixer.RemoveMixerInput(provider);

        _active.Clear();
    }

    public void Dispose()
    {
        _outputDevice.Stop();
        _outputDevice.Dispose();
    }
}
