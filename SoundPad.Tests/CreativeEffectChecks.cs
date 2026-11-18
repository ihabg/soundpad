using System.Text.Json;
using SoundPad.App.Audio;
using SoundPad.App.Models;

internal static class CreativeEffectChecks
{
    private const int SampleRate = 48000;
    internal static void Run(Action<bool, string> check)
    {
        float[] Tone(double left, double right, double seconds = 1)
        {
            var data = new float[(int)(seconds * SampleRate) * 2];
            for (int f = 0; f < data.Length / 2; f++)
            {
                data[f * 2] = (float)(.25 * Math.Sin(f * 2 * Math.PI * left / SampleRate));
                data[f * 2 + 1] = (float)(.25 * Math.Sin(f * 2 * Math.PI * right / SampleRate));
            }
            return data;
        }
        double Frequency(float[] data, int channel)
        {
            int crossings = 0;
            int start = SampleRate / 5; int end = SampleRate * 4 / 5;
            for (int frame = start + 1; frame < end; frame++)
                if (data[(frame - 1) * 2 + channel] <= 0 && data[frame * 2 + channel] > 0) crossings++;
            return crossings * (double)SampleRate / (end - start);
        }
        double Rms(float[] data, int channel)
        {
            double sum = 0; int count = 0;
            for (int i = SampleRate / 5 * 2 + channel; i < data.Length; i += 2) { sum += data[i] * data[i]; count++; }
            return Math.Sqrt(sum / Math.Max(1, count));
        }
        float[] Render(float[] source, Dictionary<string, double> effects)
        {
            var sound = new CachedSound(source);
            return EffectProcessor.Render(sound, [(0, sound.TotalSamples)], effects);
        }
        var tone = Tone(440, 660);
        check(Render(tone, new()).SequenceEqual(tone), "New creative effects default to off and leave audio unchanged");
        var up = Render(tone, new() { ["pitch"] = 12 });
        var down = Render(tone, new() { ["pitch"] = -12 });
        check(up.Length == tone.Length && down.Length == tone.Length, "Pitch shifting preserves exact duration in both directions");
        check(Math.Abs(Frequency(up, 0) - 880) < 10 && Math.Abs(Frequency(up, 1) - 1320) < 10,
            "One-octave pitch up doubles the frequency independently in both stereo channels");
        check(Math.Abs(Frequency(down, 0) - 220) < 6 && Math.Abs(Frequency(down, 1) - 330) < 6,
            "One-octave pitch down halves frequency without slowing playback");
        var ending = new float[SampleRate * 2];
        var endTone = Tone(440, 0, .1);
        endTone.CopyTo(ending, ending.Length - endTone.Length);
        var shiftedEnding = Render(ending, new() { ["pitch"] = 7 });
        check(shiftedEnding.Skip(shiftedEnding.Length - 960).Any(s => Math.Abs(s) > .001),
            "Pitch render flushes its latency so audio at the source ending survives");

        var impulse = new float[9600]; impulse[0] = .8f;
        var echo = Render(impulse, new() { ["echo"] = 60 });
        check(echo.Length == impulse.Length + SampleRate * 2, "Echo retains a complete one-second tail");
        check(echo[24000] > echo[48000] && echo[48000] > echo[72000] && echo[96000] > 0,
            "Echo produces four decaying repeats at quarter-second intervals");
        check(echo.Where((_, i) => i % 2 == 1).All(s => s == 0), "Echo preserves stereo channel separation");
        var reverb = Render(impulse, new() { ["reverb"] = 70 });
        check(reverb.Length == impulse.Length + (int)(1.5 * SampleRate * 2), "Reverb retains its full decay tail");
        check(reverb.Skip(impulse.Length).Any(s => Math.Abs(s) > .00001), "Reverb remains audible after the original audio ends");
        check(reverb.Where((_, i) => i % 2 == 1).All(s => s == 0), "Reverb uses independent stereo filter states");
        var silence = new float[9600];
        check(Render(silence, new() { ["reverb"] = 100, ["echo"] = 100 }).All(s => s == 0), "Echo and reverb cannot create sound from silence");

        var radio = Render(Tone(100, 1000), new() { ["radio"] = 100 });
        var radioHigh = Render(Tone(10000, 1000), new() { ["radio"] = 100 });
        check(Rms(radio, 1) > Rms(radio, 0) * 4 && Rms(radioHigh, 1) > Rms(radioHigh, 0) * 4,
            "Telephone filter retains speech frequencies while reducing bass and high treble");
        var distorted = Render(tone, new() { ["distortion"] = 80 });
        check(!distorted.SequenceEqual(tone) && distorted.Length == tone.Length && distorted.All(s => float.IsFinite(s) && Math.Abs(s) <= 1),
            "Distortion changes the tone while retaining length and bounded output");
        var all = new Dictionary<string, double> { ["pitch"] = 7, ["radio"] = 30, ["distortion"] = 20, ["echo"] = 25, ["reverb"] = 30, ["speed"] = .5 };
        var combined = Render(tone, all);
        check(Math.Abs(combined.Length / 96000d - AudioEffectCatalog.Duration(1, all)) < .001,
            "Combined creative effects render the same full duration as the timeline predicts");
        check(combined.All(s => float.IsFinite(s) && Math.Abs(s) <= 1.00001), "Stacked creative effects retain finite non-clipping output");
        check(Math.Abs(AudioEffectCatalog.BodyDuration(1, all) - 2) < .001 && AudioEffectCatalog.Duration(0, all) == 0,
            "Timeline distinguishes slowed source content from effect tails and empty audio");
        var item = new SoundItem { Effects = all };
        var restored = JsonSerializer.Deserialize<SoundItem>(JsonSerializer.Serialize(item))!;
        check(EffectProcessor.HasEffects(restored) && all.All(pair => restored.Effects![pair.Key] == pair.Value),
            "All five new effects survive library settings serialization");
        var safe = AudioEffectCatalog.Sanitize(new Dictionary<string, double> { ["pitch"] = double.NaN, ["echo"] = 1000, ["reverb"] = -10 });
        check(safe["pitch"] == 0 && safe["echo"] == 100 && safe["reverb"] == 0,
            "Creative effect parameters reject invalid values and remain in their safe ranges");
        var mixSource = new SoundItem { Effects = new() { ["pitch"] = 5, ["echo"] = 40, ["reverb"] = 35 } };
        var mixSound = new CachedSound(tone);
        var mix = MixRenderer.Render([new MixTrack { Clips = [new MixClip { SoundItemId = mixSource.Id }] }],
            new Dictionary<Guid, SoundItem> { [mixSource.Id] = mixSource }, new Dictionary<Guid, CachedSound> { [mixSource.Id] = mixSound });
        check(Math.Abs(mix.Length / 96000d - 3.5) < .001 && mix.Skip(tone.Length).Any(s => Math.Abs(s) > .00001),
            "Sound Editor mix output includes the full creative effect tails");
    }
}
