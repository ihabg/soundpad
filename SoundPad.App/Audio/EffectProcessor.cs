using NAudio.Wave;
using NAudio.Wave.SampleProviders;
using SoundPad.App.Models;

namespace SoundPad.App.Audio;

// Renders non-destructive audio effects (Reverse, Normalize, Speed) into a
// new float[] that can be wrapped in CachedSound(float[]) for playback or export.
// All processing happens off the audio thread. Processing order:
//   extract segments → reverse → normalize → resample for speed.
// Fade-in/Fade-out are NOT applied here — they remain streaming operations
// in CachedSoundSampleProvider so they do not affect the cached result.
public static class EffectProcessor
{
    // Returns a safe playback speed from a SoundItem.
    // Treats 0, NaN, Infinity, and negatives as 1.0 (no change).
    // Clamps all other values to [0.5, 2.0].
    public static double GetSafePlaybackSpeed(SoundItem item)
    {
        double s = item.PlaybackSpeed;
        if (double.IsNaN(s) || double.IsInfinity(s) || s <= 0)
            return 1.0;
        return Math.Clamp(s, 0.5, 2.0);
    }

    // Returns true when the item has at least one active effect.
    public static bool HasEffects(SoundItem item)
        => item.ReverseAudio
        || item.NormalizeAudio
        || Math.Abs(GetSafePlaybackSpeed(item) - 1.0) > 0.001;

    // Renders all effects into a new float[] ready for CachedSound(float[]).
    //
    // segments  — (S, E) sample-index pairs into rawSound.AudioData,
    //             produced by GetSegments(). Accounts for trim and block cuts.
    // reverse   — reverses the audio (stereo-safe: swaps frame pairs, not samples).
    // normalize — scales to peak amplitude 1.0; silent audio is left unchanged.
    // speed     — [0.5, 2.0]; resamples vinyl-style (pitch shifts with speed).
    //
    // Returns Array.Empty<float>() if there is no audio to render.
    public static float[] Render(
        CachedSound rawSound,
        IReadOnlyList<(int S, int E)> segments,
        bool reverse,
        bool normalize,
        double speed)
    {
        // Step 1: extract segment audio into a flat contiguous buffer.
        int totalSamples = 0;
        foreach (var (s, e) in segments)
            totalSamples += Math.Max(0, e - s);

        if (totalSamples <= 0)
            return Array.Empty<float>();

        var flat = new float[totalSamples];
        int writePos = 0;
        foreach (var (s, e) in segments)
        {
            int len = Math.Max(0, e - s);
            if (len == 0) continue;
            Array.Copy(rawSound.AudioData, s, flat, writePos, len);
            writePos += len;
        }

        // Step 2: reverse (stereo-safe — swap frame pairs, not individual samples).
        if (reverse)
            ReverseFrames(flat, rawSound.Channels);

        // Step 3: normalize (scale to peak = 1.0; leaves silent audio unchanged).
        if (normalize)
            NormalizeInPlace(flat);

        // Step 4: speed resample (vinyl-style; pitch shifts proportionally to speed).
        double safeSpeed = Math.Clamp(speed, 0.5, 2.0);
        if (Math.Abs(safeSpeed - 1.0) > 0.001)
            flat = ResampleForSpeed(flat, rawSound.WaveFormat, safeSpeed);

        return flat;
    }

    // Reverses audio by swapping complete multi-channel frames.
    // Stereo (channels=2): [L0 R0] swaps with [Ln-1 Rn-1], etc.
    // Samples within a frame always stay together, preserving stereo channel pairing.
    private static void ReverseFrames(float[] data, int channels)
    {
        if (channels <= 0) return;
        int frames = data.Length / channels;
        for (int i = 0; i < frames / 2; i++)
        {
            int j = frames - 1 - i;
            for (int c = 0; c < channels; c++)
            {
                (data[j * channels + c], data[i * channels + c]) =
                    (data[i * channels + c], data[j * channels + c]);
            }
        }
    }

    // Scales all samples so the peak amplitude becomes 1.0.
    // Safe on silence: if peak < epsilon no scaling is applied (avoids div-by-zero).
    // All output values are clamped to [-1.0, 1.0].
    private static void NormalizeInPlace(float[] data)
    {
        float peak = 0f;
        for (int i = 0; i < data.Length; i++)
        {
            float abs = MathF.Abs(data[i]);
            if (abs > peak) peak = abs;
        }

        const float Epsilon = 1e-6f;
        if (peak < Epsilon) return;

        float scale = 1.0f / peak;
        for (int i = 0; i < data.Length; i++)
            data[i] = Math.Clamp(data[i] * scale, -1.0f, 1.0f);
    }

    // Treats the input as if recorded at (targetSampleRate × speed) Hz, then
    // resamples back to targetSampleRate via WDL.
    // Speed=2.0: declared at 96 kHz, resampled to 48 kHz → half duration, higher pitch.
    // Speed=0.5: declared at 24 kHz, resampled to 48 kHz → double duration, lower pitch.
    private static float[] ResampleForSpeed(float[] data, WaveFormat fmt, double speed)
    {
        int targetRate = fmt.SampleRate;
        int inputRate  = (int)Math.Round(targetRate * speed);
        inputRate      = Math.Clamp(inputRate, 1000, 768_000);

        var inputFmt   = WaveFormat.CreateIeeeFloatWaveFormat(inputRate, fmt.Channels);
        ISampleProvider src = new ArraySampleProvider(data, inputFmt);

        if (inputRate != targetRate)
            src = new WdlResamplingSampleProvider(src, targetRate);

        var result = new List<float>(data.Length);
        var buf    = new float[targetRate * fmt.Channels];
        int read;
        while ((read = src.Read(buf, 0, buf.Length)) > 0)
        {
            for (int i = 0; i < read; i++)
                result.Add(buf[i]);
        }

        return result.ToArray();
    }

    // Wraps a float[] as a streaming ISampleProvider without copying data.
    // Used by ResampleForSpeed to feed WdlResamplingSampleProvider.
    private sealed class ArraySampleProvider : ISampleProvider
    {
        private readonly float[] _data;
        private int              _pos;

        public WaveFormat WaveFormat { get; }

        public ArraySampleProvider(float[] data, WaveFormat fmt)
        {
            _data      = data;
            WaveFormat = fmt;
        }

        public int Read(float[] buffer, int offset, int count)
        {
            int available = _data.Length - _pos;
            int toRead    = Math.Min(available, count);
            if (toRead <= 0) return 0;
            Array.Copy(_data, _pos, buffer, offset, toRead);
            _pos += toRead;
            return toRead;
        }
    }
}
