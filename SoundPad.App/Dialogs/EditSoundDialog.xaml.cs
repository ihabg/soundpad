using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Shapes;
using System.Windows.Threading;
using SoundPad.App.Audio;
using SoundPad.App.Models;
using UiTextBox = Wpf.Ui.Controls.TextBox;

namespace SoundPad.App.Dialogs;

public partial class EditSoundDialog : Wpf.Ui.Controls.FluentWindow
{
    // ── Audio references ──────────────────────────────────────────────────────
    private readonly CachedSound          _sound;
    private readonly HashSet<(uint, uint)> _takenHotkeys;
    private readonly AudioPlaybackEngine? _previewEngine;
    private HotkeyBinding?               _pendingHotkey;
    private PlaybackHandle?              _previewHandle;

    // ── Results ───────────────────────────────────────────────────────────────
    public string         ResultName        { get; private set; } = "";
    public string         ResultCategory    { get; private set; } = "General";
    public float          ResultVolume      { get; private set; } = 1f;
    public double?        ResultTrimStart   { get; private set; }
    public double?        ResultTrimEnd     { get; private set; }
    public double?        ResultFadeIn      { get; private set; }
    public double?        ResultFadeOut     { get; private set; }
    public HotkeyBinding? ResultHotkey     { get; private set; }
    public bool           WasHotkeyChanged  { get; private set; }

    // ── Waveform data ─────────────────────────────────────────────────────────
    private const int WaveformBuckets = 360;
    private float[]  _peaks    = Array.Empty<float>();
    private double   _duration; // total seconds

    // ── Visual / canonical trim-fade state ───────────────────────────────────
    // These are the authoritative values for the timeline.
    // Text boxes are kept in sync bidirectionally.
    private double _vTrimStart;  // seconds
    private double _vTrimEnd;    // seconds
    private double _vFadeIn;     // seconds
    private double _vFadeOut;    // seconds
    private double _playheadSec; // current playhead position
    private bool   _syncingFields; // prevents TextChanged → RedrawCanvas re-entrancy

    // ── Drag state ────────────────────────────────────────────────────────────
    private enum DragTarget { None, TrimStart, TrimEnd }
    private DragTarget _drag = DragTarget.None;

    // ── Playback animation ────────────────────────────────────────────────────
    private DispatcherTimer? _playTimer;
    private DateTime         _playStartTime;
    private double           _playStartSec;

    // ── Constructor ───────────────────────────────────────────────────────────

