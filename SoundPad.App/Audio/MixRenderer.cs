using System;
using System.Collections.Generic;
using System.Linq;
using SoundPad.App.Models;
using SoundPad.App.Services;

namespace SoundPad.App.Audio;

public static class MixRenderer
{
    
    private const int MaxMixSamples = 48000 * 2 * 600;

    
    
    
    
    
    
    
    
    
    
    public static float[] Render(
        IReadOnlyList<MixTrack>                tracks,
        IReadOnlyDictionary<Guid, SoundItem>   soundItems,
        IReadOnlyDictionary<Guid, CachedSound> cachedSounds)
    {
        
        int sr = CachedSound.TargetFormat.SampleRate * CachedSound.TargetFormat.Channels;

        var trackData = new List<(int Offset, float[] Samples)>();
        int mixLength = 0;

        foreach (var track in tracks)
        {
            float trackGain = track.Volume * track.Volume; 

            foreach (var clip in track.Clips)
            {
                if (!soundItems.TryGetValue(clip.SoundItemId, out var item))    continue;
                if (!cachedSounds.TryGetValue(clip.SoundItemId, out var sound)) continue;

                
                var segments = GetSegmentsForItem(sound, item);
                float[] rendered;
                try
                {
                    rendered = EffectProcessor.Render(
                        sound, segments,
                        item.ReverseAudio, item.NormalizeAudio,
                        item.PlaybackSpeed);
                }
                catch (Exception ex)
                {
                    AppLogger.Error("MixRenderer",
                        $"Clip render failed for '{item.DisplayName}'", ex);
                    continue;
                }

                if (rendered.Length == 0) continue;

                
                int localStart = clip.LocalTrimStartSeconds.HasValue
                    ? Math.Clamp((int)(clip.LocalTrimStartSeconds.Value * sr), 0, rendered.Length)
                    : 0;
                int localEnd = clip.LocalTrimEndSeconds.HasValue
                    ? Math.Clamp((int)(clip.LocalTrimEndSeconds.Value * sr), localStart, rendered.Length)
                    : rendered.Length;
                int clipLen = localEnd - localStart;
                if (clipLen <= 0) continue;

                
                
                
                float soundGain = item.Volume * item.Volume;
                float clipGain  = clip.Volume;                
                float totalGain = soundGain * clipGain * trackGain;

                int fadeInSamples  = Math.Clamp((int)(clip.FadeInSeconds  * sr), 0, clipLen / 2);
                int fadeOutSamples = Math.Clamp((int)(clip.FadeOutSeconds * sr), 0, clipLen / 2);

                var processed = new float[clipLen];
                for (int i = 0; i < clipLen; i++)
                {
                    float g = totalGain;
                    if (fadeInSamples  > 0 && i < fadeInSamples)
                        g *= (float)i / fadeInSamples;
                    if (fadeOutSamples > 0 && i >= clipLen - fadeOutSamples)
                        g *= (float)(clipLen - 1 - i) / fadeOutSamples;
                    processed[i] = rendered[localStart + i] * g;
                }

                
                int offsetSamples = Math.Max(0, (int)(clip.StartOffsetSeconds * sr));
                int endSample = offsetSamples + clipLen;
                if (endSample > mixLength) mixLength = endSample;

                trackData.Add((offsetSamples, processed));
            }
        }

        if (trackData.Count == 0) return Array.Empty<float>();

        if (mixLength > MaxMixSamples) mixLength = MaxMixSamples;

        
        var mix = new float[mixLength];
        foreach (var (offset, samples) in trackData)
        {
            int copyLen = Math.Min(samples.Length, mixLength - offset);
            if (copyLen <= 0) continue;
            for (int i = 0; i < copyLen; i++)
                mix[offset + i] += samples[i];
        }

        float peak = 0f;
        foreach (float s in mix)
        {
            float abs = MathF.Abs(s);
            if (abs > peak) peak = abs;
        }
        if (peak > 1.0f)
        {
            float scale = 1.0f / peak;
            for (int i = 0; i < mix.Length; i++)
                mix[i] *= scale;
        }

        return mix;
    }

    
    
    private static List<(int S, int E)> GetSegmentsForItem(CachedSound sound, SoundItem item)
    {
        int sr = sound.SampleRate * sound.Channels;

        int globalStart = item.TrimStartSeconds.HasValue
            ? Math.Clamp((int)(item.TrimStartSeconds.Value * sr), 0, sound.TotalSamples) : 0;
        int globalEnd = item.TrimEndSeconds.HasValue
            ? Math.Clamp((int)(item.TrimEndSeconds.Value * sr), globalStart, sound.TotalSamples)
            : sound.TotalSamples;

        if (item.Segments is null || item.Segments.Count == 0)
            return [(globalStart, globalEnd)];

        var result = item.Segments
            .Select(s => (
                S: Math.Clamp((int)(s.StartSeconds * sr), 0, sound.TotalSamples),
                E: Math.Clamp((int)(s.EndSeconds   * sr), 0, sound.TotalSamples)
            ))
            .Where(s => s.S < s.E)
            .ToList();

        return result.Count > 0 ? result : [(globalStart, globalEnd)];
    }
}
