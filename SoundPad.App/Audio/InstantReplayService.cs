using NAudio.CoreAudioApi;
using NAudio.Wave;
using NAudio.Wave.SampleProviders;

namespace SoundPad.App.Audio;

// Device entry for the Instant Replay capture-device ComboBox.
// Id == null means "use the Windows default render endpoint".
internal sealed record IrCaptureDevice(string? Id, string Name)
{
    public override string ToString() => Name;

    internal static List<IrCaptureDevice> GetAll()
    {
        var list = new List<IrCaptureDevice>();
        try
        {
            using var enumerator = new MMDeviceEnumerator();

            string? defaultId   = null;
            string  defaultName = "";
            try
            {
                var def     = enumerator.GetDefaultAudioEndpoint(DataFlow.Render, Role.Multimedia);
                defaultId   = def.ID;
                defaultName = def.FriendlyName;
            }
            catch { }

            list.Add(new IrCaptureDevice(null,
                string.IsNullOrEmpty(defaultName)
                    ? "Default output device"
                    : $"Default ({defaultName})"));

            var col = enumerator.EnumerateAudioEndPoints(DataFlow.Render, DeviceState.Active);
            for (int i = 0; i < col.Count; i++)
            {
                string label = col[i].ID == defaultId
                    ? $"{col[i].FriendlyName} (Default)"
                    : col[i].FriendlyName;
                list.Add(new IrCaptureDevice(col[i].ID, label));
            }
        }
        catch
        {
            if (list.Count == 0)
                list.Add(new IrCaptureDevice(null, "Default output device"));
        }
        return list;
    }
}

// Keeps a rolling ring buffer of the last N minutes of system audio (WASAPI loopback)
// and optionally a second ring buffer for microphone audio.
// The WASAPI/WaveIn threads write; the UI thread reads diagnostics and drains clips.
internal sealed class InstantReplayService : IDisposable
{
    // KSDATAFORMAT_SUBTYPE GUIDs — used to normalise WaveFormatExtensible.
    private static readonly Guid SubTypeIeeeFloat = new("00000003-0000-0010-8000-00aa00389b71");
    private static readonly Guid SubTypePcm       = new("00000001-0000-0010-8000-00aa00389b71");

    private static readonly WaveFormat TargetFormat = CachedSound.TargetFormat; // 48 kHz stereo float

    // ── Shared lock — guards BOTH the system ring and the mic ring ───────────
    private readonly object _lock = new();

    // ── System audio ring buffer ─────────────────────────────────────────────
    private float[] _ring;
    private int     _writePos;
    private long    _totalWritten;
    private int     _minutes;

    // ── Mic audio ring buffer (empty until StartMic is called) ───────────────
    private float[] _micRing        = Array.Empty<float>();
    private int     _micWritePos;
    private long    _micTotalWritten;

    // ── Signal metrics — volatile so UI thread reads without a lock ──────────
    private volatile float _lastRms;
    private volatile float _lastPeak;
    private volatile int   _dataAvailableCount;
    private volatile int   _lastBytesRecorded;
    private volatile int   _lastNonZeroCount;
    private          long  _totalBytesReceived; // display-only; best-effort accuracy
    private volatile float _lastMicRms;

    // ── System capture objects ───────────────────────────────────────────────
    private MMDevice?              _captureDevice;
    private WasapiLoopbackCapture? _capture;

    // ── System working format (populated by SetupConversion) ─────────────────
    private WaveFormat? _workingFormat;
    private bool        _needsResample;
    private bool        _needsMono2Stereo;

    // ── System direct-conversion staging buffers (WASAPI callback thread) ────
    private float[] _directBuf = new float[192_000];
    private float[] _expandBuf = new float[192_000];

    // ── System resample chain — only allocated when device rate != 48 kHz ────
    private BufferedWaveProvider? _convBuffer;
    private ISampleProvider?      _convChain;
    private readonly float[]      _readBuf = new float[96_000];

