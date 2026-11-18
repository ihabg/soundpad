using System.IO;
using System.Reflection;
using System.Windows.Controls;
using SoundPad.App.Audio;
using SoundPad.App.Controls;
using SoundPad.App.Models;
using SoundPad.App.Services;

internal static class SharedEffectsChecks
{
    internal static void Run(Action<bool, string> check)
    {
        var panel = new AudioEffectsPanel();
        check(AudioEffectCatalog.Definitions.All(effect => panel.GetEditor(effect.Id) is CheckBox or Slider),
            "Shared effects UI generates a control for every registered effect");
        var legacy = new SoundItem { ReverseAudio = true, PlaybackSpeed = .5 };
        check(AudioEffectCatalog.FromItem(legacy)["reverse"] == 1 && EffectProcessor.GetSafePlaybackSpeed(legacy) == .5,
            "Existing reverse, normalize, and speed settings remain compatible");
        string directory = Path.Combine(AppContext.BaseDirectory, "TestData", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        string draft = Path.Combine(directory, "draft.json");
        var samples = new float[192000];
        for (int i = 0; i < samples.Length; i++) samples[i] = (i % 997) / 5000f;
        var sound = new CachedSound(samples);
        var source = new SoundItem { DisplayName = "Shared effects fixture", FilePath = Path.Combine(directory, "fixture.wav"), ReverseAudio = true };
        using (var writer = new NAudio.Wave.WaveFileWriter(source.FilePath, CachedSound.TargetFormat)) writer.WriteSamples(samples, 0, samples.Length);
        var cache = new Dictionary<Guid, CachedSound> { [source.Id] = sound };
        SoundEditorWorkspace? editor = null;
        try
        {
            editor = new SoundEditorWorkspace(cache, [source], () => null, draft);
            object? Call(string name, params object[] args) => typeof(SoundEditorWorkspace).GetMethod(name, BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(editor, args);
            List<MixTrack> Tracks() => (List<MixTrack>)typeof(SoundEditorWorkspace).GetField("_tracks", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(editor)!;
            void Set(string field, object value) => typeof(SoundEditorWorkspace).GetField(field, BindingFlags.Instance | BindingFlags.NonPublic)!.SetValue(editor, value);
            editor.AddSource(source);
            var effects = (AudioEffectsPanel)editor.FindName("ClipEffectsPanel");
            check(((CheckBox)effects.GetEditor("reverse")).IsChecked == true,
                "Editor inspector inherits effects already assigned to the library sound");
            ((CheckBox)effects.GetEditor("normalize")).IsChecked = true;
            ((Slider)effects.GetEditor("speed")).Value = .5;
            var clip = Tracks().Single().Clips.Single();
            check(clip.Effects?["normalize"] == 1 && clip.Effects["speed"] == .5 && source.Effects is null && !source.NormalizeAudio && source.PlaybackSpeed == 1,
                "Editor effects affect only the selected clip and preserve the original library sound");
            check(Math.Abs((double)Call("GetClipEffectiveDuration", clip)! - 4) < .001,
                "Changing editor speed immediately scales the clip timeline duration");
            var mix = MixRenderer.Render(Tracks(), new Dictionary<Guid, SoundItem> { [source.Id] = source }, cache);
            var expected = EffectProcessor.Render(sound, [(0, sound.TotalSamples)], AudioEffectCatalog.Resolve(source, clip.Effects));
            float peak = expected.Max(s => MathF.Abs(s));
            if (peak > 1)
                for (int i = 0; i < expected.Length; i++) expected[i] *= 1f / peak;
            check(mix.SequenceEqual(expected), "Editor mix preview and export use the same effects pipeline as Edit Sound");
            Call("DuplicateClip");
            var duplicated = Tracks().Single().Clips;
            check(duplicated.Count == 2 && duplicated.All(c => c.Effects?["speed"] == .5) && !ReferenceEquals(duplicated[0].Effects, duplicated[1].Effects),
                "Duplicating a clip preserves effects without sharing mutable settings");
            Call("Undo");
            Set("_selectedClipId", Tracks().Single().Clips.Single().Id);
            Call("Undo");
            check(Tracks().Single().Clips.Single().Effects?["speed"] == 1, "Undo restores the previous clip effects");
            Call("Redo");
            clip = Tracks().Single().Clips.Single();
            check(clip.Effects?["speed"] == .5, "Redo restores changed clip effects");
            Set("_selectedClipId", clip.Id); Call("RefreshSelectedClipPanel");
            effects.Reset();
            check(AudioEffectCatalog.Definitions.Where(d => !d.IsPlaybackControl).All(d => Tracks().Single().Clips.Single().Effects![d.Id] == d.DefaultValue)
                && Tracks().Single().Clips.Single().Effects!["speed"] == .5,
                "Effects Reset clears effects while preserving the separate playback speed");
            Call("Undo");
            clip = Tracks().Single().Clips.Single();
            check(clip.Effects?["speed"] == .5 && clip.Effects["normalize"] == 1 && clip.Effects["reverse"] == 1,
                "One Undo restores all effects changed by Reset");
            Set("_selectedClipId", clip.Id); Call("RefreshSelectedClipPanel");
            var speed = (Slider)effects.GetEditor("speed");
            speed.RaiseEvent(new System.Windows.Controls.Primitives.DragStartedEventArgs(0, 0) { RoutedEvent = System.Windows.Controls.Primitives.Thumb.DragStartedEvent });
            speed.Value = .75; speed.Value = 1.25;
            speed.RaiseEvent(new System.Windows.Controls.Primitives.DragCompletedEventArgs(0, 0, false) { RoutedEvent = System.Windows.Controls.Primitives.Thumb.DragCompletedEvent });
            Call("Undo"); clip = Tracks().Single().Clips.Single();
            check(clip.Effects?["speed"] == .5, "One Undo restores the speed before an entire slider drag");
            Set("_selectedClipId", clip.Id); Set("_playheadSeconds", 2d);
            Call("SplitSelectedClipAtPlayhead");
            check(Tracks().Single().Clips.Count == 2 && Tracks().Single().Clips.All(c => c.Effects?["speed"] == .5 && c.Effects["normalize"] == 1),
                "Splitting a clip preserves its effects on both halves");
            editor.Dispose(); editor = null;
            var restored = SoundEditorProjectService.Load(draft)!;
            check(restored.Tracks.Single().Clips.All(c => c.Effects?["speed"] == .5 && c.Effects["normalize"] == 1),
                "Editor draft persistence retains per-clip effects across restart");
            editor = new SoundEditorWorkspace(cache, [source], () => null, draft);
            check(Math.Abs((double)Call("GetTotalDuration")! - 4) < .001,
                "Reopened editor draft keeps the correct effect-adjusted duration");
        }
        finally { editor?.Dispose(); Directory.Delete(directory, true); }
    }
}
