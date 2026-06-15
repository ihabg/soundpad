using NAudio.Wave;
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

    private void PlayButton_Click(object sender, RoutedEventArgs e)
    {
        StopAudio();

        try
        {
            _audioFile = new AudioFileReader("Sounds/test.mp3");
            _waveOut = new WaveOutEvent();
            _waveOut.Init(_audioFile);

            // When playback finishes naturally, update the status label on the UI thread
            _waveOut.PlaybackStopped += (s, args) =>
                Dispatcher.Invoke(() => StatusText.Text = "Stopped");

            _waveOut.Play();
            StatusText.Text = "Playing...";
        }
        catch (Exception ex)
        {
            StatusText.Text = $"Error: {ex.Message}";
        }
    }

    private void StopButton_Click(object sender, RoutedEventArgs e)
    {
        StopAudio();
        StatusText.Text = "Stopped";
    }

    private void Window_Closing(object sender, System.ComponentModel.CancelEventArgs e)
    {
        // Always clean up audio when the window closes so no orphaned process stays alive
        StopAudio();
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