    public EditSoundDialog(Window owner,
        SoundItem              item,
        CachedSound            sound,
        IEnumerable<string>    availableCategories,
        HashSet<(uint, uint)>  takenHotkeys,
        AudioPlaybackEngine?   previewEngine)
    {
        InitializeComponent();
        Owner          = owner;
        _sound         = sound;
        _takenHotkeys  = takenHotkeys;
        _previewEngine = previewEngine;
        _pendingHotkey = item.Hotkey;
        _duration      = sound.Duration.TotalSeconds;

        // Initialise canonical state from saved item values.
        _vTrimStart  = item.TrimStartSeconds ?? 0;
        _vTrimEnd    = item.TrimEndSeconds   ?? _duration;
        _vFadeIn     = item.FadeInSeconds    ?? 0;
        _vFadeOut    = item.FadeOutSeconds   ?? 0;
        _playheadSec = _vTrimStart;

        ComputePeaks();

        // ── Name / Category
        NameBox.Text = item.DisplayName;
        CategoryBox.Items.Add("General");
        foreach (var cat in availableCategories
            .Where(c => !string.Equals(c, "General", StringComparison.OrdinalIgnoreCase))
            .OrderBy(c => c, StringComparer.OrdinalIgnoreCase))
        {
            CategoryBox.Items.Add(cat);
        }
        CategoryBox.Text = string.IsNullOrWhiteSpace(item.Category) ? "General" : item.Category;

        // ── Volume
        int pct = (int)Math.Round(item.Volume * 100);
        VolumeSlider.Value = pct;
        VolumePctText.Text = $"{pct}%";
        VolumeSlider.ValueChanged += (_, e) => VolumePctText.Text = $"{(int)e.NewValue}%";

        // ── Pre-fill trim/fade text boxes (empty = null = default)
        _syncingFields = true;
        if (item.TrimStartSeconds.HasValue)
            TrimStartBox.Text = item.TrimStartSeconds.Value.ToString("G", CultureInfo.InvariantCulture);
        if (item.TrimEndSeconds.HasValue)
            TrimEndBox.Text   = item.TrimEndSeconds.Value.ToString("G", CultureInfo.InvariantCulture);
        if (item.FadeInSeconds.HasValue)
            FadeInBox.Text    = item.FadeInSeconds.Value.ToString("G", CultureInfo.InvariantCulture);
        if (item.FadeOutSeconds.HasValue)
            FadeOutBox.Text   = item.FadeOutSeconds.Value.ToString("G", CultureInfo.InvariantCulture);
        _syncingFields = false;

        // Subscribe to text changes so typing updates the canvas.
        TrimStartBox.TextChanged += TrimField_TextChanged;
        TrimEndBox.TextChanged   += TrimField_TextChanged;
        FadeInBox.TextChanged    += TrimField_TextChanged;
        FadeOutBox.TextChanged   += TrimField_TextChanged;

        // ── Hotkey
        RefreshHotkeyDisplay();

        // ── Duration / preview
        DurationText.Text = $"Duration: {_duration:F2}s";
        PreviewButton.IsEnabled = previewEngine is not null;

        Closing += (_, _) => StopPreview();
        Loaded  += (_, _) => { NameBox.SelectAll(); NameBox.Focus(); };
        NameBox.KeyDown += (_, e) => { if (e.Key == Key.Enter) Save_Click(this, e); };
    }

    // ── Waveform peaks ────────────────────────────────────────────────────────

    private void ComputePeaks()
    {
        int total = _sound.TotalSamples;
        if (total == 0) { _peaks = Array.Empty<float>(); return; }

        int bucketSize = Math.Max(1, total / WaveformBuckets);
        _peaks = new float[WaveformBuckets];
        for (int b = 0; b < WaveformBuckets; b++)
        {
            int start = b * bucketSize;
            int end   = Math.Min(start + bucketSize, total);
            float peak = 0f;
            for (int i = start; i < end; i++)
                peak = Math.Max(peak, Math.Abs(_sound.AudioData[i]));
            _peaks[b] = peak;
        }
    }

    // ── Coordinate helpers ────────────────────────────────────────────────────

    private double SecondsToX(double sec, double w)
    {
        if (_duration <= 0 || w <= 0) return 0;
        return Math.Clamp(sec / _duration * w, 0, w);
    }

    private double XToSeconds(double x, double w)
    {
        if (w <= 0 || _duration <= 0) return 0;
        return Math.Clamp(x / w * _duration, 0, _duration);
    }

    // ── Canvas events ─────────────────────────────────────────────────────────

    private void WaveformCanvas_SizeChanged(object sender, SizeChangedEventArgs e)
    {
        if (WaveformCanvas.ActualWidth > 0)
            RedrawCanvas();
    }

    private void Waveform_MouseDown(object sender, MouseButtonEventArgs e)
    {
        double x      = e.GetPosition(WaveformCanvas).X;
        double w      = WaveformCanvas.ActualWidth;
        double startX = SecondsToX(_vTrimStart, w);
        double endX   = SecondsToX(_vTrimEnd,   w);

        // Hit-test handles first (8 px tolerance).
        if (Math.Abs(x - startX) <= 8)
        {
            _drag = DragTarget.TrimStart;
            WaveformCanvas.CaptureMouse();
        }
        else if (Math.Abs(x - endX) <= 8)
        {
            _drag = DragTarget.TrimEnd;
            WaveformCanvas.CaptureMouse();
        }
        else
        {
            // Click in body → move playhead.
            _playheadSec = XToSeconds(x, w);
            RedrawCanvas();
        }

        e.Handled = true;
    }