    // ── Mic capture objects ──────────────────────────────────────────────────
    private WaveInEvent? _micWaveIn;
    private float        _micVolume     = 1.0f;
    private float[]      _micConvertBuf = new float[48_000]; // 500 ms at 48 kHz stereo (generous)

    // ── Public surface ────────────────────────────────────────────────────────
    public bool   IsRunning          { get; private set; }
    public bool   IsMicRunning       { get; private set; }
    public float  LastRms            => _lastRms;
    public float  LastPeak           => _lastPeak;
    public float  LastMicRms         => _lastMicRms;
    public bool   IsReceivingAudio   => _lastRms    > 0.0001f;
    public bool   IsMicReceivingAudio => _lastMicRms > 0.0001f;
    public int    DataAvailableCount => _dataAvailableCount;
    public int    LastBytesRecorded  => _lastBytesRecorded;
    public long   TotalBytesReceived => _totalBytesReceived;
    public int    LastNonZeroCount   => _lastNonZeroCount;
    public string CaptureDeviceName  { get; private set; } = "";
    public string CaptureFormatDesc  { get; private set; } = "";
    public string? FallbackWarning   { get; private set; }

    public float DeviceMasterPeak
    {
        get
        {
            try   { return _captureDevice?.AudioMeterInformation?.MasterPeakValue ?? 0f; }
            catch { return 0f; }
        }
    }

    internal InstantReplayService(int minutes)
    {
        _minutes = Math.Clamp(minutes, 1, 5);
        _ring    = AllocRing(_minutes);
    }

    private static float[] AllocRing(int minutes) =>
        new float[minutes * 60 * TargetFormat.SampleRate * TargetFormat.Channels];

    // ── System capture ───────────────────────────────────────────────────────

    internal void Start(string? deviceId = null)
    {
        if (IsRunning) return;
        try
        {
            _capture = OpenCapture(deviceId);
            SetupConversion(_capture.WaveFormat);
            _capture.DataAvailable += OnDataAvailable;
            _capture.StartRecording();
            IsRunning = true;
        }
        catch
        {
            Cleanup();
            throw;
        }
    }

    private WasapiLoopbackCapture OpenCapture(string? deviceId)
    {
        FallbackWarning = null;
        using var enumerator = new MMDeviceEnumerator();

        if (string.IsNullOrEmpty(deviceId))
        {
            try   { _captureDevice = enumerator.GetDefaultAudioEndpoint(DataFlow.Render, Role.Multimedia); }
            catch { }
            CaptureDeviceName = _captureDevice?.FriendlyName ?? "(default output)";
            return _captureDevice is not null
                ? new WasapiLoopbackCapture(_captureDevice)
                : new WasapiLoopbackCapture();
        }

        try
        {
            _captureDevice    = enumerator.GetDevice(deviceId);
            CaptureDeviceName = _captureDevice.FriendlyName;
            return new WasapiLoopbackCapture(_captureDevice);
        }
        catch
        {
            try
            {
                _captureDevice    = enumerator.GetDefaultAudioEndpoint(DataFlow.Render, Role.Multimedia);
                CaptureDeviceName = _captureDevice.FriendlyName;
                FallbackWarning   = $"Selected device unavailable — using default: {CaptureDeviceName}";
                return new WasapiLoopbackCapture(_captureDevice);
            }
            catch
            {
                CaptureDeviceName = "(default output)";
                FallbackWarning   = "Selected device unavailable — using system default.";
                return new WasapiLoopbackCapture();
            }
        }
    }

