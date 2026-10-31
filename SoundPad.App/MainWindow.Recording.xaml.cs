using System.Diagnostics;
using System.IO;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Shapes;
using System.Windows.Threading;
using NAudio.Wave;
using SoundPad.App.Audio;
using SoundPad.App.Dialogs;
using SoundPad.App.Models;
using SoundPad.App.Services;
using UiButton = Wpf.Ui.Controls.Button;
using ControlAppearance = Wpf.Ui.Controls.ControlAppearance;

namespace SoundPad.App;

public partial class MainWindow
{
    private List<RecordingClip> _recordings = [];
    private InstantReplayService? _manualCapture;
    private readonly Stopwatch _manualClock = new();
    private DispatcherTimer? _recordingTimer;
    private bool _savingRecording;
    private bool _manualFinalizing;
    private bool _recordingWorkspaceClosed;
    private bool _recordingIndexReady;
    private bool _closeAfterRecordingSave;
    private readonly Queue<float> _liveRecordingLevels = new();
    private PlaybackHandle? _recordingPreview;
    private AudioPlaybackEngine? _recordingPreviewEngine;
    private Guid? _previewRecordingId;
    private int _previewRequest;
    private CachedSound? _previewSound;
    private readonly Stopwatch _previewClock = new();
    private double _previewBaseSeconds;
    private DispatcherTimer? _previewTimelineTimer;
    private readonly Dictionary<Guid, double> _recordingPositions = new();
    private readonly Dictionary<Guid, Controls.RecordingTimeline> _recordingTimelines = new();
    private Guid? _scrubbingRecordingId;
    private bool _resumeAfterScrubbing;