    private void Waveform_MouseMove(object sender, MouseEventArgs e)
    {
        double x = e.GetPosition(WaveformCanvas).X;
        double w = WaveformCanvas.ActualWidth;

        // Update cursor based on proximity to handles (when not dragging).
        if (_drag == DragTarget.None)
        {
            double startX = SecondsToX(_vTrimStart, w);
            double endX   = SecondsToX(_vTrimEnd,   w);
            bool nearHandle = Math.Abs(x - startX) <= 8 || Math.Abs(x - endX) <= 8;
            WaveformCanvas.Cursor = nearHandle ? Cursors.SizeWE : Cursors.Cross;
            return;
        }

        double sec = XToSeconds(x, w);

        if (_drag == DragTarget.TrimStart)
        {
            _vTrimStart = Math.Clamp(sec, 0, _vTrimEnd - 0.01);
            if (_playheadSec < _vTrimStart) _playheadSec = _vTrimStart;
            WriteTextBox(TrimStartBox, _vTrimStart, isDefault: _vTrimStart <= 0);
        }
        else // TrimEnd
        {
            _vTrimEnd = Math.Clamp(sec, _vTrimStart + 0.01, _duration);
            if (_playheadSec > _vTrimEnd) _playheadSec = _vTrimEnd;
            WriteTextBox(TrimEndBox, _vTrimEnd, isDefault: _vTrimEnd >= _duration);
        }

        RedrawCanvas();
    }

    private void Waveform_MouseUp(object sender, MouseButtonEventArgs e)
    {
        _drag = DragTarget.None;
        WaveformCanvas.ReleaseMouseCapture();
    }

    // ── Text box ↔ visual sync ────────────────────────────────────────────────

    private void TrimField_TextChanged(object sender, TextChangedEventArgs e)
    {
        if (_syncingFields) return;

        _vTrimStart = ParseField(TrimStartBox.Text) ?? 0;
        _vTrimEnd   = ParseField(TrimEndBox.Text)   ?? _duration;
        _vFadeIn    = ParseField(FadeInBox.Text)     ?? 0;
        _vFadeOut   = ParseField(FadeOutBox.Text)    ?? 0;

        if (WaveformCanvas.ActualWidth > 0)
            RedrawCanvas();
    }

    // Writes a value back to a text box while suppressing the TextChanged handler.
    private void WriteTextBox(UiTextBox box, double value, bool isDefault)
    {
        _syncingFields = true;
        box.Text       = isDefault ? "" : value.ToString("G6", CultureInfo.InvariantCulture);
        _syncingFields = false;
    }

    private static double? ParseField(string text)
    {
        text = (text ?? "").Trim();
        if (string.IsNullOrEmpty(text)) return null;
        return double.TryParse(text, NumberStyles.Any, CultureInfo.InvariantCulture, out var d) && d >= 0
            ? d : (double?)null;
    }

    // ── Canvas drawing ────────────────────────────────────────────────────────

