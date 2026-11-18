using NAudio.Wave;
using SoundPad.App.Models;

namespace SoundPad.App.Audio;

public sealed record AudioEffectDefinition(string Id, string Name, double DefaultValue,
    double Minimum, double Maximum, bool IsToggle, string Hint,
    Func<float[], WaveFormat, double, float[]> Process,
    Func<double, double, double>? OutputDuration = null,
    bool IsPlaybackControl = false,
    string? ValueFormat = null,
    bool AddsTail = false)
{
    public double Sanitize(double value) => double.IsFinite(value) ? Math.Clamp(value, Minimum, Maximum) : DefaultValue;
}

// Add an effect here once: both editors generate their controls from these
// definitions, and preview/export/playback use the same processing pipeline.
public static class AudioEffectCatalog
{
    public static IReadOnlyList<AudioEffectDefinition> Definitions { get; } = Array.AsReadOnly(new[]
    {
        new AudioEffectDefinition("reverse", "Reverse", 0, 0, 1, true, "Play the sound backwards.",
            (samples, format, value) => { if (value >= .5) EffectProcessor.ReverseFrames(samples, format.Channels); return samples; }),
        new AudioEffectDefinition("normalize", "Normalize", 0, 0, 1, true, "Raise the peak level without clipping.",
            (samples, format, value) => { if (value >= .5) EffectProcessor.NormalizeInPlace(samples); return samples; }),
        new AudioEffectDefinition("speed", "Speed", 1, .5, 2, false, "Speed also changes pitch (vinyl-style).",
            (samples, format, value) => EffectProcessor.ResampleForSpeed(samples, format, value),
            (duration, value) => duration / value, IsPlaybackControl: true),
        new AudioEffectDefinition("pitch", "Pitch shift", 0, -12, 12, false,
            "Lower or raise pitch by up to one octave without changing duration.",
            CreativeEffects.PitchShift, ValueFormat: "+0.0' st';-0.0' st';0' st'"),
        new AudioEffectDefinition("radio", "Radio / telephone", 0, 0, 100, false,
            "Blend in a narrow, telephone-style filter. 0% is off.",
            CreativeEffects.Radio, ValueFormat: "0'%'"),
        new AudioEffectDefinition("distortion", "Distortion", 0, 0, 100, false,
            "Add soft-clipped crunch and grit. 0% is off.",
            CreativeEffects.Distortion, ValueFormat: "0'%'"),
        new AudioEffectDefinition("echo", "Echo / delay", 0, 0, 100, false,
            "Four fading echoes, 250 ms apart. Adds a one-second tail. 0% is off.",
            CreativeEffects.Echo, (duration, value) => value > .001 ? duration + CreativeEffects.EchoTailSeconds : duration,
            ValueFormat: "0'%'", AddsTail: true),
        new AudioEffectDefinition("reverb", "Reverb", 0, 0, 100, false,
            "Add room ambience with a 1.5-second decay tail. 0% is off.",
            CreativeEffects.Reverb, (duration, value) => value > .001 ? duration + CreativeEffects.ReverbTailSeconds : duration,
            ValueFormat: "0'%'", AddsTail: true)
    });

    public static Dictionary<string, double> FromItem(SoundItem item)
    {
        var values = new Dictionary<string, double>
        {
            ["reverse"] = item.ReverseAudio ? 1 : 0,
            ["normalize"] = item.NormalizeAudio ? 1 : 0,
            ["speed"] = item.PlaybackSpeed
        };
        if (item.Effects is not null)
            foreach (var pair in item.Effects) values[pair.Key] = pair.Value;
        return Sanitize(values);
    }

    public static Dictionary<string, double> Resolve(SoundItem item, IReadOnlyDictionary<string, double>? overrides)
    {
        var values = FromItem(item);
        if (overrides is not null)
            foreach (var pair in overrides) values[pair.Key] = pair.Value;
        return Sanitize(values);
    }

    public static Dictionary<string, double> Sanitize(IReadOnlyDictionary<string, double> values)
    {
        var result = values.ToDictionary(p => p.Key, p => p.Value);
        foreach (var effect in Definitions)
            result[effect.Id] = effect.Sanitize(values.GetValueOrDefault(effect.Id, effect.DefaultValue));
        return result;
    }

    public static bool HasEffects(IReadOnlyDictionary<string, double> values)
        => Definitions.Any(effect => Math.Abs(effect.Sanitize(values.GetValueOrDefault(effect.Id, effect.DefaultValue)) - effect.DefaultValue) > .001);

    public static double Speed(IReadOnlyDictionary<string, double> values)
        => Definitions.Single(e => e.Id == "speed").Sanitize(values.GetValueOrDefault("speed", 1));

    public static double Duration(double sourceSeconds, IReadOnlyDictionary<string, double> values)
        => GetDuration(sourceSeconds, values, includeTails: true);

    public static double BodyDuration(double sourceSeconds, IReadOnlyDictionary<string, double> values)
        => GetDuration(sourceSeconds, values, includeTails: false);

    private static double GetDuration(double sourceSeconds, IReadOnlyDictionary<string, double> values, bool includeTails)
    {
        if (!double.IsFinite(sourceSeconds) || sourceSeconds <= 0) return 0;
        foreach (var effect in Definitions)
            if (effect.OutputDuration is not null && (includeTails || !effect.AddsTail))
                sourceSeconds = effect.OutputDuration(sourceSeconds, effect.Sanitize(values.GetValueOrDefault(effect.Id, effect.DefaultValue)));
        return sourceSeconds;
    }
}