    private void InitializeRecordingWorkspace()
    {
        try
        {
            _recordings = RecordingLibraryService.Load();
            _recordingIndexReady = true;
            RefreshRecordingCards();
        }
        catch (Exception ex)
        {
            StatusText.Text = $"Could not open recording history: {ex.Message}";
            AppLogger.Error("Recording", "Load history failed", ex);
        }
        _recordingTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(150) };
        _recordingTimer.Tick += RecordingTimer_Tick;
        _recordingTimer.Start();
        _previewTimelineTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(40) };
        _previewTimelineTimer.Tick += (_, _) => UpdateRecordingPlaybackPosition();
        UpdateRecordingControls();
    }

    private async void RecordingTimer_Tick(object? sender, EventArgs e)
    {
        if (_recordingPreview?.IsFinished == true && CurrentPreviewSeconds >= (_previewSound?.Duration.TotalSeconds ?? 0))
        {
            StopRecordingPreview();
            StatusText.Text = "Recording preview finished.";
        }
        if (_manualCapture is not null)
        {
            ManualRecordingTimeText.Text = _manualClock.Elapsed.ToString(@"mm\:ss");
            if (_manualClock.Elapsed >= TimeSpan.FromMinutes(5))
                await FinishManualRecordingAsync();
        }
        if (RecordingTab.IsSelected)
        {
            var source = _manualCapture ?? _instantReplay;
            float peak = source?.IsRunning == true ? Math.Max(source.LastPeak, source.LastMicRms) : 0;
            RecordingLevelMeter.Value = Math.Clamp(peak, 0, 1);
            _liveRecordingLevels.Enqueue(Math.Clamp(peak, 0, 1));
            while (_liveRecordingLevels.Count > 100) _liveRecordingLevels.Dequeue();
            RecordingWaveformHint.Visibility = source?.IsRunning == true ? Visibility.Collapsed : Visibility.Visible;
            DrawWaveform(LiveRecordingWaveform, _liveRecordingLevels.ToArray());
        }
    }

    private void UpdateRecordingControls()
    {
        if (ManualRecordButton is null) return;
        RecordingSourceText.Text = "Source: " + (InstantReplayCaptureDeviceCombo.SelectedItem?.ToString() ?? "Default system output") + (_settings.InstantReplayIncludeMicrophone ? " + microphone" : "");
        RecordingSourceText.ToolTip = RecordingSourceText.Text;
        bool manual = _manualCapture is not null || _manualFinalizing;
        bool replay = _instantReplay?.IsRunning == true;
        ManualRecordButton.Content = _manualCapture is not null ? "■ Stop and save" : "● Start recording";
        ManualRecordButton.Appearance = _manualCapture is not null ? ControlAppearance.Danger : ControlAppearance.Primary;
        ManualRecordButton.IsEnabled = !_manualFinalizing && !_savingRecording && _recordingIndexReady;
        SaveInstantReplayClipButton.IsEnabled = replay && !_savingRecording && _recordingIndexReady;
        InstantReplayCaptureDeviceCombo.IsEnabled = !manual && !replay;
        InstantReplayMicDeviceCombo.IsEnabled = !manual && !replay;
        InstantReplayIncludeMicCheckBox.IsEnabled = !manual && !replay;
        InstantReplayMinutesCombo.IsEnabled = !manual && !replay;
        InstantReplaySwitch.IsEnabled = !manual;
    }

    private async void ManualRecord_Click(object sender, RoutedEventArgs e)
    {
        if (_manualCapture is not null)
        {
            await FinishManualRecordingAsync();
            return;
        }
        if (_manualFinalizing || _savingRecording || !_recordingIndexReady) return;
        StopRecordingPreview();
        var capture = new InstantReplayService(5, preserveTimeline: true);
        try
        {
            capture.Start(_settings.InstantReplayCaptureDeviceId);
            if (_settings.InstantReplayIncludeMicrophone)
            {
                if (InstantReplayMicDeviceCombo.SelectedItem is not MicDevice mic)
                    throw new InvalidOperationException("Choose a microphone first, or turn off Include microphone.");
                capture.StartMic(mic.Number, _settings.InstantReplayMicVolume);
            }
            _manualCapture = capture;
            _manualClock.Restart();
            _liveRecordingLevels.Clear();
            ManualRecordingStatusText.Text = capture.FallbackWarning ?? $"Recording {capture.CaptureDeviceName}. Stop when your moment is captured.";
            StatusText.Text = "Recording started. Stop and save when ready.";
        }
        catch (Exception ex)
        {
            await Task.Run(capture.Dispose);
            ManualRecordingStatusText.Text = $"Could not start recording: {ex.Message}";
            StatusText.Text = ManualRecordingStatusText.Text;
        }
        UpdateRecordingControls();
    }

    private async Task FinishManualRecordingAsync()
    {
        var capture = _manualCapture;
        if (capture is null || _manualFinalizing) return;
        _manualCapture = null;
        _manualClock.Stop();
        _manualFinalizing = true;
        bool mic = capture.IsMicRunning;
        UpdateRecordingControls();
        ManualRecordingStatusText.Text = "Saving your recording…";
        try
        {
            await Task.Run(capture.Dispose);
            while (_savingRecording) await Task.Delay(50);
            await SaveCapturedRecordingAsync(capture, "Recording", 0, mic);
            ManualRecordingStatusText.Text = StatusText.Text;
        }
        catch (Exception ex)
        {
            ManualRecordingStatusText.Text = $"Could not finish recording: {ex.Message}";
            AppLogger.Error("Recording", "Stop capture failed", ex);
        }
        finally
        {
            _manualFinalizing = false;
            if (!_recordingWorkspaceClosed) UpdateRecordingControls();
            CompletePendingRecordingClose();
        }
    }

    private async Task SaveCapturedRecordingAsync(InstantReplayService capture, string prefix, int seconds, bool includeMic)
    {
        if (_savingRecording || !_recordingIndexReady) return;
        _savingRecording = true;
        UpdateRecordingControls();
        try
        {
            // Snapshot before returning to the dispatcher, so pausing or changing
            // the capture source cannot change what this Save action represents.
            var system = capture.GetRecentSamples();
            var mic = includeMic ? capture.GetRecentMicSamples() : null;
            var source = capture.CaptureDeviceName + (includeMic ? " + microphone" : "");
            int max = seconds > 0 ? seconds * CachedSound.TargetFormat.SampleRate * 2 : int.MaxValue;
            var clip = await Task.Run(() => WriteRecording(RecordingSamples.MixRecent(system, mic, max), prefix, source));
            if (clip is null)
            {
                StatusText.Text = "No audio captured yet. Play some audio on the capture source and try again.";
                return;
            }
            _recordings.Insert(0, clip);
            SaveRecordingHistory();
            if (!_recordingWorkspaceClosed) RefreshRecordingCards();
            StatusText.Text = $"Saved {clip.Sound.DisplayName}. Preview or trim it in Recording.";
        }
        catch (Exception ex)
        {
            StatusText.Text = $"Could not save recording: {ex.Message}";
            AppLogger.Error("Recording", "Save recording failed", ex);
        }
        finally
        {
            _savingRecording = false;
            if (!_recordingWorkspaceClosed) UpdateRecordingControls();
            CompletePendingRecordingClose();
        }
    }

    private void CompletePendingRecordingClose()
    {
        if (_closeAfterRecordingSave && !_savingRecording && !_manualFinalizing)
        {
            _closeAfterRecordingSave = false;
            Dispatcher.BeginInvoke(new Action(Close));
        }
    }

    private static RecordingClip? WriteRecording(float[] samples, string prefix, string source)
    {
        if (samples.Length == 0) return null;
        Directory.CreateDirectory(RecordingLibraryService.DirectoryPath);
        var item = new SoundItem
        {
            DisplayName = $"{prefix} {DateTime.Now:MMM d HH-mm-ss}",
            Category = "Clips", CreatedAt = DateTime.UtcNow
        };
        item.FilePath = System.IO.Path.Combine(RecordingLibraryService.DirectoryPath, item.Id + ".wav");
        try
        {
            using var writer = new WaveFileWriter(item.FilePath, CachedSound.TargetFormat);
            writer.WriteSamples(samples, 0, samples.Length);
        }
        catch
        {
            if (File.Exists(item.FilePath)) File.Delete(item.FilePath);
            throw;
        }
        return new RecordingClip
        {
            Sound = item, Source = source,
            DurationSeconds = samples.Length / (48000.0 * 2),
            Peaks = RecordingSamples.Peaks(samples)
        };
    }

    private void SaveRecordingHistory() => RecordingLibraryService.Save(_recordings);

    private void RefreshRecordingCards()
    {
        _recordingTimelines.Clear();
        RecentRecordingsPanel.Children.Clear();
        EmptyRecordingsText.Visibility = _recordings.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
        RecordingCountText.Text = _recordings.Count == 0 ? "Your clips will appear here." : $"{_recordings.Count} saved clips · newest first";
        foreach (var clip in _recordings)
        {
            var item = clip.Sound;
            var content = new StackPanel();
            content.Children.Add(new TextBlock { Text = item.DisplayName, FontSize = 15, FontWeight = FontWeights.Bold, TextWrapping = TextWrapping.Wrap });
            content.Children.Add(new TextBlock
            {
                Text = $"{item.CreatedAt.ToLocalTime():MMM d, HH:mm} · {TimeSpan.FromSeconds(clip.DurationSeconds):mm\\:ss}",
                Foreground = (Brush)FindResource("TextFillColorSecondaryBrush"), FontSize = 11, Margin = new Thickness(0, 4, 0, 8)
            });
            var timeline = new Controls.RecordingTimeline();
            timeline.SetWaveform(clip.Peaks);
            timeline.SetProgress(_recordingPositions.GetValueOrDefault(item.Id), clip.DurationSeconds);
            timeline.ScrubStarted += () => BeginRecordingScrub(item.Id);
            timeline.SeekChanged += seconds => SeekRecording(item.Id, seconds);
            timeline.ScrubCompleted += seconds => EndRecordingScrub(item.Id, seconds);
            _recordingTimelines[item.Id] = timeline;
            content.Children.Add(timeline);
            var actions = new WrapPanel();
            var play = new UiButton { Content = _previewRecordingId == item.Id && _recordingPreview is not null ? "Ⅱ Pause" : "▶ Play", Padding = new Thickness(8, 5, 8, 5), Margin = new Thickness(0, 0, 6, 6), ToolTip = "Preview on your monitor output" };
            play.Click += async (_, _) => await PreviewRecordingAsync(clip);
            var trim = new UiButton { Content = "✂ Trim", Padding = new Thickness(8, 5, 8, 5), Margin = new Thickness(0, 0, 6, 6) };
            trim.Click += async (_, _) =>
            {
                trim.IsEnabled = false;
                try { await TrimRecordingAsync(clip); }
                finally { trim.IsEnabled = true; }
            };
            var add = new UiButton { Content = "＋ Add to soundboard", Appearance = ControlAppearance.Primary, Padding = new Thickness(8, 5, 8, 5), Margin = new Thickness(0, 0, 0, 6), ToolTip = $"Add a copy to {_activeDeck.Name}" };
            add.Click += async (_, _) =>
            {
                add.IsEnabled = false;
                try { await AddRecordingToDeckAsync(clip); }
                finally { add.IsEnabled = true; }
            };
            var delete = new UiButton
            {
                Content = "Delete", Appearance = ControlAppearance.Danger,
                Icon = new Wpf.Ui.Controls.SymbolIcon { Symbol = Wpf.Ui.Controls.SymbolRegular.Delete20 },
                Padding = new Thickness(8, 5, 8, 5), Margin = new Thickness(6, 0, 0, 6),
                ToolTip = "Delete this saved recording"
            };
            delete.Click += (_, _) => DeleteRecording(clip);
            actions.Children.Add(play); actions.Children.Add(trim); actions.Children.Add(add); actions.Children.Add(delete);
            content.Children.Add(actions);
            RecentRecordingsPanel.Children.Add(new Border
            {
                Child = content, Padding = new Thickness(12), Margin = new Thickness(0, 0, 0, 12), CornerRadius = new CornerRadius(6),
                BorderBrush = (Brush)FindResource("ComicInkBrush"), BorderThickness = new Thickness(2),
                Background = (Brush)FindResource("ApplicationBackgroundBrush")
            });
        }
    }

    private void DeleteRecording(RecordingClip clip)
    {
        if (!_recordings.Contains(clip)) return;
        if (new ConfirmDeleteRecordingDialog(this, clip.Sound.DisplayName).ShowDialog() != true)
            return;
        try
        {
            // Also cancel any preview that is still loading this clip.
            StopRecordingPreview();
            RecordingLibraryService.Delete(_recordings, clip);
            _recordings.Remove(clip);
            _recordingPositions.Remove(clip.Sound.Id);
            RefreshRecordingCards();
            StatusText.Text = $"Deleted recording: {clip.Sound.DisplayName}";
        }
        catch (Exception ex)
        {
            StatusText.Text = $"Could not delete recording: {ex.Message}";
            AppLogger.Error("Recording", "Delete failed", ex);
        }
    }

    private static void DrawWaveform(Canvas canvas, float[] peaks)
    {
        canvas.Children.Clear();
        double width = canvas.ActualWidth, height = canvas.ActualHeight;
        if (width <= 0 || height <= 0 || peaks.Length == 0) return;
        var drawing = new StreamGeometry();
        using (var context = drawing.Open())
        {
            for (int i = 0; i < peaks.Length; i++)
            {
                double x = (i + .5) * width / peaks.Length;
                double amplitude = Math.Max(1, Math.Clamp(peaks[i], 0, 1) * (height / 2 - 2));
                context.BeginFigure(new Point(x, height / 2 - amplitude), false, false);
                context.LineTo(new Point(x, height / 2 + amplitude), true, false);
            }
        }
        drawing.Freeze();
        canvas.Children.Add(new System.Windows.Shapes.Path
        {
            Data = drawing, Stroke = new SolidColorBrush(Color.FromRgb(105, 228, 222)),
            StrokeThickness = Math.Clamp(width / peaks.Length * .55, 1, 3), StrokeStartLineCap = PenLineCap.Round, StrokeEndLineCap = PenLineCap.Round
        });
    }

    private void LiveRecordingWaveform_SizeChanged(object sender, SizeChangedEventArgs e)
        => DrawWaveform(LiveRecordingWaveform, _liveRecordingLevels.ToArray());

    private async Task PreviewRecordingAsync(RecordingClip clip)
    {
        if (_previewRecordingId == clip.Sound.Id && _previewSound is not null)
        {
            if (_recordingPreview is not null) PauseRecordingPreview();
            else
            {
                double position = _recordingPositions.GetValueOrDefault(clip.Sound.Id);
                StartRecordingPreviewAt(position >= _previewSound.Duration.TotalSeconds ? 0 : position);
                RefreshRecordingCards();
            }
            return;
        }
        StopRecordingPreview();
        int request = _previewRequest;
        try
        {
            var item = CloneRecordingSound(clip.Sound);
            var sound = await Task.Run(() => RenderRecording(item));
            if (_recordingWorkspaceClosed || request != _previewRequest) return;
            if (_monitorEngine is null || MonitorComboBox.SelectedItem is not AudioDevice device || device.IsNone)
                throw new InvalidOperationException("Select a monitor output in Settings to preview recordings.");
            if (sound.TotalSamples == 0)
                throw new InvalidOperationException("This clip has no audio after trimming.");
            // Share the already-open monitor mixer, just like the sound editor.
            // A separate waveOut stream can stop immediately on devices that are
            // already in use, even though rendering the clip succeeded.
            _recordingPreviewEngine = _monitorEngine;
            _previewSound = sound;
            _previewRecordingId = item.Id;
            double position = _recordingPositions.GetValueOrDefault(item.Id);
            StartRecordingPreviewAt(position >= sound.Duration.TotalSeconds ? 0 : position);
            RefreshRecordingCards();
            StatusText.Text = $"Previewing {item.DisplayName} on {device.Name}.";
        }
        catch (Exception ex)
        {
            StopRecordingPreview();
            StatusText.Text = $"Could not preview recording: {ex.Message}";
            AppLogger.Error("Recording", "Preview failed", ex);
        }
    }

    private double CurrentPreviewSeconds => Math.Clamp(_previewBaseSeconds + _previewClock.Elapsed.TotalSeconds,
        0, _previewSound?.Duration.TotalSeconds ?? 0);

    private void UpdateRecordingPlaybackPosition()
    {
        if (_previewRecordingId is not { } id || _previewSound is null) return;
        double seconds = CurrentPreviewSeconds;
        _recordingPositions[id] = seconds;
        if (_scrubbingRecordingId != id && _recordingTimelines.TryGetValue(id, out var timeline))
            timeline.SetProgress(seconds, _previewSound.Duration.TotalSeconds);
    }

    private void PauseRecordingPreview(bool refreshCards = true)
    {
        UpdateRecordingPlaybackPosition();
        _previewBaseSeconds = CurrentPreviewSeconds;
        _previewClock.Reset();
        _previewTimelineTimer?.Stop();
        if (_recordingPreview is not null)
            _recordingPreviewEngine?.StopOne(_recordingPreview.TopProvider);
        _recordingPreview = null;
        if (refreshCards) RefreshRecordingCards();
    }

    private void StartRecordingPreviewAt(double seconds)
    {
        if (_previewSound is null || _recordingPreviewEngine is null || _previewRecordingId is not { } id) return;
        int start = (int)(Math.Clamp(seconds, 0, _previewSound.Duration.TotalSeconds) * _previewSound.SampleRate) * _previewSound.Channels;
        start = Math.Clamp(start, 0, _previewSound.TotalSamples);
        _previewBaseSeconds = start / (double)(_previewSound.SampleRate * _previewSound.Channels);
        _recordingPositions[id] = _previewBaseSeconds;
        _previewClock.Reset();
        if (start >= _previewSound.TotalSamples) return;
        _recordingPreview = _recordingPreviewEngine.Play(_previewSound, 1f, start, _previewSound.TotalSamples, 0, 0);
        _previewClock.Start();
        _previewTimelineTimer?.Start();
    }

    private void BeginRecordingScrub(Guid id)
    {
        _resumeAfterScrubbing = _previewRecordingId == id && _recordingPreview is not null;
        if (_previewRecordingId == id) PauseRecordingPreview(refreshCards: false);
        else StopRecordingPreview(refreshCards: false);
        _scrubbingRecordingId = id;
    }

    private void SeekRecording(Guid id, double seconds)
    {
        // Slider ValueChanged also supports arrow-key seeking when no mouse drag is active.
        bool resume = _scrubbingRecordingId is null && _previewRecordingId == id && _recordingPreview is not null;
        if (resume) PauseRecordingPreview(refreshCards: false);
        _recordingPositions[id] = seconds;
        if (_previewRecordingId != id) return;
        _previewBaseSeconds = seconds;
        _previewClock.Reset();
        if (resume) StartRecordingPreviewAt(seconds);
    }

    private void EndRecordingScrub(Guid id, double seconds)
    {
        if (_scrubbingRecordingId != id) return;
        _scrubbingRecordingId = null;
        _recordingPositions[id] = seconds;
        if (_previewRecordingId == id)
        {
            _previewBaseSeconds = seconds;
            if (_resumeAfterScrubbing) StartRecordingPreviewAt(seconds);
        }
        _resumeAfterScrubbing = false;
        RefreshRecordingCards();
    }

    private static CachedSound RenderRecording(SoundItem item)
    {
        var raw = new CachedSound(item.FilePath);
        var rendered = EffectProcessor.Render(raw, GetSegments(raw, item), item.ReverseAudio, item.NormalizeAudio, EffectProcessor.GetSafePlaybackSpeed(item));
        var sound = new CachedSound(rendered);
        var (_, _, fadeIn, fadeOut) = GetTrimFadeParams(raw, item);
        // Bake fades and perceptual volume so preview matches the deck playback.
        var provider = new CachedSoundSampleProvider(sound, 0, sound.TotalSamples, fadeIn, fadeOut);
        var result = new float[sound.TotalSamples];
        int read = provider.Read(result, 0, result.Length);
        float gain = ConvertUiVolumeToGain(item.Volume);
        for (int i = 0; i < read; i++) result[i] *= gain;
        return new CachedSound(result);
    }

    private void StopRecordingPreview(bool refreshCards = true)
    {
        UpdateRecordingPlaybackPosition();
        _previewTimelineTimer?.Stop();
        _previewClock.Reset();
        _previewBaseSeconds = 0;
        _previewSound = null;
        _previewRequest++;
        var output = _recordingPreview;
        _recordingPreview = null;
        _previewRecordingId = null;
        if (output is not null)
            _recordingPreviewEngine?.StopOne(output.TopProvider);
        _recordingPreviewEngine = null;
        if (output is not null && !_recordingWorkspaceClosed) StatusText.Text = "Recording preview stopped.";
        if (refreshCards && output is not null && !_recordingWorkspaceClosed) RefreshRecordingCards();
    }

    private async Task TrimRecordingAsync(RecordingClip clip)
    {
        StopRecordingPreview();
        try
        {
            var sound = await Task.Run(() => new CachedSound(clip.Sound.FilePath));
            if (_recordingWorkspaceClosed || !_recordings.Contains(clip)) return;
            var dlg = new EditSoundDialog(this, clip.Sound, sound, new[] { "Clips" }, [], _monitorEngine);
            if (dlg.ShowDialog() != true) return;
            var item = clip.Sound;
            item.DisplayName = dlg.ResultName; item.Category = dlg.ResultCategory; item.Tags = dlg.ResultTags;
            item.Volume = dlg.ResultVolume; item.TrimStartSeconds = dlg.ResultTrimStart; item.TrimEndSeconds = dlg.ResultTrimEnd;
            item.FadeInSeconds = dlg.ResultFadeIn; item.FadeOutSeconds = dlg.ResultFadeOut; item.Segments = dlg.ResultSegments;
            item.ReverseAudio = dlg.ResultReverseAudio; item.NormalizeAudio = dlg.ResultNormalizeAudio; item.PlaybackSpeed = dlg.ResultPlaybackSpeed;
            // A recent recording has no global hotkey until it belongs to a deck.
            item.Hotkey = null;
            var rendered = await Task.Run(() => RenderRecording(CloneRecordingSound(item)));
            clip.DurationSeconds = rendered.Duration.TotalSeconds;
            clip.Peaks = RecordingSamples.Peaks(rendered.AudioData);
            _recordingPositions.Remove(item.Id);
            SaveRecordingHistory();
            RefreshRecordingCards();
            StatusText.Text = $"Updated {item.DisplayName}. Original recording preserved.";
        }
        catch (Exception ex) { StatusText.Text = $"Could not edit recording: {ex.Message}"; }
    }

    private static SoundItem CloneRecordingSound(SoundItem item)
        => JsonSerializer.Deserialize<SoundItem>(JsonSerializer.Serialize(item))!;

    private async Task AddRecordingToDeckAsync(RecordingClip clip)
    {
        var deck = _activeDeck;
        try
        {
            var item = CloneRecordingSound(clip.Sound);
            item.Id = Guid.NewGuid(); item.Hotkey = null; item.HotkeyInitialized = true;
            Directory.CreateDirectory(AppPaths.SoundsDirectory);
            string destination = System.IO.Path.Combine(AppPaths.SoundsDirectory, item.Id + ".wav");
            var cached = await Task.Run(() =>
            {
                File.Copy(item.FilePath, destination);
                return new CachedSound(destination);
            });
            if (_recordingWorkspaceClosed) return;
            item.FilePath = destination;
            if (!_decks.Contains(deck))
            {
                File.Delete(destination);
                throw new InvalidOperationException("The destination deck was removed. Please choose another deck.");
            }
            deck.Sounds.Add(item);
            try { DeckService.Save(_decks); }
            catch
            {
                deck.Sounds.Remove(item);
                File.Delete(destination);
                throw;
            }
            if (deck == _activeDeck)
            {
                _cachedSounds[item.Id] = cached;
                RefreshCategoryFilter(); FilterSoundsPanel(); ActiveDeckChanged?.Invoke(deck);
            }
            StatusText.Text = $"Added {item.DisplayName} to {deck.Name}.";
        }
        catch (Exception ex) { StatusText.Text = $"Could not add recording: {ex.Message}"; }
    }

    private void ShutdownRecordingWorkspace()
    {
        _recordingWorkspaceClosed = true;
        _recordingTimer?.Stop();
        _previewTimelineTimer?.Stop();
        StopRecordingPreview();
        // Window_Closing finishes and saves manual capture asynchronously before
        // allowing shutdown, avoiding capture-disposal deadlocks on the UI thread.
    }
}