    private void RedrawCanvas()
    {
        var canvas = WaveformCanvas;
        double w   = canvas.ActualWidth;
        double h   = canvas.ActualHeight;
        if (w <= 0 || h <= 0) return;

        canvas.Children.Clear();

        double cy     = h / 2.0;
        double maxAmp = cy * 0.88;

        // Clamp visual state so the canvas always looks sane.
        double trimStart = Math.Clamp(_vTrimStart, 0, _duration);
        double trimEnd   = Math.Clamp(_vTrimEnd,   trimStart, _duration);

        double startX = SecondsToX(trimStart, w);
        double endX   = SecondsToX(trimEnd,   w);

        // 1 ── Waveform peaks (behind overlays)
        if (_peaks.Length > 0)
        {
            var wfPen   = new Pen(new SolidColorBrush(Color.FromRgb(100, 149, 237)), 1.0); // cornflower blue
            for (int b = 0; b < _peaks.Length; b++)
            {
                double x   = b / (double)(_peaks.Length - 1) * w;
                double amp = _peaks[b] * maxAmp;
                canvas.Children.Add(new Line
                {
                    X1 = x, Y1 = cy - amp,
                    X2 = x, Y2 = cy + amp,
                    Stroke = wfPen.Brush, StrokeThickness = 1.0
                });
            }
        }

        // 2 ── Dim overlay outside trim region
        if (startX > 0)
        {
            var r = new Rectangle { Width = startX, Height = h,
                Fill = new SolidColorBrush(Color.FromArgb(155, 0, 0, 0)) };
            Canvas.SetLeft(r, 0); Canvas.SetTop(r, 0);
            canvas.Children.Add(r);
        }
        if (endX < w)
        {
            var r = new Rectangle { Width = w - endX, Height = h,
                Fill = new SolidColorBrush(Color.FromArgb(155, 0, 0, 0)) };
            Canvas.SetLeft(r, endX); Canvas.SetTop(r, 0);
            canvas.Children.Add(r);
        }

        double selW = endX - startX;

        // 3 ── Fade-in gradient (green, left of selection)
        if (_vFadeIn > 0 && selW > 0)
        {
            double fiW = Math.Min(SecondsToX(trimStart + _vFadeIn, w) - startX, selW);
            if (fiW > 0)
            {
                var r = new Rectangle { Width = fiW, Height = h,
                    Fill = new LinearGradientBrush(
                        Color.FromArgb(90, 50, 210, 100),
                        Color.FromArgb(0,  50, 210, 100),
                        new Point(0, 0), new Point(1, 0)) };
                Canvas.SetLeft(r, startX); Canvas.SetTop(r, 0);
                canvas.Children.Add(r);
            }
        }

        // 4 ── Fade-out gradient (orange, right of selection)
        if (_vFadeOut > 0 && selW > 0)
        {
            double foX = Math.Max(SecondsToX(trimEnd - _vFadeOut, w), startX);
            double foW = endX - foX;
            if (foW > 0)
            {
                var r = new Rectangle { Width = foW, Height = h,
                    Fill = new LinearGradientBrush(
                        Color.FromArgb(0,  220, 130, 0),
                        Color.FromArgb(90, 220, 130, 0),
                        new Point(0, 0), new Point(1, 0)) };
                Canvas.SetLeft(r, foX); Canvas.SetTop(r, 0);
                canvas.Children.Add(r);
            }
        }

        // 5 ── Trim Start handle (green, thumb at top)
        canvas.Children.Add(VLine(startX, h, Brushes.LimeGreen, 2.0));
        canvas.Children.Add(Thumb(startX, 0, Brushes.LimeGreen, top: true));

        // 6 ── Trim End handle (orange-red, thumb at bottom)
        canvas.Children.Add(VLine(endX, h, Brushes.OrangeRed, 2.0));
        canvas.Children.Add(Thumb(endX, h, Brushes.OrangeRed, top: false));

        // 7 ── Playhead (white dashed, topmost)
        double phX = SecondsToX(Math.Clamp(_playheadSec, 0, _duration), w);
        var ph = new Line
        {
            X1 = phX, Y1 = 0, X2 = phX, Y2 = h,
            Stroke = Brushes.White, StrokeThickness = 1.5,
            StrokeDashArray = new DoubleCollection { 4, 3 }
        };
        canvas.Children.Add(ph);
    }

    private static UIElement VLine(double x, double h, Brush brush, double thickness)
        => new Line { X1 = x, Y1 = 0, X2 = x, Y2 = h, Stroke = brush, StrokeThickness = thickness };

