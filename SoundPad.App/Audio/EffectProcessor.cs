using NAudio.Wave;
using NAudio.Wave.SampleProviders;
using SoundPad.App.Models;

namespace SoundPad.App.Audio;







public static class EffectProcessor
{
    
    
    
    public static double GetSafePlaybackSpeed(SoundItem item)
    {
        return AudioEffectCatalog.Speed(AudioEffectCatalog.FromItem(item));
    }

    
    public static bool HasEffects(SoundItem item)
        => AudioEffectCatalog.HasEffects(AudioEffectCatalog.FromItem(item));

    
    
    
    
    
    
    
    
    
    public static float[] Render(
        CachedSound rawSound,
        IReadOnlyList<(int S, int E)> segments,
        bool reverse,
        bool normalize,
        double speed)
        => Render(rawSound, segments, new Dictionary<string, double>
        {
            ["reverse"] = reverse ? 1 : 0, ["normalize"] = normalize ? 1 : 0, ["speed"] = speed
        });

    public static float[] Render(CachedSound rawSound, IReadOnlyList<(int S, int E)> segments,
        IReadOnlyDictionary<string, double> effects)
    {
        
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
            rawSound.CopySamples(s, flat, writePos, len);
            writePos += len;
        }

        
        foreach (var effect in AudioEffectCatalog.Definitions)
        {
            double value = effect.Sanitize(effects.GetValueOrDefault(effect.Id, effect.DefaultValue));
            if (Math.Abs(value - effect.DefaultValue) > .001)
                flat = effect.Process(flat, rawSound.WaveFormat, value);
        }

        return flat;
    }

    
    
    
    internal static void ReverseFrames(float[] data, int channels)
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

    
    
    
    internal static void NormalizeInPlace(float[] data)
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

    
    
    
    
    internal static float[] ResampleForSpeed(float[] data, WaveFormat fmt, double speed)
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
