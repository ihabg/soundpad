using NAudio.Wave;

namespace SoundPad.App.Audio;

// Streams a CachedSound's float array to the mixer.
// Each instance has its own _position, so the same CachedSound can
// have multiple providers playing at the same time independently.
public class CachedSoundSampleProvider : ISampleProvider
{
    private readonly CachedSound _sound;
    private int _position;

    public WaveFormat WaveFormat => _sound.WaveFormat;

    public bool IsFinished => _position >= _sound.AudioData.Length;

    public CachedSoundSampleProvider(CachedSound sound)
    {
        _sound    = sound;
        _position = 0;
    }

    // NAudio calls this on the audio thread to fill the next output buffer.
    // Returning fewer samples than requested (including 0) tells the mixer
    // that this source has finished and it will remove it automatically.
    public int Read(float[] buffer, int offset, int count)
    {
        int available = _sound.AudioData.Length - _position;
        int toRead    = Math.Min(available, count);

        if (toRead > 0)
            Array.Copy(_sound.AudioData, _position, buffer, offset, toRead);

        _position += toRead;
        return toRead;
    }
}