    private static UIElement Thumb(double x, double anchorY, Brush fill, bool top)
    {
        const double tw = 12, th = 14;
        var r = new Rectangle { Width = tw, Height = th, Fill = fill, RadiusX = 2, RadiusY = 2 };
        Canvas.SetLeft(r, x - tw / 2);
        Canvas.SetTop(r,  top ? anchorY : anchorY - th);
        return r;
    }

    // ── Hotkey ────────────────────────────────────────────────────────────────

    private void RefreshHotkeyDisplay()
    {
        if (_pendingHotkey is not null)
        {
            HotkeyDisplayText.Text = _pendingHotkey.DisplayText;
            HotkeyDisplayText.Foreground =
                (Brush)Application.Current.Resources["SystemAccentColorPrimaryBrush"];
            ClearHotkeyButton.IsEnabled = true;
        }
        else
        {
            HotkeyDisplayText.Text = "No hotkey assigned";
            HotkeyDisplayText.Foreground =
                (Brush)Application.Current.Resources["TextFillColorTertiaryBrush"];
            ClearHotkeyButton.IsEnabled = false;
        }
    }

    private void ChangeHotkey_Click(object sender, RoutedEventArgs e)
    {
        var dlg = new HotkeyCaptureDialog(this, NameBox.Text.Trim(), _pendingHotkey);
        if (dlg.ShowDialog() != true) return;

        if (dlg.WasCleared)
        {
            _pendingHotkey   = null;
            WasHotkeyChanged = true;
            RefreshHotkeyDisplay();
            return;
        }

        var newBinding = dlg.ResultBinding;
        if (newBinding is null) return;

        if (_takenHotkeys.Contains((newBinding.Modifiers, newBinding.Key)))
        {
            MessageBox.Show(
                $"\"{newBinding.DisplayText}\" is already assigned to another sound.\n\nChoose a different combination.",
                "Hotkey conflict", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        _pendingHotkey   = newBinding;
        WasHotkeyChanged = true;
        RefreshHotkeyDisplay();
    }

    private void ClearHotkey_Click(object sender, RoutedEventArgs e)
    {
        _pendingHotkey   = null;
        WasHotkeyChanged = true;
        RefreshHotkeyDisplay();
    }

    // ── Preview ───────────────────────────────────────────────────────────────

    private void Preview_Click(object sender, RoutedEventArgs e)
    {
        if (_previewEngine is null) return;

        // Toggle: clicking Stop while playing.
        if (_previewHandle is not null && !_previewHandle.IsFinished)
        {
            StopPreview();
            return;
        }

        StopPreview();

        // Start from playhead if it sits inside the trim range; else start from trim start.
        double startPos = (_playheadSec > _vTrimStart && _playheadSec < _vTrimEnd)
            ? _playheadSec : _vTrimStart;

        float uiVol = (float)(VolumeSlider.Value / 100.0);
        float gain  = uiVol * uiVol; // power-2 curve, same as main playback

        int sr       = _sound.SampleRate * _sound.Channels;
        int startS   = (int)(startPos  * sr);
        int endS     = _vTrimEnd < _duration ? (int)(_vTrimEnd * sr) : -1;
        int fadeInS  = _vFadeIn  > 0         ? (int)(_vFadeIn  * sr) : 0;
        int fadeOutS = _vFadeOut > 0         ? (int)(_vFadeOut * sr) : 0;

        bool useDefault = startS == 0 && endS < 0 && fadeInS == 0 && fadeOutS == 0;
        _previewHandle = useDefault
            ? _previewEngine.Play(_sound, gain)
            : _previewEngine.Play(_sound, gain, startS, endS, fadeInS, fadeOutS);

        _playStartSec  = startPos;
        _playStartTime = DateTime.Now;
        _playheadSec   = startPos;

        _playTimer          = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(40) };
        _playTimer.Tick    += (_, _) => TickPlayhead();
        _playTimer.Start();

        PreviewButton.Content   = "Stop Preview";
        PreviewStatusText.Text  = "Playing…";
    }

    private void TickPlayhead()
    {
        if (_previewHandle is null || _previewHandle.IsFinished)
        {
            StopPreview();
            return;
        }

        _playheadSec = _playStartSec + (DateTime.Now - _playStartTime).TotalSeconds;
        if (_playheadSec >= _vTrimEnd)
        {
            _playheadSec = _vTrimEnd;
            RedrawCanvas();
            StopPreview();
            return;
        }

        if (WaveformCanvas.ActualWidth > 0)
            RedrawCanvas();
    }

    private void StopPreview()
    {
        _playTimer?.Stop();
        _playTimer = null;

        if (_previewHandle is not null)
        {
            _previewEngine?.StopOne(_previewHandle.TopProvider);
            _previewHandle = null;
        }

        if (PreviewButton is not null)     PreviewButton.Content  = "Play Preview";
        if (PreviewStatusText is not null) PreviewStatusText.Text = "";
    }

    // ── Save / Cancel ─────────────────────────────────────────────────────────

    private void Save_Click(object sender, RoutedEventArgs e)
    {
        TrimErrorText.Visibility = Visibility.Collapsed;

        var name = NameBox.Text.Trim();
        if (string.IsNullOrWhiteSpace(name))
        {
            TrimErrorText.Text       = "Name cannot be empty.";
            TrimErrorText.Visibility = Visibility.Visible;
            NameBox.Focus();
            return;
        }

        if (!TryParseFields(out var trimStart, out var trimEnd,
                            out var fadeIn,    out var fadeOut, out var parseError))
        {
            TrimErrorText.Text       = parseError;
            TrimErrorText.Visibility = Visibility.Visible;
            return;
        }

        if (trimStart.HasValue && trimEnd.HasValue && trimEnd.Value <= trimStart.Value)
        {
            TrimErrorText.Text       = "Trim End must be greater than Trim Start.";
            TrimErrorText.Visibility = Visibility.Visible;
            return;
        }

        if (trimEnd.HasValue && trimEnd.Value > _duration)
        {
            TrimErrorText.Text       = $"Trim End cannot exceed the sound duration ({_duration:F2}s).";
            TrimErrorText.Visibility = Visibility.Visible;
            return;
        }

        ResultName      = name;
        ResultCategory  = string.IsNullOrWhiteSpace(CategoryBox.Text)
            ? "General" : CategoryBox.Text.Trim();
        ResultVolume    = (float)(VolumeSlider.Value / 100.0);
        ResultTrimStart = trimStart;
        ResultTrimEnd   = trimEnd;
        ResultFadeIn    = fadeIn;
        ResultFadeOut   = fadeOut;
        ResultHotkey    = _pendingHotkey;
        DialogResult    = true;
    }

    private void Cancel_Click(object sender, RoutedEventArgs e) => DialogResult = false;

    // ── Parsing ───────────────────────────────────────────────────────────────

    private bool TryParseFields(out double? trimStart, out double? trimEnd,
        out double? fadeIn, out double? fadeOut, out string error)
    {
        trimStart = trimEnd = fadeIn = fadeOut = null;
        error = "";
        return TryParseSeconds(TrimStartBox.Text, "Trim Start", out trimStart, out error)
            && TryParseSeconds(TrimEndBox.Text,   "Trim End",   out trimEnd,   out error)
            && TryParseSeconds(FadeInBox.Text,    "Fade In",    out fadeIn,    out error)
            && TryParseSeconds(FadeOutBox.Text,   "Fade Out",   out fadeOut,   out error);
    }

    private static bool TryParseSeconds(string text, string fieldName,
        out double? value, out string error)
    {
        value = null;
        error = "";
        text  = (text ?? "").Trim();
        if (string.IsNullOrEmpty(text)) return true;

        if (!double.TryParse(text, NumberStyles.Any, CultureInfo.InvariantCulture, out var d) || d < 0)
        {
            error = $"{fieldName} must be a positive number in seconds (e.g. 1.5).";
            return false;
        }
        value = d;
        return true;
    }
}
