using System.Diagnostics;
using System.IO;
using System.Text.Json;
using NAudio.Wave;
using NAudio.Wave.SampleProviders;
using SoundPad.App.Audio;

internal static class MemoryChecks
{
    internal static void Run(Action<bool, string> check)
    {
        var samples = new float[CachedSound.MemoryLimitBytes / 4 + 96000];
        for (int i = 0; i < samples.Length; i++) samples[i] = (i % 997 - 498) / 500f;
        var sound = new CachedSound(samples);
        check(sound.IsDiskBacked && sound.ResidentBytes <= 384000, "Long sounds retain at most one second of float PCM in RAM");
        var actual = new float[samples.Length];
        sound.CopySamples(0, actual, 0, actual.Length);
        check(actual.SequenceEqual(samples), "Disk-backed cache preserves every float sample exactly");
        var shortSound = new CachedSound(new float[100]);
        check(!shortSound.IsDiskBacked, "Short sound effects stay entirely in RAM");
        bool valid = true;
        Parallel.For(0, 8, stream =>
        {
            var buffer = new float[1920];
            for (int n = 0; n < 100; n++)
            {
                int start = (stream * 91001 + n * 777) % (samples.Length - buffer.Length);
                sound.CopySamples(start, buffer, 0, buffer.Length);
                if (!buffer.AsSpan().SequenceEqual(samples.AsSpan(start, buffer.Length))) valid = false;
            }
        });
        check(valid, "Concurrent outputs and random seeks have independent cache positions");
        check(sound.BuildPeaks(137).SequenceEqual(RecordingSamples.Peaks(samples, 137)), "Block waveform generation matches full-array peaks exactly");
        check(new CachedSound([.1f, -.5f]).BuildPeaks(5).SequenceEqual(RecordingSamples.Peaks([.1f, -.5f], 5)), "Waveform buckets handle clips shorter than the bucket count");
        var ram = new CachedSound(samples[..100000]);
        var segments = new List<(int S, int E)> { (100, 1000), (12000, 16000) };
        var diskProvider = new CachedSoundSampleProvider(sound, segments, 200, 300);
        var ramProvider = new CachedSoundSampleProvider(ram, segments, 200, 300);
        var diskResult = new float[6000]; var ramResult = new float[6000];
        int count = diskProvider.Read(diskResult, 7, 5000);
        int expectedCount = ramProvider.Read(ramResult, 7, 5000);
        check(count == expectedCount && diskResult.SequenceEqual(ramResult), "Segment playback and fades match RAM playback sample for sample");
        check(EffectProcessor.Render(sound, segments, true, true, 1).SequenceEqual(EffectProcessor.Render(ram, segments, true, true, 1)), "Reverse and normalize effects match on disk and RAM caches");
        using (var capture = new InstantReplayService(1))
        {
            void Set(string field, object value) => typeof(InstantReplayService).GetField(field,
                System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!.SetValue(capture, value);
            Set("_ring", new float[] { 8, 9, 4, 5, 6, 7 }); Set("_writePos", 2); Set("_totalWritten", 10L);
            Set("_micRing", new float[] { 18, 19, 14, 15, 16, 17 }); Set("_micWritePos", 2); Set("_micTotalWritten", 10L);
            check(capture.GetRecentSamples(4)!.SequenceEqual(new float[] { 6, 7, 8, 9 }) &&
                capture.GetRecentMicSamples(4)!.SequenceEqual(new float[] { 16, 17, 18, 19 }), "Bounded replay snapshots retain the newest system and mic frames across wrap");
            check(capture.GetRecentSamples(3)!.SequenceEqual(new float[] { 8, 9 }) && capture.GetRecentSamples(0)!.Length == 0,
                "Small replay snapshots stay frame-aligned without copying the full ring");
            check(capture.GetRecentSamples()!.SequenceEqual(new float[] { 4, 5, 6, 7, 8, 9 }), "Full replay snapshot still retains the complete available history");
        }

        string wav = Path.Combine(AppContext.BaseDirectory, $"memory-check-{Guid.NewGuid():N}.wav");
        try
        {
            using (var writer = new WaveFileWriter(wav, CachedSound.TargetFormat)) writer.WriteSamples(samples, 0, samples.Length);
            var decoded = new CachedSound(wav);
            var decodedSamples = new float[samples.Length];
            decoded.CopySamples(0, decodedSamples, 0, decodedSamples.Length);
            check(decoded.IsDiskBacked && decodedSamples.SequenceEqual(samples), "File decoding crosses the memory threshold without losing samples");
        }
        finally { File.Delete(wav); }

        var timings = new double[1000]; var playbackBlock = new float[1920];
        for (int n = 0; n < timings.Length; n++)
        {
            long start = Stopwatch.GetTimestamp();
            for (int track = 0; track < 8; track++)
                sound.CopySamples(100000 + ((n * 1920 + track * 1171) % (samples.Length - 102000)), playbackBlock, 0, playbackBlock.Length);
            timings[n] = Stopwatch.GetElapsedTime(start).TotalMilliseconds;
        }
        Array.Sort(timings);
        Console.WriteLine($"PERF Eight simultaneous 20 ms audio blocks: p99 {timings[990]:F3} ms, max {timings[^1]:F3} ms (20 ms budget)");
    }

    // Read-only, opt-in local measurement: never changes library files or settings.
    internal static void ProfileLibrary(bool legacy)
    {
        string directory = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "SoundPad");
        using var decks = JsonDocument.Parse(File.ReadAllText(Path.Combine(directory, "decks.json")));
        using var settings = JsonDocument.Parse(File.ReadAllText(Path.Combine(directory, "settings.json")));
        string? active = settings.RootElement.GetProperty("ActiveDeckId").GetString();
        var deck = decks.RootElement.EnumerateArray().First(d => d.GetProperty("Id").GetString() == active);
        var retained = new List<object>(); long decodedBytes = 0, residentBytes = 0; int diskCount = 0;
        long allocatedStart = GC.GetTotalAllocatedBytes(true);
        var timer = Stopwatch.StartNew();
        foreach (var item in deck.GetProperty("Sounds").EnumerateArray())
        {
            string path = item.GetProperty("FilePath").GetString()!;
            if (!File.Exists(path)) continue;
            if (legacy)
            {
                using var reader = new AudioFileReader(path);
                ISampleProvider provider = reader;
                if (reader.WaveFormat.Channels == 1) provider = new MonoToStereoSampleProvider(provider);
                if (reader.WaveFormat.SampleRate != 48000) provider = new WdlResamplingSampleProvider(provider, 48000);
                var list = new List<float>(); var block = new float[96000]; int read;
                while ((read = provider.Read(block, 0, block.Length)) > 0)
                    for (int i = 0; i < read; i++) list.Add(block[i]);
                var samples = list.ToArray(); retained.Add(samples); decodedBytes += (long)samples.Length * 4;
                residentBytes += (long)samples.Length * 4;
            }
            else
            {
                var sound = new CachedSound(path); retained.Add(sound);
                decodedBytes += (long)sound.TotalSamples * 4; residentBytes += sound.ResidentBytes;
                if (sound.IsDiskBacked) diskCount++;
            }
        }
        timer.Stop();
        var process = Process.GetCurrentProcess(); process.Refresh();
        Console.WriteLine($"PROFILE {(legacy ? "legacy" : "optimized")}: {retained.Count} sounds; decoded {decodedBytes / 1048576.0:F1} MiB; resident audio {residentBytes / 1048576.0:F1} MiB; disk-backed {diskCount}; load {timer.Elapsed.TotalSeconds:F2}s; allocations {(GC.GetTotalAllocatedBytes(true) - allocatedStart) / 1048576.0:F1} MiB; working set {process.WorkingSet64 / 1048576.0:F1} MiB; private {process.PrivateMemorySize64 / 1048576.0:F1} MiB");
        GC.KeepAlive(retained);
    }
}
