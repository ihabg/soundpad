using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using System.Windows;

namespace SoundPad.App.Audio;








internal sealed class WaveformCache
{
    
    
    private const int PeakCount = 2000;

    
    private readonly Dictionary<Guid, float[]?> _peaks   = new();
    private readonly HashSet<Guid>               _loading = new();
    private readonly Action                      _onReady;

    internal WaveformCache(Action onReady) => _onReady = onReady;

    
    
    
    internal float[]? Request(Guid soundItemId, CachedSound? cachedSound)
    {
        if (_peaks.TryGetValue(soundItemId, out var cached)) return cached;

        if (cachedSound is null)
        {
            _peaks[soundItemId] = null;   
            return null;
        }
        if (_loading.Contains(soundItemId)) return null;

        _loading.Add(soundItemId);

        
        
        float[] audioData = cachedSound.AudioData;
        int     channels  = cachedSound.Channels;

        _ = Task.Run(() =>
        {
            float[]? result = null;
            try   { result = BuildPeaks(audioData, channels, PeakCount); }
            catch {  }

            Application.Current?.Dispatcher.BeginInvoke(() =>
            {
                _peaks[soundItemId] = result;
                _loading.Remove(soundItemId);
                _onReady();
            });
        });

        return null;
    }

    
    private static float[] BuildPeaks(float[] audioData, int channels, int numPeaks)
    {
        int ch     = Math.Max(1, channels);
        int frames = audioData.Length / ch;
        if (frames <= 0) return Array.Empty<float>();

        var    peaks  = new float[numPeaks];
        double stride = (double)frames / numPeaks;

        for (int p = 0; p < numPeaks; p++)
        {
            int fStart = (int)(p       * stride);
            int fEnd   = (int)((p + 1) * stride);
            if (fEnd > frames) fEnd = frames;

            float peak = 0f;
            for (int f = fStart; f < fEnd; f++)
            {
                int baseIdx = f * ch;
                for (int c = 0; c < ch; c++)
                {
                    int i = baseIdx + c;
                    if ((uint)i < (uint)audioData.Length)
                        peak = Math.Max(peak, MathF.Abs(audioData[i]));
                }
            }
            peaks[p] = peak;
        }

        return peaks;
    }
}
