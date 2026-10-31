namespace SoundPad.App.Audio;

internal static class RecordingSamples
{
    internal static void FillSilence(float[] ring, ref int position, ref long written, long target)
    {
        long gap = target - written;
        if (gap <= 0 || ring.Length == 0) return;
        if (gap >= ring.Length)
            Array.Clear(ring);
        else
        {
            int first = (int)Math.Min(gap, ring.Length - position);
            Array.Clear(ring, position, first);
            if (first < gap) Array.Clear(ring, 0, (int)gap - first);
        }
        position = (int)((position + gap) % ring.Length);
        written = target;
    }

    // Align recent system and microphone samples at their newest frame, even
    // when one capture stream started later or has fewer samples available.
    internal static float[] MixRecent(float[]? system, float[]? mic, int maxSamples = int.MaxValue)
    {
        int length = Math.Min(maxSamples, Math.Max(system?.Length ?? 0, mic?.Length ?? 0));
        length -= length % 2;
        var mixed = new float[length];
        for (int i = 0; i < length; i++)
        {
            int s = (system?.Length ?? 0) - length + i;
            int m = (mic?.Length ?? 0) - length + i;
            float a = system is not null && s >= 0 ? system[s] : 0;
            float b = mic is not null && m >= 0 ? mic[m] : 0;
            mixed[i] = Math.Clamp(a + b, -1f, 1f);
        }
        return mixed;
    }

    internal static float[] Peaks(float[] samples, int buckets = 100)
    {
        var peaks = new float[buckets];
        for (int b = 0; b < buckets; b++)
        {
            int start = (int)((long)b * samples.Length / buckets);
            int end = (int)((long)(b + 1) * samples.Length / buckets);
            for (int i = start; i < end; i++) peaks[b] = Math.Max(peaks[b], Math.Abs(samples[i]));
        }
        return peaks;
    }
}
