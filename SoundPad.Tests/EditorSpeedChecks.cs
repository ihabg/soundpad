using System.Reflection;
using System.Windows;
using System.Windows.Controls;
using SoundPad.App.Audio;
using SoundPad.App.Dialogs;
using SoundPad.App.Models;

internal static class EditorSpeedChecks
{
    internal static void Run(Action<bool, string> check)
    {
        Exception? error = null;
        var thread = new Thread(() =>
        {
            try
            {
                var app = new SoundPad.App.App();
                app.ShutdownMode = ShutdownMode.OnExplicitShutdown;
                app.InitializeComponent();
                SharedEffectsChecks.Run(check);
                OverlapPlaybackChecks.Run(check);
                var owner = new Window();
                new System.Windows.Interop.WindowInteropHelper(owner).EnsureHandle();
                var samples = new float[60 * 96000];
                // A tone in the final second must survive the slowed render.
                for (int i = 59 * 96000; i < samples.Length; i++) samples[i] = (float)(.4 * Math.Sin(i * .04));
                var sound = new CachedSound(samples);
                var dialog = new EditSoundDialog(owner, new SoundItem(), sound, [], [], null);
                object? Call(string name, params object[] args) => typeof(EditSoundDialog).GetMethod(name, BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(dialog, args);
                var speed = (Slider)((SoundPad.App.Controls.AudioEffectsPanel)dialog.FindName("EffectsPanel")).GetEditor("speed");
                speed.Value = .5;
                check(Math.Abs((double)Call("ComputeEditedDuration")! - 120) < .001,
                    "Editor timeline doubles a 60-second sound to 120 seconds at half speed");
                check(((TextBlock)dialog.FindName("DurationText")).Text.Contains("120.00"),
                    "Changing speed immediately refreshes the visible duration");
                var ruler = (Canvas)dialog.FindName("RulerCanvas");
                ruler.Measure(new Size(600, 28)); ruler.Arrange(new Rect(0, 0, 600, 28));
                Call("RedrawRuler");
                check(ruler.Children.OfType<TextBlock>().Any(t => t.Text == "2:00"),
                    "Half-speed timeline ruler shows the full two-minute playback range");
                void Set(string field, object value) => typeof(EditSoundDialog).GetField(field, BindingFlags.Instance | BindingFlags.NonPublic)!.SetValue(dialog, value);
                var provider = new CachedSoundSampleProvider(sound);
                Set("_previewHandle", new PlaybackHandle(provider, provider));
                Set("_playStartVisualSec", 0d); Set("_playStartTime", DateTime.Now.AddSeconds(-90));
                Call("TickPlayhead");
                check(typeof(EditSoundDialog).GetField("_previewHandle", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(dialog) is not null,
                    "Slowed preview remains active beyond the original sixty-second cutoff");
                Call("StopPreview");
                speed.Value = 2;
                check(Math.Abs((double)Call("ComputeEditedDuration")! - 30) < .001,
                    "Editor timeline halves a 60-second sound to 30 seconds at double speed");
                speed.Value = .5;
                var sourceSegments = (List<(int S, int E)>)Call("ComputeEditorSegments", 90d)!;
                check(sourceSegments.Single() == (45 * 96000, 60 * 96000),
                    "Seeking in a slowed timeline maps to the correct original audio position");
                Call("SplitBlockAtVisualTime", 90d);
                var segments = (List<AudioSegment>)typeof(EditSoundDialog).GetField("_segments", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(dialog)!;
                check(segments.Count == 2 && segments[0].EndSeconds == 45 && segments[1].StartSeconds == 45,
                    "Cutting a slowed timeline preserves source-based segment boundaries");
                check(Math.Abs((double)Call("ComputeEditedDuration")! - 120) < .001,
                    "Splitting a slowed sound preserves its complete playback duration");
                speed.Value = 1;
                check(Math.Abs((double)Call("ComputeEditedDuration")! - 60) < .001,
                    "Resetting speed restores the original timeline duration");
                var effectPanel = (SoundPad.App.Controls.AudioEffectsPanel)dialog.FindName("EffectsPanel");
                effectPanel.GetSlider("echo").Value = 40;
                effectPanel.GetSlider("reverb").Value = 30;
                check(Math.Abs((double)Call("ComputeEditedDuration")! - 62.5) < .001,
                    "Edit timeline includes complete echo and reverb tails after split source blocks");
                var blocks = ((System.Collections.IEnumerable)Call("BuildVisualBlocks")!).Cast<object>().ToList();
                double visualEnd = (double)blocks[^1].GetType().GetProperty("VisualEnd")!.GetValue(blocks[^1])!;
                check(Math.Abs(visualEnd - 62.5) < .001,
                    "Split waveform appends the effect tail once rather than once per source block");
                Call("SplitBlockAtVisualTime", 10d);
                check(segments[0].EndSeconds == 10 && Math.Abs((double)Call("ComputeEditedDuration")! - 62.5) < .001,
                    "Echo and reverb tails do not move source cut positions or shorten playback");
                dialog.Close();

                var edited = new EditSoundDialog(owner, new SoundItem { PlaybackSpeed = .5,
                    Segments = [new AudioSegment(10, 20), new AudioSegment(40, 60)] }, sound, [], [], null);
                double duration = (double)typeof(EditSoundDialog).GetMethod("ComputeEditedDuration", BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(edited, null)!;
                check(Math.Abs(duration - 60) < .001, "Reopening a saved slow-speed sound scales the duration of all retained blocks");
                edited.Close(); owner.Close();

                var rendered = EffectProcessor.Render(sound, [(0, sound.TotalSamples)], false, false, .5);
                check(Math.Abs(rendered.Length / 96000d - 120) < .01,
                    "Half-speed rendering retains the full two-minute output");
                check(rendered.Skip(rendered.Length - 96000).Any(s => Math.Abs(s) > .1),
                    "Audio from the final source second survives at the end of the slowed output");
            }
            catch (Exception ex) { error = ex; }
        });
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start(); thread.Join();
        if (error is not null) throw new Exception("Sound editor speed regression failed", error);
    }
}