    private void SetupConversion(WaveFormat captureFormat)
    {
        WaveFormat wf = captureFormat;
        if (captureFormat is WaveFormatExtensible ext)
        {
            if (ext.SubFormat == SubTypeIeeeFloat)
                wf = WaveFormat.CreateIeeeFloatWaveFormat(captureFormat.SampleRate, captureFormat.Channels);
            else if (ext.SubFormat == SubTypePcm)
                wf = new WaveFormat(captureFormat.SampleRate, captureFormat.BitsPerSample, captureFormat.Channels);
            else
                throw new NotSupportedException(
                    $"Unsupported extensible sub-format: {ext.SubFormat}. " +
                    "Change Windows audio format to 16-bit or 32-bit float.");
        }

        bool canConvert =
            (wf.Encoding == WaveFormatEncoding.IeeeFloat && wf.BitsPerSample == 32) ||
            (wf.Encoding == WaveFormatEncoding.Pcm       && wf.BitsPerSample == 16);
        if (!canConvert)
            throw new NotSupportedException(
                $"Unsupported loopback format: {wf.Encoding}/{wf.BitsPerSample}-bit at {wf.SampleRate} Hz. " +
                "Change Windows audio format to 16-bit or 32-bit float.");

        _workingFormat    = wf;
        _needsResample    = wf.SampleRate != TargetFormat.SampleRate;
        _needsMono2Stereo = wf.Channels   == 1;

        if (_needsResample)
        {
            _convBuffer = new BufferedWaveProvider(wf) { DiscardOnBufferOverflow = true };
            ISampleProvider chain = wf.Encoding == WaveFormatEncoding.IeeeFloat
                ? (ISampleProvider)new WaveToSampleProvider(_convBuffer)
                : new Pcm16BitToSampleProvider(_convBuffer);
            if (_needsMono2Stereo)
                chain = new MonoToStereoSampleProvider(chain);
            chain      = new WdlResamplingSampleProvider(chain, TargetFormat.SampleRate);
            _convChain = chain;
        }

        string extTag = captureFormat is WaveFormatExtensible ext2
            ? $"  [Ext: {ext2.SubFormat}]"
            : "";
        CaptureFormatDesc =
            $"{captureFormat.SampleRate} Hz  {captureFormat.Channels} ch  " +
            $"{captureFormat.BitsPerSample}-bit  {wf.Encoding}{extTag}";
    }

    internal void Stop()
    {
        if (!IsRunning) return;
        IsRunning = false;
        Cleanup();
        lock (_lock) { _writePos = 0; _totalWritten = 0; }
        _lastRms = 0f; _lastPeak = 0f;
    }

    internal void ResizeBuffer(int minutes, string? deviceId = null)
    {
        bool wasRunning = IsRunning;
        if (IsRunning) { IsRunning = false; Cleanup(); }
        _minutes = Math.Clamp(minutes, 1, 5);
        lock (_lock) { _ring = AllocRing(_minutes); _writePos = 0; _totalWritten = 0; }
        _lastRms = 0f; _lastPeak = 0f;
        if (wasRunning) Start(deviceId);
    }

    private void OnDataAvailable(object? sender, WaveInEventArgs e)
    {
        if (e.BytesRecorded == 0 || _workingFormat is null) return;

        _dataAvailableCount++;
        _lastBytesRecorded   = e.BytesRecorded;
        _totalBytesReceived += e.BytesRecorded;

        if (_needsResample)
        {
            var buf   = _convBuffer;
            var chain = _convChain;
            if (buf is null || chain is null) return;
            buf.AddSamples(e.Buffer, 0, e.BytesRecorded);
            int read;
            while ((read = chain.Read(_readBuf, 0, _readBuf.Length)) > 0)
                AppendToRing(_readBuf, read);
        }
        else
        {
            int converted = ConvertToFloat(e.Buffer, e.BytesRecorded);
            if (converted == 0) return;

            if (_needsMono2Stereo)
            {
                EnsureExpandBuf(converted * 2);
                for (int i = 0; i < converted; i++)
                {
                    _expandBuf[i * 2]     = _directBuf[i];
                    _expandBuf[i * 2 + 1] = _directBuf[i];
                }
                AppendToRing(_expandBuf, converted * 2);
            }
            else
            {
                AppendToRing(_directBuf, converted);
            }
        }
    }

