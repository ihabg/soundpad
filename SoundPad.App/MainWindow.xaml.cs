using NAudio.Wave;
using System.IO;
using System.Windows;

namespace SoundPad.App;

public partial class MainWindow : Window
{
    private WaveOutEvent? _waveOut;
    private AudioFileReader? _audioFile;

    public MainWindow()
    {
        InitializeComponent();
    }

    // --- Button click handlers ---
    // Each one just calls the shared PlaySound method with its file name.

    private void Sound1_Click(object sender, RoutedEventArgs e) => PlaySound("sound1.mp3");
    private void Sound2_Click(object sender, RoutedEventArgs e) => PlaySound("sound2.mp3");
    private void Sound3_Click(object sender, RoutedEventArgs e) => PlaySound("sound3.mp3");
    private void Sound4_Click(object sender, RoutedEventArgs e) => PlaySound("sound4.mp3");

    private void StopButton_Click(object sender, RoutedEventArgs e)
    {
        StopAudio();
        StatusText.Text = "Stopped";
    }

    private void Window_Closing(object sender, System.ComponentModel.CancelEventArgs e)
    {
        StopAudio();
    }

    // --- Core audio logic ---

    private void PlaySound(string fileName)
    {
        StopAudio();

        // Build an absolute path so the app works no matter where it is launched from.
        // AppContext.BaseDirectory is always the folder that contains the .exe.
        var path = Path.Combine(AppContext.BaseDirectory, "Sounds", fileName);

        try
        {
            _audioFile = new AudioFileReader(path);
            _waveOut = new WaveOutEvent();
            _waveOut.Init(_audioFile);

            // NAudio fires PlaybackStopped on a background thread, so we must
            // use Dispatcher.Invoke to touch the UI label from there safely.
            _waveOut.PlaybackStopped += (s, args) =>
                Dispatcher.Invoke(() => StatusText.Text = "Stopped");

            _waveOut.Play();
            StatusText.Text = $"Playing: {fileName}";
        }
        catch (Exception ex)
        {
            StatusText.Text = $"Error: {ex.Message}";
        }
    }

    private void StopAudio()
    {
        _waveOut?.Stop();
        _waveOut?.Dispose();
        _waveOut = null;

        _audioFile?.Dispose();
        _audioFile = null;
    }
}