using NAudio.Wave;
using NAudio.Wave.SampleProviders;

namespace SoundPad.App.Audio;



public sealed record PlaybackHandle(
    ISampleProvider           TopProvider,
    CachedSoundSampleProvider SourceProvider)
{
    public bool IsFinished => SourceProvider.IsFinished;
}





public class AudioPlaybackEngine : IDisposable
{
    private readonly WaveOutEvent          _outputDevice;
    private readonly MixingSampleProvider  _mixer;

    
    
    
    
    private readonly List<ISampleProvider> _active = new List<ISampleProvider>();

    
    
    
    
    
    
    
    
    public AudioPlaybackEngine(int deviceNumber    = AudioDevice.DefaultDeviceNumber,
                                int desiredLatency  = 300,
                                int numberOfBuffers = 3)
    {
        
        
        _mixer = new MixingSampleProvider(CachedSound.TargetFormat)
        {
            ReadFully = true
        };

        
        _outputDevice = new WaveOutEvent
        {
            DeviceNumber    = deviceNumber,
            DesiredLatency  = desiredLatency,
            NumberOfBuffers = numberOfBuffers
        };
        _outputDevice.Init(_mixer);
        _outputDevice.Play(); 
    }

    
    
    public PlaybackHandle Play(CachedSound sound)
    {
        var source = new CachedSoundSampleProvider(sound);
        _active.Add(source);
        _mixer.AddMixerInput(source);
        return new PlaybackHandle(source, source);
    }

    
    
    
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

    
    
    
    public PlaybackHandle Play(CachedSound sound, float volume,
        IReadOnlyList<(int S, int E)> segments, int fadeInSamples, int fadeOutSamples)
    {
        var source = new CachedSoundSampleProvider(sound, segments, fadeInSamples, fadeOutSamples);
        ISampleProvider top = source;
        if (volume < 0.999f)
            top = new VolumeSampleProvider(source) { Volume = Math.Clamp(volume, 0f, 1f) };
        _active.Add(top);
        _mixer.AddMixerInput(top);
        return new PlaybackHandle(top, source);
    }

    
    
    public void StopAll()
    {
        foreach (var provider in _active)
            _mixer.RemoveMixerInput(provider);

        _active.Clear();
    }

    
    
    
    public void StopOne(ISampleProvider topProvider)
    {
        if (_active.Remove(topProvider))
            _mixer.RemoveMixerInput(topProvider);
    }

    
    
    
    public void AddMixerInput(ISampleProvider provider)
    {
        _mixer.AddMixerInput(provider);
    }

    
    
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