    private int ConvertToFloat(byte[] data, int byteCount)
    {
        if (_workingFormat!.Encoding == WaveFormatEncoding.IeeeFloat && _workingFormat.BitsPerSample == 32)
        {
            int count = byteCount / 4;
            EnsureDirectBuf(count);
            Buffer.BlockCopy(data, 0, _directBuf, 0, byteCount);
            return count;
        }
        if (_workingFormat.Encoding == WaveFormatEncoding.Pcm && _workingFormat.BitsPerSample == 16)
        {
            int count = byteCount / 2;
            EnsureDirectBuf(count);
            for (int i = 0; i < count; i++)
                _directBuf[i] = (short)(data[i * 2] | (data[i * 2 + 1] << 8)) / 32768f;
            return count;
        }
        return 0;
    }

    private void AppendToRing(float[] samples, int sampleCount)
    {
        lock (_lock)
        {
            int toEnd = _ring.Length - _writePos;
            if (sampleCount <= toEnd)
            {
                Buffer.BlockCopy(samples, 0, _ring, _writePos * 4, sampleCount * 4);
                _writePos += sampleCount;
                if (_writePos == _ring.Length) _writePos = 0;
            }
            else
            {
                Buffer.BlockCopy(samples, 0,         _ring, _writePos * 4, toEnd * 4);
                Buffer.BlockCopy(samples, toEnd * 4, _ring, 0,             (sampleCount - toEnd) * 4);
                _writePos = sampleCount - toEnd;
            }
            _totalWritten += sampleCount;
        }

        float sumSq = 0f, peak = 0f;
        int   nonZero = 0;
        for (int i = 0; i < sampleCount; i++)
        {
            float s   = samples[i];
            float abs = s < 0f ? -s : s;
            sumSq += s * s;
            if (abs > peak)  peak = abs;
            if (abs > 1e-6f) nonZero++;
        }
        _lastRms          = (float)Math.Sqrt(sumSq / sampleCount);
        _lastPeak         = peak;
        _lastNonZeroCount = nonZero;
    }

    // Returns the chronological system audio ring as a contiguous float[], or null if empty.
    internal float[]? GetRecentSamples()
    {
        lock (_lock)
        {
            if (_totalWritten == 0) return null;
            int available = (int)Math.Min(_totalWritten, (long)_ring.Length);
            var result    = new float[available];
            int startPos  = _totalWritten >= _ring.Length ? _writePos : 0;
            int firstPart = Math.Min(available, _ring.Length - startPos);
            Buffer.BlockCopy(_ring, startPos * 4, result, 0,             firstPart * 4);
            if (firstPart < available)
                Buffer.BlockCopy(_ring, 0,         result, firstPart * 4, (available - firstPart) * 4);
            return result;
        }
    }

    // ── Mic capture ──────────────────────────────────────────────────────────

    // Opens the selected WaveIn device and begins recording into the mic ring.
    // Uses 48 kHz mono 16-bit PCM (same format as MicPassthrough) for compatibility.
    // Throws MmException if the device does not support the capture format.
    internal void StartMic(int deviceNumber, float volume = 1.0f)
    {
        if (IsMicRunning) return;
        _micVolume = Math.Clamp(volume, 0f, 2f);

        // Allocate the mic ring to the same duration as the system ring.
        lock (_lock)
        {
            _micRing        = AllocRing(_minutes);
            _micWritePos    = 0;
            _micTotalWritten = 0;
        }
        _lastMicRms = 0f;

        var captureFormat = new WaveFormat(TargetFormat.SampleRate, 1); // 48 kHz mono 16-bit PCM

        _micWaveIn = new WaveInEvent
        {
            DeviceNumber       = deviceNumber,
            WaveFormat         = captureFormat,
            BufferMilliseconds = 50,
            NumberOfBuffers    = 2
        };
        _micWaveIn.DataAvailable += OnMicDataAvailable;

        try
        {
            _micWaveIn.StartRecording();
            IsMicRunning = true;
        }
        catch
        {
            _micWaveIn.DataAvailable -= OnMicDataAvailable;
            _micWaveIn.Dispose();
            _micWaveIn = null;
            throw;
        }
    }

