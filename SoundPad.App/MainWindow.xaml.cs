using SoundPad.App.Audio;
using System.IO;
using System.Windows;

namespace SoundPad.App;

public partial class MainWindow : Window
{
    private AudioPlaybackEngine?              _engine;
    private readonly Dictionary<string, CachedSound> _sounds = [];

    public MainWindow()
    {
        InitializeComponent();
        LoadSounds();
    }

    // Decodes every sound file once at startup and stores the result in _sounds.
    // After this method returns, the .mp3 files are never opened again.
    private void LoadSounds()
    {
        var fileNames = new[] { "sound1.mp3", "sound2.mp3", "sound3.mp3", "sound4.mp3" };

        try
        {
            _engine = new AudioPlaybackEngine();

            foreach (var name in fileNames)
            {
                var path = Path.Combine(AppContext.BaseDirectory, "Sounds", name);

                if (!File.Exists(path))
                {
                    // Show the full absolute path so the user knows exactly what is missing.
                    StatusText.Text = $"Missing file: {path}";
                    return;
                }

                _sounds[name] = new CachedSound(path);
            }

            StatusText.Text = $"Sounds loaded ({_sounds.Count})";
        }
        catch (Exception ex)
        {
            StatusText.Text = $"Load error: {ex.Message}";
        }
    }

    // --- Button click handlers ---

    private void Sound1_Click(object sender, RoutedEventArgs e) => PlayCachedSound("sound1.mp3");
    private void Sound2_Click(object sender, RoutedEventArgs e) => PlayCachedSound("sound2.mp3");
    private void Sound3_Click(object sender, RoutedEventArgs e) => PlayCachedSound("sound3.mp3");
    private void Sound4_Click(object sender, RoutedEventArgs e) => PlayCachedSound("sound4.mp3");

    private void StopButton_Click(object sender, RoutedEventArgs e)
    {
        _engine?.StopAll();
        StatusText.Text = "Stopped";
    }

    private void Window_Closing(object sender, System.ComponentModel.CancelEventArgs e)
    {
        _engine?.Dispose();
    }

    // --- Core playback ---

    private void PlayCachedSound(string fileName)
    {
        if (_engine is null || !_sounds.TryGetValue(fileName, out var sound))
        {
            StatusText.Text = "Sound not loaded.";
            return;
        }

        _engine.Play(sound);
        StatusText.Text = $"Playing: {fileName}";
    }
}
