using System;
using System.Collections.Generic;
using System.Linq;
using SoundPad.App.Models;
using SoundPad.App.Services;

namespace SoundPad.App.Audio;

public static class MixRenderer
{
    // Hard cap: 10 minutes at 48 kHz stereo IEEE float
    private const int MaxMixSamples = 48000 * 2 * 600;

    // Renders all tracks and their clips into a single interleaved float[].
    //
    // Pipeline per clip (matches main-window playback behavior):
    //   1. Resolve source segments via GetSegmentsForItem (mirrors MainWindow.GetSegments).
    //   2. Render the flat clip through EffectProcessor (segments + reverse/normalize/speed).
    //   3. Apply local composer trim (LocalTrimStart/End) to that flat clip.
    //   4. Apply fade-in/out envelopes and combined gain.
    //      Gain = SoundItem.Volume^2 * clip.Volume^2 * track.Volume^2  (all squared).
    //   5. Place the result at clip.StartOffsetSeconds in the output buffer.
    //   6. Sum all clips. Normalize peak to ≤ 1.0 to prevent clipping.
    public static float[] Render(
        IReadOnlyList<MixTrack>                tracks,
        IReadOnlyDictionary<Guid, SoundItem>   soundItems,
        IReadOnlyDictionary<Guid, CachedSound> cachedSounds)
    {
        // floats-per-second in the target format (48000 Hz * 2 ch = 96000 f/s)
        int sr = CachedSound.TargetFormat.SampleRate * CachedSound.TargetFormat.Channels;

        var trackData = new List<(int Offset, float[] Samples)>();
        int mixLength = 0;

        foreach (var track in tracks)
        {
            float trackGain = track.Volume * track.Volume; // squared

            foreach (var clip in track.Clips)
            {
                if (!soundItems.TryGetValue(clip.SoundItemId, out var item))    continue;
                if (!cachedSounds.TryGetValue(clip.SoundItemId, out var sound)) continue;

                // Step 1+2: render flat clip (source segments + SoundItem effects)
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

                // Step 3: apply local composer trim
                int localStart = clip.LocalTrimStartSeconds.HasValue
                    ? Math.Clamp((int)(clip.LocalTrimStartSeconds.Value * sr), 0, rendered.Length)
                    : 0;
                int localEnd = clip.LocalTrimEndSeconds.HasValue
                    ? Math.Clamp((int)(clip.LocalTrimEndSeconds.Value * sr), localStart, rendered.Length)
                    : rendered.Length;
                int clipLen = localEnd - localStart;
                if (clipLen <= 0) continue;

                // Step 4: fade + gain
                // SoundItem and track volumes use perceptual square curve; clip volume is linear
                // (1.0 = 100%, 2.0 = 200%) so the user-facing percentage maps directly to gain.
                float soundGain = item.Volume * item.Volume;
                float clipGain  = clip.Volume;                // linear: 1.0 = 100 %, 2.0 = 200 %
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

                // Step 5: place at timeline offset
                int offsetSamples = Math.Max(0, (int)(clip.StartOffsetSeconds * sr));
                int endSample = offsetSamples + clipLen;
                if (endSample > mixLength) mixLength = endSample;

                trackData.Add((offsetSamples, processed));
            }
        }

        if (trackData.Count == 0) return Array.Empty<float>();

        if (mixLength > MaxMixSamples) mixLength = MaxMixSamples;

        // Step 6: sum and normalize
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

    // Mirrors MainWindow.GetSegments exactly. Must stay in sync with playback behavior.
    // Converts SoundItem trim/segment data to the sample-index pairs EffectProcessor expects.
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
