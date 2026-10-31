using NAudio.Wave;
using NAudio.Wave.SampleProviders;
using System.IO;
using System.Runtime.InteropServices;

namespace SoundPad.App.Audio;

public class CachedSound
{
    public static readonly WaveFormat TargetFormat = WaveFormat.CreateIeeeFloatWaveFormat(48000, 2);
    internal const int MemoryLimitBytes = 4 * 1024 * 1024;
    private const int PrefixSamples = 48000 * 2; // First second stays ready for immediate playback.
    private const long MaxSourceFileSizeBytes = 200L * 1024 * 1024;
    private readonly float[] _memory;
    private readonly FileStream? _disk;

    public WaveFormat WaveFormat => TargetFormat;
    public int SampleRate => TargetFormat.SampleRate;
    public int Channels => TargetFormat.Channels;
    public int TotalSamples { get; }
    public int TotalFrames => TotalSamples / Channels;
    public TimeSpan Duration => TimeSpan.FromSeconds((double)TotalSamples / (SampleRate * Channels));
    internal bool IsDiskBacked => _disk is not null;
    internal long ResidentBytes => (long)_memory.Length * sizeof(float);

    public CachedSound(string filePath)
    {
        var fileSize = new FileInfo(filePath).Length;
        if (fileSize > MaxSourceFileSizeBytes)
            throw new InvalidOperationException($"Audio file is too large ({fileSize / (1024 * 1024)} MB). Limit is 200 MB. Trim the file to a shorter clip before importing.");

        using var reader = new AudioFileReader(filePath);
        ISampleProvider provider = reader;
        if (reader.WaveFormat.Channels == 1) provider = new MonoToStereoSampleProvider(provider);
        if (reader.WaveFormat.SampleRate != TargetFormat.SampleRate)
            provider = new WdlResamplingSampleProvider(provider, TargetFormat.SampleRate);

        // Decode once in bounded blocks, keeping identical float PCM for long sounds
        // on disk instead of a growing List plus a second full-size array.
        using var memory = new MemoryStream();
        FileStream? disk = null;
        try
        {
            var buffer = new float[PrefixSamples];
            int read;
            while ((read = provider.Read(buffer, 0, buffer.Length)) > 0)
            {
                var bytes = MemoryMarshal.AsBytes(buffer.AsSpan(0, read));
                if (disk is null && memory.Length + bytes.Length > MemoryLimitBytes)
                {
                    disk = OpenTemporaryCache();
                    memory.Position = 0;
                    memory.CopyTo(disk);
                    memory.SetLength(0);
                    memory.Capacity = 0;
                }
                if (disk is null) memory.Write(bytes);
                else disk.Write(bytes);
                TotalSamples = checked(TotalSamples + read);
            }
            if (disk is null)
                _memory = MemoryMarshal.Cast<byte, float>(memory.GetBuffer().AsSpan(0, (int)memory.Length)).ToArray();
            else
            {
                disk.Flush();
                _memory = new float[Math.Min(PrefixSamples, TotalSamples)];
                ReadExactly(disk, 0, _memory);
                _disk = disk;
            }
        }
        catch { disk?.Dispose(); throw; }
    }

    public CachedSound(float[] processedData)
    {
        TotalSamples = processedData.Length;
        if ((long)TotalSamples * sizeof(float) <= MemoryLimitBytes) { _memory = processedData; return; }
        var disk = OpenTemporaryCache();
        try
        {
            disk.Write(MemoryMarshal.AsBytes(processedData.AsSpan()));
            disk.Flush();
            _memory = processedData.AsSpan(0, Math.Min(PrefixSamples, TotalSamples)).ToArray();
            _disk = disk;
        }
        catch { disk.Dispose(); throw; }
    }

    private static FileStream OpenTemporaryCache() => new(
        Path.Combine(Path.GetTempPath(), $"SoundPad-audio-{Guid.NewGuid():N}.pcm"),
        FileMode.CreateNew, FileAccess.ReadWrite, FileShare.Read | FileShare.Delete,
        1, FileOptions.RandomAccess | FileOptions.DeleteOnClose);

    // Independent file offsets allow simultaneous monitor/virtual playback.
    // FileStream owns the handle and deletes the cache when no sound references remain.
    public void CopySamples(int sourceOffset, float[] destination, int destinationOffset, int count)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(sourceOffset);
        if (sourceOffset > TotalSamples - count) throw new ArgumentOutOfRangeException(nameof(count));
        var output = destination.AsSpan(destinationOffset, count);
        if (_disk is null || sourceOffset <= _memory.Length - count)
            _memory.AsSpan(sourceOffset, count).CopyTo(output);
        else ReadExactly(_disk, (long)sourceOffset * sizeof(float), output);
    }

    private static void ReadExactly(FileStream stream, long offset, Span<float> output)
    {
        var bytes = MemoryMarshal.AsBytes(output);
        while (!bytes.IsEmpty)
        {
            int read = RandomAccess.Read(stream.SafeFileHandle, bytes, offset);
            if (read == 0) throw new EndOfStreamException("The decoded audio cache ended unexpectedly.");
            bytes = bytes[read..];
            offset += read;
        }
    }

    internal float[] BuildPeaks(int buckets)
    {
        if (buckets <= 0) throw new ArgumentOutOfRangeException(nameof(buckets));
        var peaks = new float[buckets];
        var block = new float[Math.Min(16384, TotalSamples)];
        int bucket = 0;
        int bucketEnd = TotalSamples / buckets;
        for (int offset = 0; offset < TotalSamples; offset += block.Length)
        {
            int count = Math.Min(block.Length, TotalSamples - offset);
            CopySamples(offset, block, 0, count);
            for (int i = 0; i < count; i++)
            {
                while (offset + i >= bucketEnd && bucket < buckets - 1)
                {
                    bucket++;
                    bucketEnd = (int)((long)(bucket + 1) * TotalSamples / buckets);
                }
                peaks[bucket] = Math.Max(peaks[bucket], MathF.Abs(block[i]));
            }
        }
        return peaks;
    }
}
