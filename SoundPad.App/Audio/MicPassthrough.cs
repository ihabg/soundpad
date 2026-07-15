using NAudio.Wave;
using NAudio.Wave.SampleProviders;

namespace SoundPad.App.Audio;




















public class MicPassthrough : IDisposable
{
    
    
    
    private const int MicBufferMilliseconds = 20;

    
    
    private const int MicNumberOfBuffers = 2;

    private WaveInEvent?          _waveIn;
    private BufferedWaveProvider? _buffer;
    private VolumeSampleProvider? _volumeProvider; 
    private readonly AudioPlaybackEngine _engine;
    private bool _recording = false;

    
    public float Volume { get; private set; } = 1.0f;

    public MicPassthrough(AudioPlaybackEngine engine)
    {
        _engine = engine;
    }

    
    
    public void Start(int micDeviceNumber)
    {
        Stop(); 

        
        
        
        
        var captureFormat = new WaveFormat(CachedSound.TargetFormat.SampleRate, 1);

        _waveIn = new WaveInEvent
        {
            DeviceNumber       = micDeviceNumber,
            WaveFormat         = captureFormat,
            BufferMilliseconds = MicBufferMilliseconds,
            NumberOfBuffers    = MicNumberOfBuffers
        };

        _buffer = new BufferedWaveProvider(captureFormat)
        {
            
            
            DiscardOnBufferOverflow = true
        };

        
        
        var buffer = _buffer;
        _waveIn.DataAvailable += (_, e) => buffer.AddSamples(e.Buffer, 0, e.BytesRecorded);

        
        
        
        
        
        
        
        
        ISampleProvider chain = new Pcm16BitToSampleProvider(_buffer);

        
        
        
        if (chain.WaveFormat.SampleRate != CachedSound.TargetFormat.SampleRate)
        {
            chain = new WdlResamplingSampleProvider(chain, CachedSound.TargetFormat.SampleRate);
        }

        
        
        
        
        if (chain.WaveFormat.Channels == 1)
        {
            chain = new MonoToStereoSampleProvider(chain);
        }

        
        
        
        _volumeProvider = new VolumeSampleProvider(chain) { Volume = Volume };

        
        
        _engine.AddMixerInput(_volumeProvider);

        try
        {
            _waveIn.StartRecording();
            _recording = true;
        }
        catch
        {
            
            
            _engine.RemoveMixerInput(_volumeProvider);
            _volumeProvider = null;
            _waveIn.Dispose();
            _waveIn  = null;
            _buffer  = null;
            throw; 
        }
    }

    
    
    public void Stop()
    {
        if (_recording)
        {
            try { _waveIn?.StopRecording(); } catch { }
            _recording = false;
        }

        if (_waveIn is not null)
        {
            try { _waveIn.Dispose(); } catch { }
            _waveIn = null;
        }

        if (_volumeProvider is not null)
        {
            
            
            _engine.RemoveMixerInput(_volumeProvider);
            _volumeProvider = null;
        }

        _buffer = null;
    }

    
    
    public void SetVolume(float volume)
    {
        Volume = Math.Clamp(volume, 0f, 1f);
        if (_volumeProvider is not null)
            _volumeProvider.Volume = Volume;
    }

    public void Dispose() => Stop();
}
