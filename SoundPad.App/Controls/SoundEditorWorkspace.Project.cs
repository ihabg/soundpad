using System.IO;
using System.Text.Json;
using System.Windows;
using System.Windows.Threading;
using Microsoft.Win32;
using NAudio.Wave;
using SoundPad.App.Audio;
using SoundPad.App.Models;
using SoundPad.App.Services;

namespace SoundPad.App.Controls;

public partial class SoundEditorWorkspace
{
    private DispatcherTimer? _draftTimer;
    private bool _restoringDraft;
    private bool _disposed;
    private bool _exporting;
    private bool _importing;

    public void UpdateSources(IReadOnlyDictionary<Guid, CachedSound> sounds, IReadOnlyList<SoundItem> items)
    {
        foreach (var item in items)
        {
            if (!sounds.TryGetValue(item.Id, out var sound)) continue;
            int existing = _deckSounds.FindIndex(s => s.Id == item.Id);
            bool used = _tracks.Any(t => t.Clips.Any(c => c.SoundItemId == item.Id));
            if (existing < 0) _deckSounds.Add(JsonSerializer.Deserialize<SoundItem>(JsonSerializer.Serialize(item))!);
            else if (!used) _deckSounds[existing] = JsonSerializer.Deserialize<SoundItem>(JsonSerializer.Serialize(item))!;
            _cachedSounds.TryAdd(item.Id, sound);
        }
    }

    public void AddSource(SoundItem item)
    {
        if (_saving || _exporting || _importing) { StatusText.Text = "Wait for the current action to finish, then add this sound."; return; }
        if (!_cachedSounds.ContainsKey(item.Id)) { StatusText.Text = "This sound could not be loaded."; return; }
        if (_tracks.Count >= MaxTracks) { StatusText.Text = "This mix already has 8 tracks. Add clips to an existing track instead."; return; }
        StopAll();
        PushUndo();
        AddTrackWithClip(item, 0);
        var clip = _tracks[^1].Clips[0];
        _selectedClipId = clip.Id;
        RebuildTimeline();
        Dispatcher.BeginInvoke(DispatcherPriority.Loaded, new Action(() => { if (!_disposed) ZoomFit_Click(this, new RoutedEventArgs()); }));
        StatusText.Text = $"Added {item.DisplayName}. Arrange it on the timeline, or add another sound.";
    }

    public void StopPlayback() => StopAll();

    public bool UsesSourceFile(string path)
    {
        var ids = _tracks.SelectMany(t => t.Clips).Select(c => c.SoundItemId).ToHashSet();
        return _deckSounds.Any(s => ids.Contains(s.Id) && string.Equals(Path.GetFullPath(s.FilePath), Path.GetFullPath(path), StringComparison.OrdinalIgnoreCase));
    }

