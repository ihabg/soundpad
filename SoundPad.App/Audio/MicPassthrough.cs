using NAudio.Wave;
using NAudio.Wave.SampleProviders;

namespace SoundPad.App.Audio;

// Captures audio from a microphone and feeds it into an AudioPlaybackEngine's mixer,
// so Discord (or anything listening to the virtual cable) hears both mic and SoundPad
// sounds at the same time.
//
// Audio pipeline:
//   WaveInEvent
//     → BufferedWaveProvider      (raw PCM bytes, bridges capture thread ↔ playback thread)
//     → Pcm16BitToSampleProvider  (converts 16-bit PCM to float — REQUIRED before anything else)
//     → WdlResamplingSampleProvider  (only added if mic sample rate ≠ mixer sample rate)
//     → MonoToStereoSampleProvider   (only added if mic is mono)
//     → VolumeSampleProvider      (scales float samples by the mic volume slider)
//     → engine mixer
//
// Why Pcm16BitToSampleProvider instead of WaveToSampleProvider?
//   WaveToSampleProvider is a pass-through wrapper for sources that are *already*
//   IEEE float.  It throws "Must be already floating point" when given PCM input.
//   Pcm16BitToSampleProvider is the correct NAudio converter for 16-bit PCM → float.
//   MonoToStereoSampleProvider and VolumeSampleProvider both require float input,
//   so PCM must be converted first.
public class MicPassthrough : IDisposable
{
    private WaveInEvent?          _waveIn;
    private BufferedWaveProvider? _buffer;
    private VolumeSampleProvider? _volumeProvider; // top of chain — added/removed from mixer
    private readonly AudioPlaybackEngine _engine;
    private bool _recording = false;

    // Volume multiplier applied to mic samples. 1.0 = 100%, 0.0 = muted.
    public float Volume { get; private set; } = 1.0f;

    public MicPassthrough(AudioPlaybackEngine engine)
    {
        _engine = engine;
    }

    // Opens the microphone and starts routing its audio into the engine's mixer.
    // Throws MmException if the device does not support 44100 Hz mono 16-bit PCM.
    public void Start(int micDeviceNumber)
    {
        Stop(); // clean up any previous session before starting a new one

        // Capture at 44100 Hz mono 16-bit PCM.
        // WaveFormat(sampleRate, channels) defaults to 16-bit PCM.
        // Windows ACM will convert from the hardware's native format if needed,
        // so the BufferedWaveProvider always receives data in exactly this format.
        var captureFormat = new WaveFormat(44100, 1);

        _waveIn = new WaveInEvent
        {
            DeviceNumber       = micDeviceNumber,
            WaveFormat         = captureFormat,
            BufferMilliseconds = 50  // 50 ms between DataAvailable callbacks — low latency
        };

        _buffer = new BufferedWaveProvider(captureFormat)
        {
            // Drop old samples if the buffer fills up rather than pausing playback.
            // This prevents latency from accumulating if the output thread falls behind.
            DiscardOnBufferOverflow = true
        };

        // Push captured bytes into the buffer on the capture thread.
        // Capture the local variable so the lambda doesn't hold a nullable field reference.
        var buffer = _buffer;
        _waveIn.DataAvailable += (_, e) => buffer.AddSamples(e.Buffer, 0, e.BytesRecorded);

        // ── Build the conversion chain ────────────────────────────────────────────
        //
        // Step 1 — PCM → float
        //   Pcm16BitToSampleProvider converts raw 16-bit integer samples to 32-bit float.
        //   This is the critical step: every later stage (resampling, stereo conversion,
        //   volume, and the mixer) requires float (IEEE 754) input and will throw or
        //   produce silence if given raw PCM bytes.
        //
        ISampleProvider chain = new Pcm16BitToSampleProvider(_buffer);

        // Step 2 — Resample if the mic sample rate does not match the mixer
        //   We asked for 44100 Hz above, so this is usually a no-op.
        //   The guard is a safety net in case Windows ACM gives us a different rate.
        if (chain.WaveFormat.SampleRate != CachedSound.TargetFormat.SampleRate)
        {
            chain = new WdlResamplingSampleProvider(chain, CachedSound.TargetFormat.SampleRate);
        }

        // Step 3 — Convert mono to stereo if needed
        //   The mixer is configured for stereo (CachedSound.TargetFormat has 2 channels).
        //   MonoToStereoSampleProvider duplicates the single channel into both L and R.
        //   It requires float input — that is already guaranteed by Step 1 above.
        if (chain.WaveFormat.Channels == 1)
        {
            chain = new MonoToStereoSampleProvider(chain);
        }

        // Step 4 — Volume control
        //   VolumeSampleProvider multiplies every float sample by Volume (0.0 – 1.0).
        //   It also requires float input.
        _volumeProvider = new VolumeSampleProvider(chain) { Volume = Volume };

        // Add to the mixer BEFORE StartRecording so there is no gap where captured
        // samples arrive but have nowhere to go.  If StartRecording fails we roll back.
        _engine.AddMixerInput(_volumeProvider);

        try
        {
            _waveIn.StartRecording();
            _recording = true;
        }
        catch
        {
            // StartRecording failed (device busy, unsupported format, permission denied…).
            // Roll back the mixer input we just added and release all resources.
            _engine.RemoveMixerInput(_volumeProvider);
            _volumeProvider = null;
            _waveIn.Dispose();
            _waveIn  = null;
            _buffer  = null;
            throw; // rethrow so the caller (MainWindow) can uncheck the box and show the error
        }
    }

    // Stops the microphone and removes its audio from the mixer.
    // Safe to call multiple times or before Start() has been called.
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
            // RemoveMixerInput is a no-op if the provider was never added, so this is safe
            // even when Stop() is called after a failed Start().
            _engine.RemoveMixerInput(_volumeProvider);
            _volumeProvider = null;
        }

        _buffer = null;
    }

    // Adjusts how loud the microphone sounds in the mix. 0.0–1.0.
    // Takes effect immediately, even while capturing.
    public void SetVolume(float volume)
    {
        Volume = Math.Clamp(volume, 0f, 1f);
        if (_volumeProvider is not null)
            _volumeProvider.Volume = Volume;
    }

    public void Dispose() => Stop();
}
