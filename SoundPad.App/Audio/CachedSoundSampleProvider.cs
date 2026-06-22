using NAudio.Wave;

namespace SoundPad.App.Audio;

// Streams a CachedSound's float array to the mixer.
// Each instance has its own _position so the same CachedSound can be
// played multiple times simultaneously without interference.
//
// Optional trim: only the samples in [startSample, endSample) are played.
// Optional fade: linear gain ramps applied from the start and/or end of
//   the trimmed region; all arithmetic uses the sound's actual format values.
//
// When startSample/endSample/fade params are omitted the behaviour is
// identical to v1.1.0 — the full array is streamed at full gain.
public class CachedSoundSampleProvider : ISampleProvider
{
    private readonly CachedSound _sound;
    private readonly int         _startSample;
    private readonly int         _endSample;
    private readonly int         _fadeInSamples;
    private readonly int         _fadeOutSamples;
    private int                  _position;

    public WaveFormat WaveFormat => _sound.WaveFormat;

    public bool IsFinished => _position >= _endSample;

    // startSample  — index into AudioData to begin playback (0 = play from start).
    // endSample    — index into AudioData to stop at (-1 = play to end of file).
    // fadeInSamples  — number of samples over which gain ramps from 0 → 1.
    // fadeOutSamples — number of samples over which gain ramps from 1 → 0 at the end.
    // All values are clamped to valid ranges in the constructor.
    public CachedSoundSampleProvider(CachedSound sound,
        int startSample    = 0,
        int endSample      = -1,
        int fadeInSamples  = 0,
        int fadeOutSamples = 0)
    {
        _sound       = sound;
        _startSample = Math.Clamp(startSample, 0, sound.TotalSamples);
        _endSample   = endSample < 0
            ? sound.TotalSamples
            : Math.Clamp(endSample, _startSample, sound.TotalSamples);

        int trimLen      = _endSample - _startSample;
        _fadeInSamples   = Math.Clamp(fadeInSamples,  0, trimLen);
        _fadeOutSamples  = Math.Clamp(fadeOutSamples, 0, trimLen);
        _position        = _startSample;
    }

    // NAudio calls this on the audio thread.  Returning fewer samples than
    // requested (including 0) signals the mixer that this source is finished.
    public int Read(float[] buffer, int offset, int count)
    {
        int available = _endSample - _position;
        int toRead    = Math.Min(available, count);

        if (toRead <= 0)
            return 0;

        Array.Copy(_sound.AudioData, _position, buffer, offset, toRead);

        if (_fadeInSamples > 0 || _fadeOutSamples > 0)
        {
            int trimLen = _endSample - _startSample;

            for (int i = 0; i < toRead; i++)
            {
                // Distance (in interleaved samples) from the start of the
                // audible region — not from position 0 of the raw array.
                int fromStart = (_position - _startSample) + i;
                float gain = 1f;

                // Fade in: ramp from 0 → 1 over the first _fadeInSamples.
                if (_fadeInSamples > 0 && fromStart < _fadeInSamples)
                    gain *= (float)fromStart / _fadeInSamples;

                // Fade out: ramp from 1 → 0 over the last _fadeOutSamples.
                if (_fadeOutSamples > 0)
                {
                    int fromEnd = trimLen - fromStart;
                    if (fromEnd < _fadeOutSamples)
                        gain *= Math.Max(0f, (float)fromEnd / _fadeOutSamples);
                }

                buffer[offset + i] *= gain;
            }
        }

        _position += toRead;
        return toRead;
    }
}
