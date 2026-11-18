using System.Reflection;
using NAudio.Wave;
using SoundPad.App;
using SoundPad.App.Audio;
using SoundPad.App.Models;

internal static class OverlapPlaybackChecks
{
    // Called on the existing WPF test thread with application resources loaded.
    internal static void Run(Action<bool, string> check)
    {
        var settings = new AppSettings();
        var window = new MainWindow(settings, () => { }, () => { });
        using var output = new SilentOutput();
        using var engine = new AudioPlaybackEngine(output);
        void Set(string name, object value) => typeof(MainWindow).GetField(name, BindingFlags.NonPublic | BindingFlags.Instance)!.SetValue(window, value);
        object? Call(string name, params object[] args) => typeof(MainWindow).GetMethod(name, BindingFlags.NonPublic | BindingFlags.Instance)!.Invoke(window, args);
        var a = new SoundItem { DisplayName = "A" }; var b = new SoundItem { DisplayName = "B" };
        var deck = new Deck { Sounds = [a, b] };
        Set("_activeDeck", deck); Set("_decks", new List<Deck> { deck }); Set("_monitorEngine", engine);
        var cache = (Dictionary<Guid, CachedSound>)typeof(MainWindow).GetField("_cachedSounds", BindingFlags.NonPublic | BindingFlags.Instance)!.GetValue(window)!;
        cache[a.Id] = new CachedSound(Enumerable.Repeat(.1f, 4800).ToArray());
        cache[b.Id] = new CachedSound(Enumerable.Repeat(.2f, 9600).ToArray());
        bool Near(float[] data, float value) => data.All(s => Math.Abs(s - value) < .00001);
        try
        {
            var toggle = (Wpf.Ui.Controls.ToggleSwitch)window.FindName("InterruptSoundsSwitch");
            toggle.IsChecked = false;
            window.PlayLibraryItem(a); window.PlayLibraryItem(b);
            check(window.IsActivePlayback(a.Id) && window.IsActivePlayback(b.Id) && Near(output.Read(100), .3f),
                "Interrupt off mixes different sounds without stopping either one");
            window.PlayLibraryItem(a);
            check(Near(output.Read(100), .4f), "Interrupt off layers another copy when the same sound is triggered again");
            output.Read(4650); Call("PlaybackMonitor_Tick", null!, EventArgs.Empty);
            check(window.IsActivePlayback(a.Id) && window.IsActivePlayback(b.Id),
                "An older copy finishing keeps the newer copy and other sound active");
            window.StopSoundById(a.Id);
            check(!window.IsActivePlayback(a.Id) && window.IsActivePlayback(b.Id) && Near(output.Read(20), .2f),
                "Stopping one sound removes its copies while other sounds continue");
            Call("StopAllSounds");
            check(window.GetActivePlaybackIds().Count == 0 && Near(output.Read(20), 0), "Stop All clears every overlapping voice");
            toggle.IsChecked = true;
            check(settings.InterruptPreviousSounds && window.InterruptPreviousSounds, "The on-screen interrupt switch synchronizes the saved playback setting");
            window.PlayLibraryItem(a); window.PlayLibraryItem(b);
            check(!window.IsActivePlayback(a.Id) && window.IsActivePlayback(b.Id) && Near(output.Read(20), .2f),
                "Interrupt on stops previous audio before the new sound plays");
            toggle.IsChecked = false;
            window.PlayLibraryItem(a);
            check(!settings.InterruptPreviousSounds && Near(output.Read(20), .3f),
                "Switching interrupt off immediately restores overlapping playback");
        }
        finally { Call("StopAllSounds"); window.Close(); }
    }

    private sealed class SilentOutput : IWavePlayer
    {
        private IWaveProvider? _source;
        public PlaybackState PlaybackState { get; private set; }
        public float Volume { get; set; } = 1;
        public WaveFormat OutputWaveFormat => _source!.WaveFormat;
        public event EventHandler<StoppedEventArgs>? PlaybackStopped;
        public void Init(IWaveProvider waveProvider) => _source = waveProvider;
        public void Play() => PlaybackState = PlaybackState.Playing;
        public void Pause() => PlaybackState = PlaybackState.Paused;
        public void Stop() { PlaybackState = PlaybackState.Stopped; PlaybackStopped?.Invoke(this, new StoppedEventArgs()); }
        public void Dispose() { }
        internal float[] Read(int samples)
        {
            var bytes = new byte[samples * 4];
            _source!.Read(bytes, 0, bytes.Length);
            var result = new float[samples]; Buffer.BlockCopy(bytes, 0, result, 0, bytes.Length);
            return result;
        }
    }
}
