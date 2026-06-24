using NAudio.Wave;

namespace SoundPad.App.Audio;

// Streams a CachedSound's float array to the mixer.
// Each instance has its own position so the same CachedSound can be played
// multiple times simultaneously without interference.
//
// Two modes:
//   Single-segment (original): one contiguous [startSample, endSample) window
//     with optional fade-in / fade-out ramps.
//   Multi-segment: a list of (S, E) pairs — used when the sound has cut regions.
//     FadeIn/FadeOut apply to the overall joined output (first and last segment
//     edges only), not to each internal segment boundary.
public class CachedSoundSampleProvider : ISampleProvider
{
    private readonly CachedSound _sound;

    // ── Single-segment fields ─────────────────────────────────────────────────
    private readonly int _startSample;
    private readonly int _endSample;
    private readonly int _fadeInSamples;
    private readonly int _fadeOutSamples;

    // ── Multi-segment fields ──────────────────────────────────────────────────
    private readonly IReadOnlyList<(int S, int E)>? _segments;
    private int _segIdx;        // which segment we are currently reading
    private int _segPos;        // position within current segment
    private int _totalPlayed;   // samples emitted so far across all segments (for fade)
    private int _totalLength;   // sum of all segment lengths (for fade-out offset)

    // ── Shared ───────────────────────────────────────────────────────────────
    private int _position; // used only in single-segment mode

    public WaveFormat WaveFormat => _sound.WaveFormat;

    public bool IsFinished => _segments is not null
        ? _segIdx >= _segments.Count
        : _position >= _endSample;

    // ── Single-segment constructor (original — unchanged behavior) ────────────
    //
    // startSample    — index into AudioData to begin playback (0 = play from start).
    // endSample      — index into AudioData to stop at (-1 = play to end of file).
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

    // ── Multi-segment constructor ─────────────────────────────────────────────
    //
    // segments       — ordered list of (S, E) sample-index pairs; must be
    //                  non-overlapping and ascending. Empty list = silence.
    // fadeInSamples  — ramps 0→1 over this many samples at the very start.
    // fadeOutSamples — ramps 1→0 over this many samples at the very end.
    public CachedSoundSampleProvider(CachedSound sound,
        IReadOnlyList<(int S, int E)> segments,
        int fadeInSamples  = 0,
        int fadeOutSamples = 0)
    {
        _sound    = sound;
        _segments = segments;
        _segIdx   = 0;
        _segPos   = segments.Count > 0 ? segments[0].S : 0;

        _totalLength = segments.Sum(seg => seg.E - seg.S);
        _fadeInSamples  = Math.Clamp(fadeInSamples,  0, _totalLength);
        _fadeOutSamples = Math.Clamp(fadeOutSamples, 0, _totalLength);

        // Single-segment fields unused in this mode — zero them.
        _startSample = 0;
        _endSample   = 0;
    }

    // ── Read ─────────────────────────────────────────────────────────────────

    // NAudio calls this on the audio thread. Returning fewer samples than
    // requested (including 0) signals the mixer that this source is finished.
    public int Read(float[] buffer, int offset, int count)
    {
        return _segments is not null
            ? ReadMultiSegment(buffer, offset, count)
            : ReadSingleSegment(buffer, offset, count);
    }

    private int ReadSingleSegment(float[] buffer, int offset, int count)
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
                int fromStart = (_position - _startSample) + i;
                float gain = 1f;

                if (_fadeInSamples > 0 && fromStart < _fadeInSamples)
                    gain *= (float)fromStart / _fadeInSamples;

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

    private int ReadMultiSegment(float[] buffer, int offset, int count)
    {
        int written = 0;

        while (written < count && _segIdx < _segments!.Count)
        {
            var (segS, segE) = _segments[_segIdx];
            int segAvailable = segE - _segPos;

            if (segAvailable <= 0)
            {
                // Advance to next segment.
                _segIdx++;
                if (_segIdx < _segments.Count)
                    _segPos = _segments[_segIdx].S;
                continue;
            }

            int toRead = Math.Min(segAvailable, count - written);
            Array.Copy(_sound.AudioData, _segPos, buffer, offset + written, toRead);

            // Apply fade gains over the overall output position.
            if (_fadeInSamples > 0 || _fadeOutSamples > 0)
            {
                for (int i = 0; i < toRead; i++)
                {
                    int outPos = _totalPlayed + i;
                    float gain = 1f;

                    if (_fadeInSamples > 0 && outPos < _fadeInSamples)
                        gain *= (float)outPos / _fadeInSamples;

                    if (_fadeOutSamples > 0)
                    {
                        int fromEnd = _totalLength - outPos;
                        if (fromEnd < _fadeOutSamples)
                            gain *= Math.Max(0f, (float)fromEnd / _fadeOutSamples);
                    }

                    buffer[offset + written + i] *= gain;
                }
            }

            _segPos      += toRead;
            _totalPlayed += toRead;
            written      += toRead;

            // If we finished this segment, advance.
            if (_segPos >= segE)
            {
                _segIdx++;
                if (_segIdx < _segments.Count)
                    _segPos = _segments[_segIdx].S;
            }
        }

        return written;
    }
}
