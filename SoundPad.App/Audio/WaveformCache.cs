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

        
        
        // Keep the source alive while computing bounded waveform blocks.

        _ = Task.Run(() =>
        {
            float[]? result = null;
            try   { result = cachedSound.BuildPeaks(PeakCount); }
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

    
}
