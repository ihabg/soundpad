using NAudio.Dsp;
using NAudio.Wave;

namespace SoundPad.App.Audio;

internal static class CreativeEffects
{
    internal const double EchoTailSeconds = 1;
    internal const double ReverbTailSeconds = 1.5;

    internal static float[] PitchShift(float[] samples, WaveFormat format, double semitones)
    {
        if (samples.Length == 0 || Math.Abs(semitones) < .001) return samples;
        const int fftSize = 2048;
        const int blockSize = 8192;
        int frames = samples.Length / format.Channels;
        float factor = (float)Math.Pow(2, semitones / 12);
        // Each channel owns its phase-vocoder state. Flush the FFT latency with
        // silence and remove it from the result, preserving the source ending.
        for (int channel = 0; channel < format.Channels; channel++)
        {
            var shifter = new SmbPitchShifter();
            var block = new float[blockSize];
            for (int position = 0; position < frames + fftSize; position += blockSize)
            {
                int count = Math.Min(blockSize, frames + fftSize - position);
                for (int i = 0; i < count; i++)
                    block[i] = position + i < frames ? samples[(position + i) * format.Channels + channel] : 0;
                shifter.PitchShift(factor, count, fftSize, 8, format.SampleRate, block);
                for (int i = 0; i < count; i++)
                {
                    int destination = position + i - fftSize;
                    if ((uint)destination < (uint)frames)
                        samples[destination * format.Channels + channel] = block[i];
                }
            }
        }
        LimitPeak(samples);
        return samples;
    }

    internal static float[] Echo(float[] samples, WaveFormat format, double amount)
    {
        if (samples.Length == 0 || amount <= .001) return samples;
        float mix = (float)(amount / 100);
        int delay = (int)(format.SampleRate * .25) * format.Channels;
        var output = new float[checked(samples.Length + delay * 4)];
        float gain = 1 / (1 + mix * 1.875f);
        for (int i = 0; i < samples.Length; i++) output[i] = samples[i] * gain;
        float repeatGain = mix * gain;
        for (int repeat = 1; repeat <= 4; repeat++)
        {
            int offset = repeat * delay;
            for (int i = 0; i < samples.Length; i++) output[offset + i] += samples[i] * repeatGain;
            repeatGain *= .5f;
        }
        return output;
    }

    internal static float[] Reverb(float[] samples, WaveFormat format, double amount)
    {
        if (samples.Length == 0 || amount <= .001) return samples;
        int channels = format.Channels;
        int inputFrames = samples.Length / channels;
        int frames = checked(inputFrames + (int)(ReverbTailSeconds * format.SampleRate));
        var output = new float[checked(frames * channels)];
        float wetGain = (float)(amount / 100 * .55);
        float dryGain = 1 - wetGain * .5f;
        double[] delays = [.0297, .0371, .0411, .0437];
        for (int channel = 0; channel < channels; channel++)
        {
            var combs = delays.Select(delay => new float[Math.Max(1, (int)(delay * format.SampleRate) + channel * 17)]).ToArray();
            var feedback = combs.Select(buffer => (float)Math.Pow(.001, (double)buffer.Length / format.SampleRate / .85)).ToArray();
            var positions = new int[combs.Length];
            var damped = new float[combs.Length];
            var diffusers = new[] { new float[Math.Max(1, format.SampleRate / 200 + channel * 7)], new float[Math.Max(1, format.SampleRate / 90 + channel * 11)] };
            var diffuserPositions = new int[diffusers.Length];
            for (int frame = 0; frame < frames; frame++)
            {
                float dry = frame < inputFrames ? samples[frame * channels + channel] : 0;
                float wet = 0;
                for (int comb = 0; comb < combs.Length; comb++)
                {
                    float delayed = combs[comb][positions[comb]];
                    damped[comb] = delayed * .65f + damped[comb] * .35f;
                    combs[comb][positions[comb]] = dry + damped[comb] * feedback[comb];
                    wet += delayed * (1 - feedback[comb]) / combs.Length;
                    positions[comb] = (positions[comb] + 1) % combs[comb].Length;
                }
                for (int diffuser = 0; diffuser < diffusers.Length; diffuser++)
                {
                    float delayed = diffusers[diffuser][diffuserPositions[diffuser]];
                    float diffused = delayed - .5f * wet;
                    diffusers[diffuser][diffuserPositions[diffuser]] = wet + .5f * diffused;
                    diffuserPositions[diffuser] = (diffuserPositions[diffuser] + 1) % diffusers[diffuser].Length;
                    wet = diffused;
                }
                output[frame * channels + channel] = dry * dryGain + wet * wetGain;
            }
        }
        LimitPeak(output);
        return output;
    }

    internal static float[] Radio(float[] samples, WaveFormat format, double amount)
    {
        float mix = (float)(amount / 100);
        for (int channel = 0; channel < format.Channels; channel++)
        {
            var highPass = BiQuadFilter.HighPassFilter(format.SampleRate, 300, .707f);
            var lowPass = BiQuadFilter.LowPassFilter(format.SampleRate, 3400, .707f);
            for (int i = channel; i < samples.Length; i += format.Channels)
            {
                float filtered = lowPass.Transform(highPass.Transform(samples[i]));
                samples[i] = samples[i] * (1 - mix) + filtered * mix;
            }
        }
        LimitPeak(samples);
        return samples;
    }

    internal static float[] Distortion(float[] samples, WaveFormat format, double amount)
    {
        float mix = (float)(amount / 100);
        double drive = 1 + mix * 15;
        double scale = Math.Tanh(drive);
        for (int i = 0; i < samples.Length; i++)
        {
            float distorted = (float)(Math.Tanh(samples[i] * drive) / scale);
            samples[i] = Math.Clamp(samples[i] * (1 - mix) + distorted * mix, -1, 1);
        }
        return samples;
    }

    private static void LimitPeak(float[] samples)
    {
        float peak = 1;
        foreach (float sample in samples) peak = Math.Max(peak, MathF.Abs(sample));
        if (peak <= 1) return;
        float gain = 1 / peak;
        for (int i = 0; i < samples.Length; i++) samples[i] *= gain;
    }
}
