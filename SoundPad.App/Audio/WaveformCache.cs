using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using System.Windows;

namespace SoundPad.App.Audio;

// Caches downsampled peak arrays built from CachedSound.AudioData so the Sound
// Composer can render waveform previews without re-decoding audio on every draw.
//
// Peaks are generated once per SoundItem on a background thread.  The first
// Request call for a given item returns null and starts the build; subsequent
// calls return the cached array.  onReady is invoked on the UI thread after
// each build completes so the caller can redraw.
internal sealed class WaveformCache
{
    // Number of peaks stored per sound. 2000 gives good detail at max zoom
    // (400 px/s × 60 s = 24000 px) while staying low memory.
    private const int PeakCount = 2000;

    // null value = build failed; don't retry
    private readonly Dictionary<Guid, float[]?> _peaks   = new();
    private readonly HashSet<Guid>               _loading = new();
    private readonly Action                      _onReady;

    internal WaveformCache(Action onReady) => _onReady = onReady;

    // Returns the cached peak array, or null if it is not yet available.
    // Triggers an async build on the first call for a given sound.
    // Thread-safe to call from the UI thread only.
    internal float[]? Request(Guid soundItemId, CachedSound? cachedSound)
    {
        if (_peaks.TryGetValue(soundItemId, out var cached)) return cached;

        if (cachedSound is null)
        {
            _peaks[soundItemId] = null;   // nothing to decode
            return null;
        }
        if (_loading.Contains(soundItemId)) return null;

        _loading.Add(soundItemId);

        // Capture data references; do not capture the CachedSound object
        // itself to avoid holding a GC root across the thread boundary.
        float[] audioData = cachedSound.AudioData;
        int     channels  = cachedSound.Channels;

        _ = Task.Run(() =>
        {
            float[]? result = null;
            try   { result = BuildPeaks(audioData, channels, PeakCount); }
            catch { /* leave null — waveform simply won't be drawn */ }

            Application.Current?.Dispatcher.BeginInvoke(() =>
            {
                _peaks[soundItemId] = result;
                _loading.Remove(soundItemId);
                _onReady();
            });
        });

        return null;
    }

    // Downsamples interleaved float audio into numPeaks max-absolute values.
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