    private void InitializeDraft()
    {
        _draftTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(700) };
        _draftTimer.Tick += (_, _) => FlushDraft();
        _restoringDraft = true;
        try
        {
            var draft = SoundEditorProjectService.Load();
            if (draft is null) return;
            foreach (var item in draft.Sources)
            {
                int existing = _deckSounds.FindIndex(s => s.Id == item.Id);
                if (existing < 0) _deckSounds.Add(item);
                else _deckSounds[existing] = item;
                if (_cachedSounds.ContainsKey(item.Id) || !File.Exists(item.FilePath)) continue;
                try { _cachedSounds[item.Id] = new CachedSound(item.FilePath); }
                catch (Exception ex) { AppLogger.Error("SoundEditor", "Could not restore a draft source", ex); }
            }
            _tracks.AddRange(draft.Tracks);
            _pixelsPerSecond = double.IsFinite(draft.Zoom) ? Math.Clamp(draft.Zoom, MinPixelsPerSecond, MaxPixelsPerSecond) : 100;
            _playheadSeconds = double.IsFinite(draft.PlayheadSeconds) ? Math.Clamp(draft.PlayheadSeconds, 0, 600) : 0;
            MixNameBox.Text = draft.Name;
            StatusText.Text = "Restored your last mix. Drafts are saved locally as you edit.";
        }
        catch (Exception ex)
        {
            if (File.Exists(SoundEditorProjectService.DraftPath))
            {
                try { File.Copy(SoundEditorProjectService.DraftPath, SoundEditorProjectService.DraftPath + $".bak-{DateTime.UtcNow:yyyyMMddHHmmssfff}"); }
                catch (Exception backupError) { AppLogger.Error("SoundEditor", "Draft backup failed", backupError); }
            }
            StatusText.Text = "Could not restore the previous draft. A backup was kept.";
            AppLogger.Error("SoundEditor", "Draft restore failed", ex);
        }
        finally { _restoringDraft = false; }
    }

    private void ScheduleDraft()
    {
        if (_restoringDraft || _disposed || _draftTimer is null) return;
        _draftTimer.Stop();
        _draftTimer.Start();
    }

    private void FlushDraft()
    {
        _draftTimer?.Stop();
        if (_restoringDraft) return;
        try
        {
            SoundEditorProjectService.Save(new SoundEditorProject
            {
                Name = MixNameBox.Text,
                Tracks = DeepCopyTracks(), Sources = BuildSoundItemMap().Values.ToList(),
                Zoom = _pixelsPerSecond, PlayheadSeconds = _playheadSeconds
            });
        }
        catch (Exception ex)
        {
            StatusText.Text = "Your draft could not be saved. Keep this editor open until you save the mix.";
            AppLogger.Error("SoundEditor", "Draft save failed", ex);
        }
    }

    private bool AllSourcesAvailable()
    {
        var missing = _tracks.SelectMany(t => t.Clips).Select(c => c.SoundItemId)
            .Where(id => !_cachedSounds.ContainsKey(id) || FindSoundItem(id) is null).Distinct().Count();
        if (missing == 0) return true;
        StatusText.Text = $"{missing} source sounds are missing. Remove their clips or restore their audio files before rendering.";
        return false;
    }

    private void UpdateTransportTime()
    {
        double duration = _tracks.SelectMany(t => t.Clips).Select(c => c.StartOffsetSeconds + GetClipEffectiveDuration(c)).DefaultIfEmpty(0).Max();
        TransportTimeText.Text = $"{TimeSpan.FromSeconds(Math.Max(0, _playheadSeconds)):mm\\:ss\\.f} / {TimeSpan.FromSeconds(duration):mm\\:ss\\.f}";
    }

    private void Split_Click(object sender, RoutedEventArgs e) => SplitSelectedClipAtPlayhead();
    private void Copy_Click(object sender, RoutedEventArgs e) => CopyClip();
    private void Paste_Click(object sender, RoutedEventArgs e) => PasteClip();
    private void Duplicate_Click(object sender, RoutedEventArgs e) => DuplicateClip();
    private void RemoveClip_Click(object sender, RoutedEventArgs e) => DeleteSelectedClip();

    private async void ImportAudio_Click(object sender, RoutedEventArgs e)
    {
        if (IsSaving) return;
        if (_tracks.Count >= MaxTracks) { StatusText.Text = "This mix already has 8 tracks."; return; }
        var picker = new OpenFileDialog
        {
            Title = "Import audio into Sound Editor", Multiselect = true,
            Filter = "Audio files|*.wav;*.mp3;*.ogg;*.flac;*.aac;*.m4a|All files|*.*"
        };
        if (picker.ShowDialog(Window.GetWindow(this)) != true) return;
        _importing = true;
        ImportAudioButton.IsEnabled = false;
        int added = 0;
        try
        {
            StopAll();
            foreach (var path in picker.FileNames.Take(MaxTracks - _tracks.Count))
            {
                var source = await Task.Run(() =>
                {
                    string managed = SoundLibraryService.ImportFile(path);
                    var sound = new CachedSound(managed);
                    var item = new SoundItem { DisplayName = Path.GetFileNameWithoutExtension(path), FilePath = managed, Category = "Imported", HotkeyInitialized = true };
                    return (Item: item, Sound: sound);
                });
                if (_disposed) return;
                PushUndo();
                _deckSounds.Add(source.Item); _cachedSounds[source.Item.Id] = source.Sound;
                AddTrackWithClip(source.Item, 0);
                _selectedClipId = _tracks[^1].Clips[0].Id;
                added++;
                RebuildTimeline();
            }
            ZoomFit_Click(this, e);
            StatusText.Text = $"Imported {added} sounds into your mix. Originals remain unchanged.";
        }
        catch (Exception ex) { StatusText.Text = $"Imported {added} sounds; could not finish: {ex.Message}"; }
        finally { _importing = false; ImportAudioButton.IsEnabled = true; FlushDraft(); SaveFinished?.Invoke(); }
    }

    private async void ExportWav_Click(object sender, RoutedEventArgs e)
    {
        if (IsSaving || !HasClips() || !AllSourcesAvailable()) return;
        var picker = new SaveFileDialog { Title = "Export your mix", Filter = "WAV audio|*.wav", FileName = "SoundPad Mix.wav", DefaultExt = ".wav" };
        if (picker.ShowDialog(Window.GetWindow(this)) != true) return;
        StopAll();
        _exporting = true;
        ExportWavButton.IsEnabled = false;
        var tracks = DeepCopyTracks(); var items = BuildSoundItemMap(); var sounds = _cachedSounds.ToDictionary();
        try
        {
            await Task.Run(() =>
            {
                var mix = MixRenderer.Render(tracks, items, sounds);
                if (mix.Length == 0) throw new InvalidOperationException("This mix has no audio.");
                using var writer = new WaveFileWriter(picker.FileName, CachedSound.TargetFormat);
                writer.WriteSamples(mix, 0, mix.Length);
            });
            StatusText.Text = "WAV exported. Your editable mix stays open.";
        }
        catch (Exception ex) { StatusText.Text = $"Could not export mix: {ex.Message}"; }
        finally { _exporting = false; ExportWavButton.IsEnabled = true; SaveFinished?.Invoke(); }
    }

    public void Dispose()
    {
        if (_disposed) return;
        StopAll();
        FlushDraft();
        _disposed = true;
        _draftTimer?.Stop();
    }
}
