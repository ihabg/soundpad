using NAudio.Wave;

namespace SoundPad.App.Audio;











public class CachedSoundSampleProvider : ISampleProvider
{
    private readonly CachedSound _sound;

    
    private readonly int _startSample;
    private readonly int _endSample;
    private readonly int _fadeInSamples;
    private readonly int _fadeOutSamples;

    
    private readonly IReadOnlyList<(int S, int E)>? _segments;
    private int _segIdx;        
    private int _segPos;        
    private int _totalPlayed;   
    private int _totalLength;   

    
    private int _position; 

    public WaveFormat WaveFormat => _sound.WaveFormat;

    public bool IsFinished => _segments is not null
        ? _segIdx >= _segments.Count
        : _position >= _endSample;

    
    
    
    
    
    
    
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

        
        _startSample = 0;
        _endSample   = 0;
    }

    

    
    
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
                
                _segIdx++;
                if (_segIdx < _segments.Count)
                    _segPos = _segments[_segIdx].S;
                continue;
            }

            int toRead = Math.Min(segAvailable, count - written);
            Array.Copy(_sound.AudioData, _segPos, buffer, offset + written, toRead);

            
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
