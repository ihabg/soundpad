using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;

namespace SoundPad.App.Controls;

public partial class RecordingTimeline : UserControl
{
    private float[] _peaks = [];
    private bool _updating;
    private bool _scrubbing;
    public event Action? ScrubStarted;
    public event Action<double>? SeekChanged;
    public event Action<double>? ScrubCompleted;

    public RecordingTimeline() => InitializeComponent();

    public void SetWaveform(float[] peaks)
    {
        _peaks = peaks;
        DrawWaveform();
    }

    public void SetProgress(double seconds, double duration)
    {
        _updating = true;
        try
        {
            SeekSlider.Maximum = Math.Max(.001, duration);
            SeekSlider.Value = Math.Clamp(seconds, 0, SeekSlider.Maximum);
            UpdateProgress();
        }
        finally { _updating = false; }
    }

    private void Waveform_SizeChanged(object sender, SizeChangedEventArgs e) => DrawWaveform();

    private void DrawWaveform()
    {
        if (WaveformArea is null) return;
        double width = WaveformArea.ActualWidth, height = WaveformArea.ActualHeight;
        var geometry = new StreamGeometry();
        using (var context = geometry.Open())
            for (int i = 0; i < _peaks.Length; i++)
            {
                double x = (i + .5) * width / _peaks.Length;
                double amplitude = Math.Max(1, Math.Clamp(_peaks[i], 0, 1) * (height / 2 - 4));
                context.BeginFigure(new Point(x, height / 2 - amplitude), false, false);
                context.LineTo(new Point(x, height / 2 + amplitude), true, false);
            }
        geometry.Freeze();
        UnplayedWaveform.Data = PlayedWaveform.Data = geometry;
        UpdateProgress();
    }

    private void UpdateProgress()
    {
        if (TimeText is null || WaveformArea is null) return;
        double fraction = SeekSlider.Maximum > 0 ? SeekSlider.Value / SeekSlider.Maximum : 0;
        PlayedWaveform.Clip = new RectangleGeometry(new Rect(0, 0, WaveformArea.ActualWidth * fraction, WaveformArea.ActualHeight));
        TimeText.Text = $"{TimeSpan.FromSeconds(SeekSlider.Value):mm\\:ss} / {TimeSpan.FromSeconds(SeekSlider.Maximum):mm\\:ss}";
    }

    private void Seek_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        UpdateProgress();
        if (!_updating) SeekChanged?.Invoke(e.NewValue);
    }

    private void Seek_MouseDown(object sender, MouseButtonEventArgs e)
    {
        if (_scrubbing) return;
        _scrubbing = true;
        ScrubStarted?.Invoke();
    }

    private void FinishScrubbing()
    {
        if (!_scrubbing) return;
        _scrubbing = false;
        ScrubCompleted?.Invoke(SeekSlider.Value);
    }

    private void Seek_MouseUp(object sender, MouseButtonEventArgs e) => FinishScrubbing();
    private void Seek_LostMouseCapture(object sender, MouseEventArgs e) => FinishScrubbing();
}