    // Stops mic capture. Safe to call multiple times or before StartMic.
    internal void StopMic()
    {
        IsMicRunning = false;
        if (_micWaveIn is not null)
        {
            try { _micWaveIn.StopRecording(); } catch { }
            _micWaveIn.DataAvailable -= OnMicDataAvailable;
            _micWaveIn.Dispose();
            _micWaveIn = null;
        }
        _lastMicRms = 0f;
    }

    private void OnMicDataAvailable(object? sender, WaveInEventArgs e)
    {
        if (e.BytesRecorded == 0) return;

        // Convert 16-bit PCM mono → float stereo with volume applied.
        int monoCount   = e.BytesRecorded / 2;
        int stereoCount = monoCount * 2;
        EnsureMicConvertBuf(stereoCount);
        float vol = _micVolume;
        for (int i = 0; i < monoCount; i++)
        {
            float s = (short)(e.Buffer[i * 2] | (e.Buffer[i * 2 + 1] << 8)) / 32768f * vol;
            _micConvertBuf[i * 2]     = s;
            _micConvertBuf[i * 2 + 1] = s;
        }
        AppendToMicRing(_micConvertBuf, stereoCount);
    }

    private void AppendToMicRing(float[] samples, int sampleCount)
    {
        lock (_lock)
        {
            if (_micRing.Length == 0) return;
            int toEnd = _micRing.Length - _micWritePos;
            if (sampleCount <= toEnd)
            {
                Buffer.BlockCopy(samples, 0, _micRing, _micWritePos * 4, sampleCount * 4);
                _micWritePos += sampleCount;
                if (_micWritePos == _micRing.Length) _micWritePos = 0;
            }
            else
            {
                Buffer.BlockCopy(samples, 0,         _micRing, _micWritePos * 4, toEnd * 4);
                Buffer.BlockCopy(samples, toEnd * 4, _micRing, 0,              (sampleCount - toEnd) * 4);
                _micWritePos = sampleCount - toEnd;
            }
            _micTotalWritten += sampleCount;
        }

        float sumSq = 0f;
        for (int i = 0; i < sampleCount; i++) { float s = samples[i]; sumSq += s * s; }
        _lastMicRms = (float)Math.Sqrt(sumSq / sampleCount);
    }

    // Returns the chronological mic ring as a contiguous float[], or null if empty.
    internal float[]? GetRecentMicSamples()
    {
        lock (_lock)
        {
            if (_micTotalWritten == 0 || _micRing.Length == 0) return null;
            int available = (int)Math.Min(_micTotalWritten, (long)_micRing.Length);
            var result    = new float[available];
            int startPos  = _micTotalWritten >= _micRing.Length ? _micWritePos : 0;
            int firstPart = Math.Min(available, _micRing.Length - startPos);
            Buffer.BlockCopy(_micRing, startPos * 4, result, 0,             firstPart * 4);
            if (firstPart < available)
                Buffer.BlockCopy(_micRing, 0,         result, firstPart * 4, (available - firstPart) * 4);
            return result;
        }
    }

    // ── Helpers ──────────────────────────────────────────────────────────────

    private void EnsureDirectBuf(int needed)
    {
        if (_directBuf.Length < needed) _directBuf = new float[needed * 2];
    }

    private void EnsureExpandBuf(int needed)
    {
        if (_expandBuf.Length < needed) _expandBuf = new float[needed * 2];
    }

    private void EnsureMicConvertBuf(int needed)
    {
        if (_micConvertBuf.Length < needed) _micConvertBuf = new float[needed * 2];
    }

    private void Cleanup()
    {
        if (_capture is not null)
        {
            try { _capture.StopRecording(); } catch { }
            _capture.DataAvailable -= OnDataAvailable;
            _capture.Dispose();
            _capture = null;
        }
        _captureDevice?.Dispose();
        _captureDevice = null;
        _convBuffer = null;
        _convChain  = null;

        StopMic();
    }

    public void Dispose()
    {
        IsRunning = false;
        Cleanup();
    }
}
