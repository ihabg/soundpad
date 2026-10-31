using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Shapes;
using System.Windows.Threading;
using NAudio.Wave;
using SoundPad.App.Audio;
using SoundPad.App.Models;
using SoundPad.App.Services;
using SoundPad.App.Dialogs;

namespace SoundPad.App.Controls;

public partial class SoundEditorWorkspace : UserControl, IDisposable
{
    
    public Func<Action<SoundItem, CachedSound>>? CaptureSaveTarget { get; set; }
    public event Action? SaveFinished;
    public bool IsSaving => _saving || _exporting || _importing;

    
    private readonly Dictionary<Guid, CachedSound> _cachedSounds = new();
    private readonly List<SoundItem> _deckSounds = new();
    private readonly Func<AudioPlaybackEngine?> _engineProvider;
    private AudioPlaybackEngine? _previewEngine;

    
    private readonly List<MixTrack> _tracks = new();

    
    private int _selectedTrackIndex = -1;

    
    private enum ComposerTool { Select, Cut, Ripple }
    private ComposerTool _activeTool = ComposerTool.Select;

    
    private Guid _selectedClipId = Guid.Empty;

    
    private enum DragZone { Body, LeftEdge, RightEdge }
    private record ClipHitInfo(int TrackIndex, Guid ClipId, DragZone Zone);

    private enum SnapTarget { None, Playhead, ClipEdge }
    private readonly record struct SnapResult(double Seconds, SnapTarget Target);

    private bool     _isDragging;
    private DragZone _dragZone;
    private int      _dragTrackIndex;
    private int      _dragOriginalTrackIndex;
    private Guid     _dragClipId;
    private double   _dragStartX;
    private double   _dragStartY;
    private double   _dragOriginalOffset;
    private double   _dragOriginalTrimStart;
    private double   _dragOriginalTrimEnd;
    private double   _dragFullDuration;

    
    private List<MixTrack>? _preDragSnapshot;
    private bool            _dragUndoPushed;
    private List<(Guid ClipId, double OrigStart, double OrigEnd)>? _dragTrackSnapshot;
    private int _dragMoverOrigSlotIdx;  

    
    private List<Guid>?               _rippleGroup;
    private Dictionary<Guid, double>? _rippleOrigStarts;

    
    private MixClip? _clipboard;
    private int      _clipboardSourceTrackIndex = -1;

    
    private double          _playheadSeconds;
    private bool            _isDraggingPlayhead;
    private DispatcherTimer? _previewTimer;

    
    private PlaybackHandle?          _previewHandle;
    private CancellationTokenSource? _previewCts;

    
    private readonly Stack<List<MixTrack>> _undoStack = new();
    private readonly Stack<List<MixTrack>> _redoStack = new();

    
    private readonly Dictionary<Guid, Border> _clipBlocks        = new();
    private readonly Dictionary<Guid, double> _clipFullDurations = new();
    private readonly WaveformCache            _waveformCache;
    private Line?      _cutPreviewLine;
    private Line?      _snapGuideLine;
    private TextBlock? _snapGuideLabel;
    private TextBlock? _cutPreviewLabel;

    
    private double _pixelsPerSecond = 100.0;
    private const double MinPixelsPerSecond = 20.0;
    private const double MaxPixelsPerSecond = 400.0;
    private const double TrackHeight        = 72.0;
    private const double TrackGap           = 4.0;
    private const double EdgeHandleWidth    = 8.0;
    private const double MinClipDuration    = 0.05;
    private const double SnapPixelThreshold = 12.0;
    private double       SnapThresholdSeconds => SnapPixelThreshold / _pixelsPerSecond;
    private const int    MaxTracks          = 8;

    
    private static readonly Color[] Palette =
    {
        Color.FromRgb(105, 228, 222),
        Color.FromRgb(255, 212, 90),
        Color.FromRgb(154, 222, 163),
        Color.FromRgb(245, 153, 190),
        Color.FromRgb(185, 162, 245),
        Color.FromRgb(234, 179,   8),
        Color.FromRgb(  6, 182, 212),
        Color.FromRgb(244,  63,  98),
    };

    
    public SoundEditorWorkspace(
        IReadOnlyDictionary<Guid, CachedSound> cachedSounds,
        IReadOnlyList<SoundItem> deckSounds,
        Func<AudioPlaybackEngine?> engineProvider)
    {
        _engineProvider = engineProvider;
        _waveformCache = new WaveformCache(() => { if (!_isDragging && !_disposed) RebuildTimeline(); });
        InitializeComponent();
        UpdateSources(cachedSounds, deckSounds);
        InitializeDraft();
        Loaded += (_, _) => { RebuildTimeline(); SetToolVisual(); Focus(); };
        Unloaded += (_, _) => { StopAll(); FlushDraft(); };
        MixNameBox.TextChanged += (_, _) => ScheduleDraft();
    }

    private void AddTrackWithClip(SoundItem item, double startOffset)
    {
        if (_tracks.Count >= MaxTracks) return;
        var clip  = MakeClip(item, startOffset);
        var track = new MixTrack
        {
            Label = item.DisplayName,
            Clips = { clip }
        };
        _tracks.Add(track);
        _selectedTrackIndex = _tracks.Count - 1;
    }

    private void AddClipToTrack(int trackIndex, SoundItem item, double startOffset)
    {
        if (trackIndex < 0 || trackIndex >= _tracks.Count) return;
        var clip = MakeClip(item, startOffset);
        _tracks[trackIndex].Clips.Add(clip);
    }

    private static MixClip MakeClip(SoundItem item, double startOffset) =>
        new() { SoundItemId = item.Id, StartOffsetSeconds = Math.Max(0, startOffset) };

    private void RemoveClip(int trackIndex, Guid clipId)
    {
        if (trackIndex < 0 || trackIndex >= _tracks.Count) return;
        _tracks[trackIndex].Clips.RemoveAll(c => c.Id == clipId);
        if (_tracks[trackIndex].Clips.Count == 0)
            _tracks.RemoveAt(trackIndex);
        if (_selectedClipId == clipId) _selectedClipId = Guid.Empty;
        if (_selectedTrackIndex >= _tracks.Count)
            _selectedTrackIndex = _tracks.Count - 1;
    }

    
    
    private void CleanEmptyTracks()
    {
        for (int i = _tracks.Count - 1; i >= 0; i--)
        {
            if (_tracks[i].Clips.Count == 0 && _tracks.Count > 1)
            {
                _tracks.RemoveAt(i);
                if (_selectedTrackIndex > i)  _selectedTrackIndex--;
            }
        }
        if (_selectedTrackIndex >= _tracks.Count)
            _selectedTrackIndex = _tracks.Count - 1;
    }

    
    
    

    private void PushUndo()
    {
        _undoStack.Push(DeepCopyTracks());
        _redoStack.Clear();
        UpdateUndoRedoButtons();
    }

    private void Undo()
    {
        if (_undoStack.Count == 0) return;
        _redoStack.Push(DeepCopyTracks());
        RestoreSnapshot(_undoStack.Pop());
        UpdateUndoRedoButtons();
        RebuildTimeline();
    }

    private void Redo()
    {
        if (_redoStack.Count == 0) return;
        _undoStack.Push(DeepCopyTracks());
        RestoreSnapshot(_redoStack.Pop());
        UpdateUndoRedoButtons();
        RebuildTimeline();
    }

    private void UpdateUndoRedoButtons()
    {
        UndoButton.IsEnabled = _undoStack.Count > 0;
        RedoButton.IsEnabled = _redoStack.Count > 0;
    }

    private List<MixTrack> DeepCopyTracks()
    {
        var copy = new List<MixTrack>(_tracks.Count);
        foreach (var t in _tracks)
        {
            var tc = new MixTrack { Id = t.Id, Label = t.Label, Volume = t.Volume };
            foreach (var c in t.Clips)
                tc.Clips.Add(new MixClip
                {
                    Id                    = c.Id,
                    SoundItemId           = c.SoundItemId,
                    StartOffsetSeconds    = c.StartOffsetSeconds,
                    LocalTrimStartSeconds = c.LocalTrimStartSeconds,
                    LocalTrimEndSeconds   = c.LocalTrimEndSeconds,
                    Volume                = c.Volume,
                    FadeInSeconds         = c.FadeInSeconds,
                    FadeOutSeconds        = c.FadeOutSeconds,
                });
            copy.Add(tc);
        }
        return copy;
    }

    private void RestoreSnapshot(List<MixTrack> snapshot)
    {
        _tracks.Clear();
        _tracks.AddRange(snapshot);
        _selectedClipId     = Guid.Empty;
        _selectedTrackIndex = Math.Min(_selectedTrackIndex, _tracks.Count - 1);
    }

    
    
    

    private double ComputeClipFullDuration(MixClip clip)
    {
        if (!_cachedSounds.TryGetValue(clip.SoundItemId, out var sound)) return 1.0;
        var item = FindSoundItem(clip.SoundItemId);
        if (item is null) return 1.0;

        double d;
        if (item.Segments != null && item.Segments.Count > 0)
            d = item.Segments.Sum(s => s.EndSeconds - s.StartSeconds);
        else
        {
            double s = item.TrimStartSeconds ?? 0.0;
            double e = item.TrimEndSeconds   ?? sound.Duration.TotalSeconds;
            d = Math.Max(0.01, e - s);
        }
        return Math.Max(MinClipDuration, d / Math.Max(0.01, item.PlaybackSpeed));
    }

    private double GetClipEffectiveDuration(MixClip clip)
    {
        double full      = GetCachedFullDuration(clip);
        double trimStart = clip.LocalTrimStartSeconds ?? 0.0;
        double trimEnd   = clip.LocalTrimEndSeconds   ?? full;
        return Math.Max(MinClipDuration, trimEnd - trimStart);
    }

    private double GetCachedFullDuration(MixClip clip)
    {
        if (_clipFullDurations.TryGetValue(clip.Id, out double d)) return d;
        d = ComputeClipFullDuration(clip);
        _clipFullDurations[clip.Id] = d;
        return d;
    }

    private double GetTotalDuration()
    {
        double max = 4.0;
        foreach (var track in _tracks)
            foreach (var clip in track.Clips)
            {
                double right = clip.StartOffsetSeconds + GetClipEffectiveDuration(clip);
                if (right > max) max = right;
            }
        return max;
    }

    private SoundItem? FindSoundItem(Guid id) =>
        _deckSounds.FirstOrDefault(s => s.Id == id);

    
    
    

    private void RebuildTimeline()
    {
        _clipBlocks.Clear();
        TrackLabelPanel.Children.Clear();
        TimelineCanvas.Children.Clear();
        _cutPreviewLine  = null;
        _snapGuideLine   = null;
        _snapGuideLabel  = null;
        _cutPreviewLabel = null;
        RulerCanvas.Children.Clear();

        int    n        = _tracks.Count;
        double totalDur = GetTotalDuration();
        double canvasW  = Math.Max(totalDur * _pixelsPerSecond + 300.0, 900.0);
        double canvasH  = n > 0
            ? TrackGap + n * (TrackHeight + TrackGap)
            : TrackHeight + TrackGap * 2;

        TimelineCanvas.Width  = canvasW;
        TimelineCanvas.Height = canvasH;
        RulerCanvas.Width     = canvasW;

        DrawRuler(totalDur, canvasW);

        for (int i = 0; i < n; i++)
        {
            double stripeY = TrackGap + i * (TrackHeight + TrackGap);
            var stripe = new Rectangle
            {
                Width  = canvasW,
                Height = TrackHeight,
                Fill   = i % 2 == 0
                    ? new SolidColorBrush(Color.FromArgb(18, 128, 128, 128))
                    : new SolidColorBrush(Color.FromArgb(8,  128, 128, 128))
            };
            Canvas.SetLeft(stripe, 0);
            Canvas.SetTop(stripe,  stripeY);
            TimelineCanvas.Children.Add(stripe);

            DrawTrackLabel(i);
            foreach (var clip in _tracks[i].Clips)
                DrawClipBlock(i, clip);
        }

        DrawPlayhead();
        RefreshSelectedClipPanel();
        EditorEmptyText.Visibility = HasClips() ? Visibility.Collapsed : Visibility.Visible;
        UpdateTransportTime();
        ScheduleDraft();
    }

    private void DrawRuler(double totalDur, double canvasW)
    {
        
        int step = _pixelsPerSecond >= 100 ? 1
                 : _pixelsPerSecond >= 40  ? 2
                 : _pixelsPerSecond >= 20  ? 5
                 : 10;
        int majorEvery = _pixelsPerSecond >= 100 ? 5 : 3;

        int maxTick = ((int)Math.Ceiling(totalDur / step) + 4) * step;
        for (int t = 0; t <= maxTick; t += step)
        {
            double x = t * _pixelsPerSecond;
            if (x > canvasW) break;
            bool major = t % (step * majorEvery) == 0;
            var tick = new Line
            {
                X1 = x, Y1 = major ? 4 : 14, X2 = x, Y2 = 26,
                Stroke          = new SolidColorBrush(Color.FromArgb(160, 176, 190, 218)),
                StrokeThickness = major ? 1.5 : 0.8
            };
            RulerCanvas.Children.Add(tick);
            if (major)
            {
                var lbl = new TextBlock
                {
                    Text       = FormatTime(t),
                    FontSize   = 10,
                    Foreground = new SolidColorBrush(Color.FromArgb(220, 176, 190, 218))
                };
                Canvas.SetLeft(lbl, x + 2);
                Canvas.SetTop(lbl,  4);
                RulerCanvas.Children.Add(lbl);
            }
        }
    }

    private static string FormatTime(int s) =>
        s < 60 ? $"{s}s" : $"{s / 60}:{s % 60:D2}";

    private void DrawTrackLabel(int i)
    {
        var track = _tracks[i];
        var color = Palette[i % Palette.Length];
        int idx   = i;

        var colorStrip = new Border
        {
            Width      = 4,
            Background = new SolidColorBrush(color)
        };
        Grid.SetColumn(colorStrip, 0);

        var nameText = new TextBlock
        {
            Text              = string.IsNullOrWhiteSpace(track.Label) ? $"Track {i + 1}" : track.Label,
            FontSize          = 12,
            FontWeight        = FontWeights.SemiBold,
            TextTrimming      = TextTrimming.CharacterEllipsis,
            VerticalAlignment = VerticalAlignment.Center
        };
        var infoStack = new StackPanel { Margin = new Thickness(6, 4, 4, 2) };
        infoStack.Children.Add(nameText);
        Grid.SetColumn(infoStack, 1);

        var volSlider = new Slider
        {
            Minimum = 0.0, Maximum = 1.5, Value = track.Volume,
            Width = 64, VerticalAlignment = VerticalAlignment.Center,
            ToolTip = "Track volume"
        };
        volSlider.GotMouseCapture += (_, _) => PushUndo();
        volSlider.ValueChanged += (_, _) =>
        {
            if (idx < _tracks.Count)
                _tracks[idx].Volume = (float)volSlider.Value;
            ScheduleDraft();
        };

        var removeBtn = new Button
        {
            Content = "×", FontSize = 14,
            Padding = new Thickness(5, 0, 5, 2),
            Margin  = new Thickness(2, 0, 4, 0),
            VerticalAlignment = VerticalAlignment.Center,
            Background  = Brushes.Transparent,
            BorderBrush = Brushes.Transparent,
            ToolTip     = "Remove track"
        };
        removeBtn.Click += (_, _) =>
        {
            PushUndo();
            _tracks.RemoveAt(idx);
            if (_selectedTrackIndex >= _tracks.Count)
                _selectedTrackIndex = _tracks.Count - 1;
            _selectedClipId = Guid.Empty;
            RebuildTimeline();
            StatusText.Text = "Track removed.";
        };

        var rightPanel = new StackPanel { Orientation = Orientation.Horizontal };
        rightPanel.Children.Add(volSlider);
        rightPanel.Children.Add(removeBtn);
        Grid.SetColumn(rightPanel, 2);

        var grid = new Grid();
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(4) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        grid.Children.Add(colorStrip);
        grid.Children.Add(infoStack);
        grid.Children.Add(rightPanel);

        bool isSelected = (i == _selectedTrackIndex);
        var row = new Border
        {
            Height          = TrackHeight,
            BorderThickness = new Thickness(0, 0, 0, 1),
            BorderBrush     = new SolidColorBrush(Color.FromArgb(30, 128, 128, 128)),
            Background      = isSelected
                ? new SolidColorBrush(Color.FromArgb(30, color.R, color.G, color.B))
                : Brushes.Transparent,
            Child = grid
        };
        row.MouseLeftButtonDown += (_, e) =>
        {
            _selectedTrackIndex = idx;
            RebuildTimeline();
            e.Handled = true;
        };

        TrackLabelPanel.Children.Add(row);
    }

    private void DrawClipBlock(int trackIndex, MixClip clip)
    {
        var    color  = Palette[trackIndex % Palette.Length];
        double effDur = GetClipEffectiveDuration(clip);
        double blockW = Math.Max(EdgeHandleWidth * 2 + 4, effDur * _pixelsPerSecond);
        double blockX = clip.StartOffsetSeconds * _pixelsPerSecond;
        double blockY = TrackGap + trackIndex * (TrackHeight + TrackGap);

        bool selected = (clip.Id == _selectedClipId);

        bool trimEnabled = TrimHandlesCheckBox.IsChecked == true;
        var leftHandle = new Border
        {
            Width      = EdgeHandleWidth,
            Background = trimEnabled ? new SolidColorBrush(Color.FromArgb(80, 0, 0, 0)) : Brushes.Transparent,
            Cursor     = trimEnabled ? Cursors.SizeWE : Cursors.SizeAll,
            Tag        = new ClipHitInfo(trackIndex, clip.Id, trimEnabled ? DragZone.LeftEdge : DragZone.Body)
        };
        Grid.SetColumn(leftHandle, 0);

        var    item      = FindSoundItem(clip.SoundItemId);
        string clipLabel = item?.DisplayName ?? "?";
        if (Math.Abs(clip.Volume - 1.0f) > 0.02f)
            clipLabel += $"  {clip.Volume * 100:0}%";
        if (clip.FadeInSeconds  > 0) clipLabel += $"  FI {clip.FadeInSeconds:0.##}s";
        if (clip.FadeOutSeconds > 0) clipLabel += $"  FO {clip.FadeOutSeconds:0.##}s";

        var label = new TextBlock
        {
            Text                = clipLabel,
            FontSize            = 11,
            Foreground          = Brushes.Black,
            VerticalAlignment   = VerticalAlignment.Center,
            HorizontalAlignment = HorizontalAlignment.Center,
            TextTrimming        = TextTrimming.CharacterEllipsis,
            Margin              = new Thickness(2, 0, 2, 0),
            IsHitTestVisible    = false
        };
        Grid.SetColumn(label, 1);

        var rightHandle = new Border
        {
            Width      = EdgeHandleWidth,
            Background = trimEnabled ? new SolidColorBrush(Color.FromArgb(80, 0, 0, 0)) : Brushes.Transparent,
            Cursor     = trimEnabled ? Cursors.SizeWE : Cursors.SizeAll,
            Tag        = new ClipHitInfo(trackIndex, clip.Id, trimEnabled ? DragZone.RightEdge : DragZone.Body)
        };
        Grid.SetColumn(rightHandle, 2);

        var inner = new Grid();
        inner.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(EdgeHandleWidth) });
        inner.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        inner.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(EdgeHandleWidth) });
        inner.Children.Add(leftHandle);
        inner.Children.Add(label);
        inner.Children.Add(rightHandle);

        var fillColor = selected
            ? Color.FromArgb(230, (byte)Math.Min(255, color.R + 50), (byte)Math.Min(255, color.G + 50), (byte)Math.Min(255, color.B + 50))
            : Color.FromArgb(200, color.R, color.G, color.B);

        
        
        var blockContent = new Grid();

        
        if (blockW >= 20)
        {
            _cachedSounds.TryGetValue(clip.SoundItemId, out var cachedSoundForWf);
            var peaks = _waveformCache.Request(clip.SoundItemId, cachedSoundForWf);
            if (peaks is not null && peaks.Length > 0)
            {
                var wfPath = BuildWaveformPath(peaks, clip, cachedSoundForWf, blockW);
                if (wfPath is not null)
                    blockContent.Children.Add(wfPath);
            }
        }

        
        if (clip.FadeInSeconds > 0 && blockW >= 16)
        {
            double fadeW = Math.Min(clip.FadeInSeconds * _pixelsPerSecond, blockW / 2.0);
            if (fadeW >= 2)
                blockContent.Children.Add(new Rectangle
                {
                    Width               = fadeW,
                    HorizontalAlignment = HorizontalAlignment.Left,
                    IsHitTestVisible    = false,
                    Fill = new LinearGradientBrush(
                        Color.FromArgb(85, 0, 0, 0), Color.FromArgb(0, 0, 0, 0),
                        new Point(0, 0.5), new Point(1, 0.5))
                });
        }
        if (clip.FadeOutSeconds > 0 && blockW >= 16)
        {
            double fadeW = Math.Min(clip.FadeOutSeconds * _pixelsPerSecond, blockW / 2.0);
            if (fadeW >= 2)
                blockContent.Children.Add(new Rectangle
                {
                    Width               = fadeW,
                    HorizontalAlignment = HorizontalAlignment.Right,
                    IsHitTestVisible    = false,
                    Fill = new LinearGradientBrush(
                        Color.FromArgb(0, 0, 0, 0), Color.FromArgb(85, 0, 0, 0),
                        new Point(0, 0.5), new Point(1, 0.5))
                });
        }
        blockContent.Children.Add(inner);

        var block = new Border
        {
            Width        = blockW,
            Height       = TrackHeight,
            CornerRadius = new CornerRadius(4),
            Background   = new SolidColorBrush(fillColor),
            BorderBrush = (Brush)FindResource("ComicInkBrush"),
            BorderThickness = new Thickness(2),
            ClipToBounds = true,
            Cursor       = _activeTool == ComposerTool.Cut ? Cursors.Cross : Cursors.SizeAll,
            Tag          = new ClipHitInfo(trackIndex, clip.Id, DragZone.Body),
            Child        = blockContent
        };

        if (selected)
        {
            block.BorderThickness = new Thickness(2);
            block.BorderBrush     = (Brush)FindResource("ComicYellowBrush");
        }

        Canvas.SetLeft(block, blockX);
        Canvas.SetTop(block,  blockY);
        TimelineCanvas.Children.Add(block);
        _clipBlocks[clip.Id] = block;
    }

    
    
    private System.Windows.Shapes.Path? BuildWaveformPath(float[] peaks, MixClip clip, CachedSound? cachedSound, double blockW)
    {
        if (cachedSound is null || cachedSound.TotalFrames == 0 || cachedSound.SampleRate == 0) return null;

        double contentW = blockW - 2 * EdgeHandleWidth;
        if (contentW < 4) return null;

        
        double rawDur = cachedSound.TotalFrames / (double)cachedSound.SampleRate;
        if (rawDur <= 0) return null;

        double trimStart = Math.Clamp(clip.LocalTrimStartSeconds ?? 0.0,      0, rawDur);
        double trimEnd   = Math.Clamp(clip.LocalTrimEndSeconds   ?? rawDur, trimStart, rawDur);

        int iPeakStart = Math.Clamp((int)(trimStart / rawDur * peaks.Length), 0, peaks.Length);
        int iPeakEnd   = Math.Clamp((int)(trimEnd   / rawDur * peaks.Length), iPeakStart, peaks.Length);
        int visiblePeaks = iPeakEnd - iPeakStart;
        if (visiblePeaks <= 0) return null;

        
        int    numBars  = Math.Max(1, Math.Min((int)(contentW / 2.0), visiblePeaks));
        double barStep  = contentW / numBars;
        double peakStep = (double)visiblePeaks / numBars;
        double centerY  = TrackHeight / 2.0;
        double maxAmp   = TrackHeight * 0.40;   

        var geo = new StreamGeometry();
        using (var ctx = geo.Open())
        {
            for (int b = 0; b < numBars; b++)
            {
                int pStart = iPeakStart + (int)(b       * peakStep);
                int pEnd   = iPeakStart + (int)((b + 1) * peakStep);
                if (pEnd > peaks.Length) pEnd = peaks.Length;
                if (pStart >= pEnd) pEnd = pStart + 1;

                float maxP = 0f;
                for (int p = pStart; p < pEnd && p < peaks.Length; p++)
                    maxP = Math.Max(maxP, peaks[p]);

                double x   = EdgeHandleWidth + (b + 0.5) * barStep;
                double amp = Math.Max(0.5, maxP * maxAmp);   

                ctx.BeginFigure(new Point(x, centerY - amp), false, false);
                ctx.LineTo(new Point(x, centerY + amp), true, false);
            }
        }
        geo.Freeze();

        return new System.Windows.Shapes.Path
        {
            Data             = geo,
            Stroke           = new SolidColorBrush(Color.FromArgb(145, 9, 13, 24)),
            StrokeThickness  = 1.0,
            IsHitTestVisible = false
        };
    }

    private void DrawPlayhead()
    {
        var old = TimelineCanvas.Children.OfType<UIElement>()
                      .Where(e => (e as FrameworkElement)?.Tag is "playhead")
                      .ToList();
        foreach (var e in old) TimelineCanvas.Children.Remove(e);

        var oldR = RulerCanvas.Children.OfType<UIElement>()
                       .Where(e => (e as FrameworkElement)?.Tag is "playhead")
                       .ToList();
        foreach (var e in oldR) RulerCanvas.Children.Remove(e);

        double x = _playheadSeconds * _pixelsPerSecond;

        var tl = new Line
        {
            X1 = x, Y1 = 0, X2 = x, Y2 = TimelineCanvas.Height,
            Stroke           = new SolidColorBrush(Color.FromRgb(255, 212, 90)),
            StrokeThickness  = 1.5,
            Tag              = "playhead",
            IsHitTestVisible = false
        };
        TimelineCanvas.Children.Add(tl);
        Canvas.SetZIndex(tl, 999);

        var rl = new Line
        {
            X1 = x, Y1 = 0, X2 = x, Y2 = 26,
            Stroke           = new SolidColorBrush(Color.FromRgb(255, 212, 90)),
            StrokeThickness  = 1.5,
            Tag              = "playhead",
            IsHitTestVisible = false
        };
        RulerCanvas.Children.Add(rl);
        Canvas.SetZIndex(rl, 999);

        var tri = new Polygon
        {
            Points = new PointCollection { new(x - 5, 0), new(x + 5, 0), new(x, 10) },
            Fill   = new SolidColorBrush(Color.FromRgb(255, 212, 90)),
            Tag    = "playhead",
            IsHitTestVisible = false
        };
        RulerCanvas.Children.Add(tri);
        Canvas.SetZIndex(tri, 999);
    }

    private void MovePlayheadVisual()
    {
        UpdateTransportTime();
        if (_previewTimer is null) ScheduleDraft();
        double x = _playheadSeconds * _pixelsPerSecond;

        foreach (var l in TimelineCanvas.Children.OfType<Line>().Where(l => l.Tag is "playhead"))
        {
            l.X1 = x; l.X2 = x; l.Y2 = TimelineCanvas.Height;
        }
        foreach (var l in RulerCanvas.Children.OfType<Line>().Where(l => l.Tag is "playhead"))
        {
            l.X1 = x; l.X2 = x;
        }
        foreach (var p in RulerCanvas.Children.OfType<Polygon>().Where(p => p.Tag is "playhead"))
        {
            p.Points = new PointCollection { new(x - 5, 0), new(x + 5, 0), new(x, 10) };
        }
    }

    private void UpdateCanvasWidth()
    {
        double w = Math.Max(GetTotalDuration() * _pixelsPerSecond + 300.0, 900.0);
        if (Math.Abs(TimelineCanvas.Width - w) > 1.0)
        {
            TimelineCanvas.Width = w;
            RulerCanvas.Width    = w;
        }
    }

    private void RefreshClipBlock(int trackIndex, MixClip clip)
    {
        if (!_clipBlocks.TryGetValue(clip.Id, out var block)) return;
        double effDur = GetClipEffectiveDuration(clip);
        double w      = Math.Max(EdgeHandleWidth * 2 + 4, effDur * _pixelsPerSecond);
        Canvas.SetLeft(block, clip.StartOffsetSeconds * _pixelsPerSecond);
        block.Width = w;
    }

    
    
    

    private static ClipHitInfo? FindClipHit(DependencyObject? obj)
    {
        while (obj != null)
        {
            if (obj is FrameworkElement { Tag: ClipHitInfo info }) return info;
            obj = VisualTreeHelper.GetParent(obj);
        }
        return null;
    }

    private (int TrackIdx, MixClip? Clip) HitTestClipAtPoint(double x, double y)
    {
        for (int ti = 0; ti < _tracks.Count; ti++)
        {
            double rowTop    = TrackGap + ti * (TrackHeight + TrackGap);
            double rowBottom = rowTop + TrackHeight;
            if (y < rowTop || y > rowBottom) continue;

            foreach (var clip in _tracks[ti].Clips)
            {
                double left  = clip.StartOffsetSeconds * _pixelsPerSecond;
                double right = left + GetClipEffectiveDuration(clip) * _pixelsPerSecond;
                if (x >= left && x <= right)
                    return (ti, clip);
            }
        }
        return (-1, null);
    }

    
    
    

    private SnapResult SnapPositionEx(double rawSeconds, Guid excludeClipId = default)
    {
        if (SnapCheckBox.IsChecked != true) return new(rawSeconds, SnapTarget.None);

        if (Math.Abs(rawSeconds - _playheadSeconds) <= SnapThresholdSeconds)
            return new(_playheadSeconds, SnapTarget.Playhead);

        foreach (var track in _tracks)
            foreach (var clip in track.Clips)
            {
                if (clip.Id == excludeClipId) continue;
                double clipStart = clip.StartOffsetSeconds;
                double clipEnd   = clipStart + GetClipEffectiveDuration(clip);
                if (Math.Abs(rawSeconds - clipStart) <= SnapThresholdSeconds) return new(clipStart, SnapTarget.ClipEdge);
                if (Math.Abs(rawSeconds - clipEnd)   <= SnapThresholdSeconds) return new(clipEnd,   SnapTarget.ClipEdge);
            }

        return new(rawSeconds, SnapTarget.None);
    }

    private double SnapPosition(double rawSeconds, Guid excludeClipId = default)
        => SnapPositionEx(rawSeconds, excludeClipId).Seconds;

    
    
    

    private void Ruler_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        double x = e.GetPosition(RulerCanvas).X;
        _playheadSeconds    = Math.Max(0, x / _pixelsPerSecond);
        _isDraggingPlayhead = true;
        RulerCanvas.CaptureMouse();
        MovePlayheadVisual();
        e.Handled = true;
    }

    private void Ruler_MouseMove(object sender, MouseEventArgs e)
    {
        if (!_isDraggingPlayhead) return;
        double x = e.GetPosition(RulerCanvas).X;
        _playheadSeconds = Math.Max(0, x / _pixelsPerSecond);
        MovePlayheadVisual();
    }

    private void Ruler_MouseLeftButtonUp(object sender, MouseButtonEventArgs e)
    {
        _isDraggingPlayhead = false;
        RulerCanvas.ReleaseMouseCapture();
    }

    
    
    

    private void Timeline_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        var pos = e.GetPosition(TimelineCanvas);

        if (_activeTool == ComposerTool.Cut)
        {
            HandleCutClick(pos);
            return;
        }

        var hit = FindClipHit(e.OriginalSource as DependencyObject);
        if (hit is null)
        {
            _selectedClipId = Guid.Empty;
            RebuildTimeline();
            return;
        }

        if (_selectedClipId != hit.ClipId)
        {
            _selectedClipId = hit.ClipId;
            RebuildTimeline();
        }

        var clip = FindClip(hit.TrackIndex, hit.ClipId);
        if (clip is null) return;

        double full = GetCachedFullDuration(clip);
        _isDragging             = true;
        _dragUndoPushed         = false;
        _dragZone               = hit.Zone;
        _dragTrackIndex         = hit.TrackIndex;
        _dragOriginalTrackIndex = hit.TrackIndex;
        _dragClipId             = hit.ClipId;
        _dragStartX             = pos.X;
        _dragStartY             = pos.Y;
        _dragOriginalOffset        = clip.StartOffsetSeconds;
        _dragTrackSnapshot         = BuildTrackSnapshot(clip.Id, hit.TrackIndex);
        _dragMoverOrigSlotIdx      = _dragTrackSnapshot.Count(s => s.OrigStart < clip.StartOffsetSeconds);
        _dragOriginalTrimStart     = clip.LocalTrimStartSeconds ?? 0.0;
        _dragOriginalTrimEnd    = clip.LocalTrimEndSeconds   ?? full;
        _dragFullDuration       = full;

        
        _preDragSnapshot = DeepCopyTracks();

        
        _rippleGroup      = null;
        _rippleOrigStarts = null;
        if (_activeTool == ComposerTool.Ripple && hit.Zone == DragZone.Body)
        {
            double moverOrigStart = clip.StartOffsetSeconds;
            _rippleGroup = _tracks[hit.TrackIndex].Clips
                .Where(c => c.StartOffsetSeconds >= moverOrigStart - 0.0001)
                .Select(c => c.Id)
                .ToList();
            _rippleOrigStarts = _tracks[hit.TrackIndex].Clips
                .Where(c => _rippleGroup.Contains(c.Id))
                .ToDictionary(c => c.Id, c => c.StartOffsetSeconds);
        }

        TimelineCanvas.CaptureMouse();
        e.Handled = true;
    }

    private void Timeline_MouseMove(object sender, MouseEventArgs e)
    {
        var pos = e.GetPosition(TimelineCanvas);

        if (!_isDragging)
        {
            if (_activeTool == ComposerTool.Cut) UpdateCutPreviewLine(pos);
            return;
        }

        double deltaX = (pos.X - _dragStartX) / _pixelsPerSecond;
        var clip = FindClip(_dragTrackIndex, _dragClipId);
        if (clip is null) { _isDragging = false; return; }

        
        if (!_dragUndoPushed && (Math.Abs(pos.X - _dragStartX) > 2.0 || Math.Abs(pos.Y - _dragStartY) > 2.0))
        {
            if (_preDragSnapshot != null)
            {
                _undoStack.Push(_preDragSnapshot);
                _preDragSnapshot = null;
                _redoStack.Clear();
                UpdateUndoRedoButtons();
            }
            _dragUndoPushed = true;
        }

        switch (_dragZone)
        {
            case DragZone.Body:
            {
                if (_activeTool == ComposerTool.Ripple && _rippleGroup != null && _rippleOrigStarts != null)
                {
                    
                    
                    double totalDeltaX = (pos.X - _dragStartX) / _pixelsPerSecond;
                    var    track       = _tracks[_dragTrackIndex];

                    
                    double leftBound = 0.0;
                    foreach (var rc in track.Clips)
                    {
                        if (_rippleGroup.Contains(rc.Id)) continue;
                        double rcEnd = rc.StartOffsetSeconds + GetClipEffectiveDuration(rc);
                        if (rcEnd > leftBound) leftBound = rcEnd;
                    }

                    double minGroupOrigStart = _rippleOrigStarts.Values.Min();
                    double clampedDelta      = Math.Max(totalDeltaX, leftBound - minGroupOrigStart);

                    foreach (var rc in track.Clips)
                    {
                        if (!_rippleGroup.Contains(rc.Id)) continue;
                        if (!_rippleOrigStarts.TryGetValue(rc.Id, out double origStart)) continue;
                        rc.StartOffsetSeconds = Math.Max(0.0, origStart + clampedDelta);
                    }

                    RebuildTimeline();
                    UpdateCanvasWidth();
                }
                else
                {
                    
                    double rawOffset = _dragOriginalOffset + deltaX;
                    var    snapBody  = SnapPositionEx(rawOffset, _dragClipId);
                    double proposed  = Math.Max(0.0, snapBody.Seconds);
                    if (snapBody.Target != SnapTarget.None) ShowSnapGuide(snapBody); else HideSnapGuide();

                    
                    int targetTrack = Math.Clamp(
                        (int)(pos.Y / (TrackHeight + TrackGap)),
                        0, _tracks.Count - 1);

                    if (targetTrack != _dragTrackIndex)
                    {
                        _tracks[_dragTrackIndex].Clips.Remove(clip);
                        _tracks[targetTrack].Clips.Add(clip);
                        _dragTrackIndex      = targetTrack;
                        _selectedTrackIndex  = targetTrack;
                        _dragTrackSnapshot     = BuildTrackSnapshot(_dragClipId, targetTrack);
                        _dragOriginalOffset    = proposed;
                        _dragStartX            = pos.X;
                        _dragMoverOrigSlotIdx  = _dragTrackSnapshot.Count(s => s.OrigStart < proposed);
                    }

                    clip.StartOffsetSeconds = ResolveFreeInsertStartTime(clip, _dragTrackIndex, proposed);

                    RebuildTimeline();
                    UpdateCanvasWidth();
                }
                break;
            }

            case DragZone.LeftEdge:
            {
                double rawStart     = _dragOriginalOffset + deltaX;
                var    snapLeft     = SnapPositionEx(rawStart, _dragClipId);
                double snappedStart = Math.Max(0.0, snapLeft.Seconds);
                if (snapLeft.Target != SnapTarget.None) ShowSnapGuide(snapLeft); else HideSnapGuide();
                double actualDelta  = snappedStart - _dragOriginalOffset;
                double newTrimStart = Math.Clamp(
                    _dragOriginalTrimStart + actualDelta,
                    0.0,
                    _dragOriginalTrimEnd - MinClipDuration);
                double clampedDelta = newTrimStart - _dragOriginalTrimStart;
                clip.StartOffsetSeconds    = Math.Max(0.0, _dragOriginalOffset + clampedDelta);
                clip.LocalTrimStartSeconds = newTrimStart > 0.001 ? newTrimStart : (double?)null;
                RefreshClipBlock(_dragTrackIndex, clip);
                RefreshSelectedClipPanel();
                UpdateCanvasWidth();
                break;
            }

            case DragZone.RightEdge:
            {
                
                double rawRightEdge    = _dragOriginalOffset + (_dragOriginalTrimEnd + deltaX - _dragOriginalTrimStart);
                var    snapRight        = SnapPositionEx(rawRightEdge, _dragClipId);
                double snappedRightEdge = snapRight.Seconds;
                if (snapRight.Target != SnapTarget.None) ShowSnapGuide(snapRight); else HideSnapGuide();
                double snappedTrimEnd   = snappedRightEdge - _dragOriginalOffset + _dragOriginalTrimStart;
                double newTrimEnd = Math.Clamp(
                    snappedTrimEnd,
                    _dragOriginalTrimStart + MinClipDuration,
                    _dragFullDuration);
                clip.LocalTrimEndSeconds = newTrimEnd < _dragFullDuration - 0.001 ? newTrimEnd : (double?)null;
                RefreshClipBlock(_dragTrackIndex, clip);
                RefreshSelectedClipPanel();
                UpdateCanvasWidth();
                break;
            }
        }
    }

    private void Timeline_MouseLeftButtonUp(object sender, MouseButtonEventArgs e)
    {
        if (!_isDragging) return;
        _isDragging        = false;
        _preDragSnapshot   = null;
        _dragTrackSnapshot = null;
        _rippleGroup       = null;
        _rippleOrigStarts  = null;
        TimelineCanvas.ReleaseMouseCapture();
        CleanEmptyTracks();
        RebuildTimeline();
    }

    private void Timeline_MouseLeave(object sender, MouseEventArgs e)
    {
        if (_isDragging && !TimelineCanvas.IsMouseCaptured)
            _isDragging = false;
        if (_cutPreviewLine  != null) _cutPreviewLine.Visibility  = Visibility.Collapsed;
        if (_cutPreviewLabel != null) _cutPreviewLabel.Visibility = Visibility.Collapsed;
        HideSnapGuide();
    }

    private void Timeline_MouseRightButtonUp(object sender, MouseButtonEventArgs e)
    {
        var hit = FindClipHit(e.OriginalSource as DependencyObject);
        if (hit is null) return;

        var clip = FindClip(hit.TrackIndex, hit.ClipId);
        if (clip is null) return;

        _selectedClipId     = hit.ClipId;
        _selectedTrackIndex = hit.TrackIndex;
        RebuildTimeline();

        var menu = BuildClipContextMenu(hit.TrackIndex, clip);
        menu.PlacementTarget = TimelineCanvas;
        menu.IsOpen          = true;
        e.Handled            = true;
    }

    
    
    

    private void HandleCutClick(Point pos)
    {
        
        
        var (trackIdx, clip) = HitTestClipAtPoint(pos.X, pos.Y);
        if (clip is null) return;

        
        double playheadX = _playheadSeconds * _pixelsPerSecond;
        double clickTime = (SnapCheckBox.IsChecked == true && Math.Abs(pos.X - playheadX) <= SnapPixelThreshold)
            ? _playheadSeconds
            : SnapPosition(pos.X / _pixelsPerSecond, clip.Id);

        double trimStart = clip.LocalTrimStartSeconds ?? 0.0;
        double full      = GetCachedFullDuration(clip);
        double trimEnd   = clip.LocalTrimEndSeconds   ?? full;
        double cutInClip = (clickTime - clip.StartOffsetSeconds) + trimStart;

        if (cutInClip < trimStart + MinClipDuration || cutInClip > trimEnd - MinClipDuration)
        {
            StatusText.Text = "Cut is too close to clip edge.";
            return;
        }

        PushUndo();

        var clipA = new MixClip
        {
            SoundItemId           = clip.SoundItemId,
            StartOffsetSeconds    = clip.StartOffsetSeconds,
            LocalTrimStartSeconds = clip.LocalTrimStartSeconds,
            LocalTrimEndSeconds   = cutInClip,
            Volume                = clip.Volume,
            FadeInSeconds         = clip.FadeInSeconds,
            FadeOutSeconds        = 0,                      
        };
        var clipB = new MixClip
        {
            SoundItemId           = clip.SoundItemId,
            StartOffsetSeconds    = clickTime,
            LocalTrimStartSeconds = cutInClip,
            LocalTrimEndSeconds   = clip.LocalTrimEndSeconds,
            Volume                = clip.Volume,
            FadeInSeconds         = 0,                      
            FadeOutSeconds        = clip.FadeOutSeconds,
        };

        var clips = _tracks[trackIdx].Clips;
        int idx   = clips.IndexOf(clip);
        clips.RemoveAt(idx);
        clips.Insert(idx,     clipA);
        clips.Insert(idx + 1, clipB);

        _selectedClipId = clipA.Id;
        RebuildTimeline();
        StatusText.Text = "Cut applied.";
    }

    private void UpdateCutPreviewLine(Point pos)
    {
        var (_, clip) = HitTestClipAtPoint(pos.X, pos.Y);
        if (clip is null)
        {
            if (_cutPreviewLine  != null) _cutPreviewLine.Visibility  = Visibility.Collapsed;
            if (_cutPreviewLabel != null) _cutPreviewLabel.Visibility = Visibility.Collapsed;
            return;
        }

        
        double playheadX = _playheadSeconds * _pixelsPerSecond;
        SnapResult cutSnap = (SnapCheckBox.IsChecked == true && Math.Abs(pos.X - playheadX) <= SnapPixelThreshold)
            ? new(_playheadSeconds, SnapTarget.Playhead)
            : SnapPositionEx(pos.X / _pixelsPerSecond, clip.Id);

        double snappedX = cutSnap.Seconds * _pixelsPerSecond;
        double h        = TimelineCanvas.ActualHeight > 0 ? TimelineCanvas.ActualHeight : TimelineCanvas.Height;

        if (_cutPreviewLine is null)
        {
            _cutPreviewLine = new Line
            {
                Stroke           = new SolidColorBrush(Color.FromRgb(255, 200, 0)),
                StrokeThickness  = 1.5,
                StrokeDashArray  = new DoubleCollection { 4, 3 },
                IsHitTestVisible = false,
                Tag              = "cutpreview"
            };
            TimelineCanvas.Children.Add(_cutPreviewLine);
            Canvas.SetZIndex(_cutPreviewLine, 500);
        }

        if (_cutPreviewLabel is null)
        {
            _cutPreviewLabel = new TextBlock
            {
                FontSize         = 10,
                Foreground       = new SolidColorBrush(Color.FromRgb(255, 200, 0)),
                Background       = new SolidColorBrush(Color.FromArgb(180, 0, 0, 0)),
                Padding          = new Thickness(3, 1, 3, 1),
                IsHitTestVisible = false,
                Tag              = "cutpreview"
            };
            TimelineCanvas.Children.Add(_cutPreviewLabel);
            Canvas.SetZIndex(_cutPreviewLabel, 501);
        }

        _cutPreviewLine.X1 = snappedX;
        _cutPreviewLine.X2 = snappedX;
        _cutPreviewLine.Y1 = 0;
        _cutPreviewLine.Y2 = h;
        _cutPreviewLine.Visibility = Visibility.Visible;

        if (cutSnap.Target != SnapTarget.None)
        {
            _cutPreviewLabel.Text = cutSnap.Target == SnapTarget.Playhead ? "Cut: Playhead" : "Cut: Clip Edge";
            Canvas.SetLeft(_cutPreviewLabel, snappedX + 4);
            Canvas.SetTop(_cutPreviewLabel, 6);
            _cutPreviewLabel.Visibility = Visibility.Visible;
        }
        else
        {
            _cutPreviewLabel.Visibility = Visibility.Collapsed;
        }
    }

    private void ShowSnapGuide(SnapResult snap)
    {
        double x     = snap.Seconds * _pixelsPerSecond;
        double h     = TimelineCanvas.ActualHeight > 0 ? TimelineCanvas.ActualHeight : 600;
        string label = snap.Target == SnapTarget.Playhead ? "Snap: Playhead" : "Snap: Clip Edge";

        if (_snapGuideLine is null)
        {
            _snapGuideLine = new Line
            {
                Stroke           = new SolidColorBrush(Color.FromRgb(250, 200, 0)),
                StrokeThickness  = 1.0,
                IsHitTestVisible = false,
                Tag              = "snapguide"
            };
            TimelineCanvas.Children.Add(_snapGuideLine);
            Canvas.SetZIndex(_snapGuideLine, 490);
        }

        if (_snapGuideLabel is null)
        {
            _snapGuideLabel = new TextBlock
            {
                FontSize         = 10,
                Foreground       = new SolidColorBrush(Color.FromRgb(250, 200, 0)),
                Background       = new SolidColorBrush(Color.FromArgb(180, 0, 0, 0)),
                Padding          = new Thickness(3, 1, 3, 1),
                IsHitTestVisible = false,
                Tag              = "snapguide"
            };
            TimelineCanvas.Children.Add(_snapGuideLabel);
            Canvas.SetZIndex(_snapGuideLabel, 491);
        }

        _snapGuideLine.X1         = x;
        _snapGuideLine.X2         = x;
        _snapGuideLine.Y1         = 0;
        _snapGuideLine.Y2         = h;
        _snapGuideLine.Visibility = Visibility.Visible;

        _snapGuideLabel.Text       = label;
        Canvas.SetLeft(_snapGuideLabel, x + 4);
        Canvas.SetTop(_snapGuideLabel, 6);
        _snapGuideLabel.Visibility = Visibility.Visible;
    }

    private void HideSnapGuide()
    {
        if (_snapGuideLine  != null) _snapGuideLine.Visibility  = Visibility.Collapsed;
        if (_snapGuideLabel != null) _snapGuideLabel.Visibility = Visibility.Collapsed;
    }

    
    
    

    private void SplitSelectedClipAtPlayhead()
    {
        if (_selectedClipId == Guid.Empty) return;
        var (trackIdx, clip) = FindClipInAllTracks(_selectedClipId);
        if (clip is null) return;

        double trimStart = clip.LocalTrimStartSeconds ?? 0.0;
        double full      = GetCachedFullDuration(clip);
        double trimEnd   = clip.LocalTrimEndSeconds   ?? full;
        double cutInClip = (_playheadSeconds - clip.StartOffsetSeconds) + trimStart;

        if (cutInClip < trimStart + MinClipDuration || cutInClip > trimEnd - MinClipDuration)
        {
            StatusText.Text = "Playhead is too close to clip edge to split.";
            return;
        }

        PushUndo();

        var clipA = new MixClip
        {
            SoundItemId           = clip.SoundItemId,
            StartOffsetSeconds    = clip.StartOffsetSeconds,
            LocalTrimStartSeconds = clip.LocalTrimStartSeconds,
            LocalTrimEndSeconds   = cutInClip,
            Volume                = clip.Volume,
            FadeInSeconds         = clip.FadeInSeconds,
            FadeOutSeconds        = 0,                      
        };
        var clipB = new MixClip
        {
            SoundItemId           = clip.SoundItemId,
            StartOffsetSeconds    = _playheadSeconds,
            LocalTrimStartSeconds = cutInClip,
            LocalTrimEndSeconds   = clip.LocalTrimEndSeconds,
            Volume                = clip.Volume,
            FadeInSeconds         = 0,                      
            FadeOutSeconds        = clip.FadeOutSeconds,
        };

        var clips = _tracks[trackIdx].Clips;
        int idx   = clips.IndexOf(clip);
        clips.RemoveAt(idx);
        clips.Insert(idx,     clipA);
        clips.Insert(idx + 1, clipB);

        _selectedClipId = clipA.Id;
        RebuildTimeline();
        StatusText.Text = "Split at playhead.";
    }

    
    
    

    private void CopyClip()
    {
        if (_selectedClipId == Guid.Empty) return;
        var (trackIdx, clip) = FindClipInAllTracks(_selectedClipId);
        if (clip is null) return;

        _clipboard = new MixClip
        {
            SoundItemId           = clip.SoundItemId,
            StartOffsetSeconds    = clip.StartOffsetSeconds,
            LocalTrimStartSeconds = clip.LocalTrimStartSeconds,
            LocalTrimEndSeconds   = clip.LocalTrimEndSeconds,
            Volume                = clip.Volume,
            FadeInSeconds         = clip.FadeInSeconds,
            FadeOutSeconds        = clip.FadeOutSeconds,
        };
        _clipboardSourceTrackIndex = trackIdx;
        StatusText.Text = "Clip copied.";
    }

    private void PasteClip()
    {
        if (_clipboard is null) return;
        int targetTrack = _selectedTrackIndex >= 0 && _selectedTrackIndex < _tracks.Count
            ? _selectedTrackIndex
            : (_clipboardSourceTrackIndex >= 0 && _clipboardSourceTrackIndex < _tracks.Count
                ? _clipboardSourceTrackIndex : 0);
        if (_tracks.Count == 0) return;

        PushUndo();
        var pasted = new MixClip
        {
            SoundItemId           = _clipboard.SoundItemId,
            StartOffsetSeconds    = _playheadSeconds,
            LocalTrimStartSeconds = _clipboard.LocalTrimStartSeconds,
            LocalTrimEndSeconds   = _clipboard.LocalTrimEndSeconds,
            Volume                = _clipboard.Volume,
            FadeInSeconds         = _clipboard.FadeInSeconds,
            FadeOutSeconds        = _clipboard.FadeOutSeconds,
        };
        _tracks[targetTrack].Clips.Add(pasted);
        _selectedClipId = pasted.Id;
        RebuildTimeline();
        StatusText.Text = "Clip pasted at playhead.";
    }

    private void DuplicateClip()
    {
        if (_selectedClipId == Guid.Empty) return;
        var (trackIdx, clip) = FindClipInAllTracks(_selectedClipId);
        if (clip is null) return;

        PushUndo();
        double effectiveDur = GetClipEffectiveDuration(clip);
        var dup = new MixClip
        {
            SoundItemId           = clip.SoundItemId,
            StartOffsetSeconds    = clip.StartOffsetSeconds + effectiveDur,
            LocalTrimStartSeconds = clip.LocalTrimStartSeconds,
            LocalTrimEndSeconds   = clip.LocalTrimEndSeconds,
            Volume                = clip.Volume,
            FadeInSeconds         = clip.FadeInSeconds,
            FadeOutSeconds        = clip.FadeOutSeconds,
        };
        _tracks[trackIdx].Clips.Add(dup);
        _selectedClipId = dup.Id;
        RebuildTimeline();
        StatusText.Text = "Clip duplicated.";
    }

    
    
    

    private void DeleteLeft()
    {
        if (_selectedClipId == Guid.Empty) return;
        var (_, clip) = FindClipInAllTracks(_selectedClipId);
        if (clip is null) return;

        double trimStart    = clip.LocalTrimStartSeconds ?? 0.0;
        double full         = GetCachedFullDuration(clip);
        double trimEnd      = clip.LocalTrimEndSeconds   ?? full;
        double newTrimStart = (_playheadSeconds - clip.StartOffsetSeconds) + trimStart;

        if (newTrimStart <= trimStart + MinClipDuration) return;
        if (newTrimStart >= trimEnd   - MinClipDuration) return;

        PushUndo();
        double actualDelta = newTrimStart - trimStart;
        clip.StartOffsetSeconds    = Math.Max(0.0, clip.StartOffsetSeconds + actualDelta);
        clip.LocalTrimStartSeconds = newTrimStart > 0.001 ? newTrimStart : (double?)null;
        RebuildTimeline();
        StatusText.Text = "Deleted left of playhead.";
    }

    private void DeleteRight()
    {
        if (_selectedClipId == Guid.Empty) return;
        var (_, clip) = FindClipInAllTracks(_selectedClipId);
        if (clip is null) return;

        double trimStart = clip.LocalTrimStartSeconds ?? 0.0;
        double full      = GetCachedFullDuration(clip);
        double trimEnd   = clip.LocalTrimEndSeconds   ?? full;
        double newTrimEnd = (_playheadSeconds - clip.StartOffsetSeconds) + trimStart;

        if (newTrimEnd >= trimEnd   - MinClipDuration) return;
        if (newTrimEnd <= trimStart + MinClipDuration) return;

        PushUndo();
        clip.LocalTrimEndSeconds = newTrimEnd < full - 0.001 ? newTrimEnd : (double?)null;
        RebuildTimeline();
        StatusText.Text = "Deleted right of playhead.";
    }

    private void DeleteLeft_Click(object sender, RoutedEventArgs e)  => DeleteLeft();
    private void DeleteRight_Click(object sender, RoutedEventArgs e) => DeleteRight();

    
    
    

    private void Window_PreviewKeyDown(object sender, KeyEventArgs e)
    {
        bool ctrl  = (e.KeyboardDevice.Modifiers & ModifierKeys.Control) != 0;
        bool shift = (e.KeyboardDevice.Modifiers & ModifierKeys.Shift)   != 0;

        if (Keyboard.FocusedElement is System.Windows.Controls.TextBox or ComboBox) return;

        switch (e.Key)
        {
            case Key.Z when ctrl && !shift: Undo();                        e.Handled = true; break;
            case Key.Y when ctrl:           Redo();                        e.Handled = true; break;
            case Key.Z when ctrl && shift:  Redo();                        e.Handled = true; break;
            case Key.A when !ctrl:          SetTool(ComposerTool.Select);  e.Handled = true; break;
            case Key.C when !ctrl:          SetTool(ComposerTool.Cut);     e.Handled = true; break;
            case Key.R when !ctrl:          SetTool(ComposerTool.Ripple);  e.Handled = true; break;
            case Key.C when ctrl:           CopyClip();                    e.Handled = true; break;
            case Key.V when ctrl:           PasteClip();                   e.Handled = true; break;
            case Key.D when ctrl:           DuplicateClip();               e.Handled = true; break;
            case Key.S when ctrl: Save_Click(this, e); e.Handled = true; break;
            case Key.S when !ctrl:          SplitSelectedClipAtPlayhead(); e.Handled = true; break;
            case Key.Space:
                if (StopButton.IsEnabled) StopAll();
                else                      StartPreview();
                e.Handled = true;
                break;
            case Key.Delete:
                if (_selectedClipId != Guid.Empty)
                {
                    DeleteSelectedClip();
                    e.Handled = true;
                }
                break;
        }
    }

    private void DeleteSelectedClip()
    {
        if (_selectedClipId == Guid.Empty) return;
        var (trackIdx, found) = FindClipInAllTracks(_selectedClipId);
        if (found is null) return;
        PushUndo();
        RemoveClip(trackIdx, _selectedClipId);
        RebuildTimeline();
        StatusText.Text = "Clip removed.";
    }

    
    
    

    private void SelectTool_Click(object sender, RoutedEventArgs e) =>
        SetTool(ComposerTool.Select);

    private void CutTool_Click(object sender, RoutedEventArgs e) =>
        SetTool(ComposerTool.Cut);

    private void RippleTool_Click(object sender, RoutedEventArgs e) =>
        SetTool(ComposerTool.Ripple);

    private void SetTool(ComposerTool tool)
    {
        _activeTool = tool;
        SetToolVisual();
        RebuildTimeline();
        StatusText.Text = tool switch
        {
            ComposerTool.Select => "Select — click to select, drag to move/trim, drag between rows to change track",
            ComposerTool.Cut    => "Cut — click inside a clip to split it (snap to playhead/edges when Snap is on)",
            ComposerTool.Ripple => "Ripple — drag a clip to push/pull it and all clips after it on the same track",
            _                   => string.Empty
        };
    }

    private void SetToolVisual()
    {
        SelectToolButton.Appearance = _activeTool == ComposerTool.Select
            ? Wpf.Ui.Controls.ControlAppearance.Primary
            : Wpf.Ui.Controls.ControlAppearance.Secondary;
        CutToolButton.Appearance = _activeTool == ComposerTool.Cut
            ? Wpf.Ui.Controls.ControlAppearance.Primary
            : Wpf.Ui.Controls.ControlAppearance.Secondary;
        RippleToolButton.Appearance = _activeTool == ComposerTool.Ripple
            ? Wpf.Ui.Controls.ControlAppearance.Primary
            : Wpf.Ui.Controls.ControlAppearance.Secondary;
    }

    private void TrimHandlesCheckBox_Click(object sender, RoutedEventArgs e) => RebuildTimeline();

    
    
    

    private void ZoomIn_Click(object sender, RoutedEventArgs e)
    {
        _pixelsPerSecond = Math.Min(MaxPixelsPerSecond, _pixelsPerSecond * 1.4);
        _clipFullDurations.Clear();
        RebuildTimeline();
    }

    private void ZoomOut_Click(object sender, RoutedEventArgs e)
    {
        _pixelsPerSecond = Math.Max(MinPixelsPerSecond, _pixelsPerSecond / 1.4);
        _clipFullDurations.Clear();
        RebuildTimeline();
    }

    private void ZoomFit_Click(object sender, RoutedEventArgs e)
    {
        double totalDur = GetTotalDuration();
        if (totalDur < 0.5) { _pixelsPerSecond = 100.0; RebuildTimeline(); return; }

        double viewportWidth = TimelineScrollViewer.ViewportWidth;
        if (viewportWidth < 100) viewportWidth = 800;

        _pixelsPerSecond = Math.Clamp((viewportWidth - 20) / totalDur, MinPixelsPerSecond, MaxPixelsPerSecond);
        _clipFullDurations.Clear();
        RebuildTimeline();
    }

    
    
    

    private void TimelineScrollViewer_PreviewMouseWheel(object sender, MouseWheelEventArgs e)
    {
        bool ctrl = (Keyboard.Modifiers & ModifierKeys.Control) != 0;
        bool alt  = (Keyboard.Modifiers & ModifierKeys.Alt)     != 0;

        if (ctrl)
        {
            
            double maxOffset = Math.Max(0.0, TimelineScrollViewer.ScrollableWidth);
            double newOffset = Math.Clamp(
                TimelineScrollViewer.HorizontalOffset - e.Delta * 0.5,
                0.0, maxOffset);
            TimelineScrollViewer.ScrollToHorizontalOffset(newOffset);
            e.Handled = true;
        }
        else if (alt)
        {
            
            var    canvasPos    = e.GetPosition(TimelineCanvas);
            double mouseSeconds = canvasPos.X / _pixelsPerSecond;
            double viewportX    = canvasPos.X - TimelineScrollViewer.HorizontalOffset;

            double oldPPS = _pixelsPerSecond;
            _pixelsPerSecond = e.Delta > 0
                ? Math.Min(MaxPixelsPerSecond, _pixelsPerSecond * 1.2)
                : Math.Max(MinPixelsPerSecond, _pixelsPerSecond / 1.2);

            if (Math.Abs(_pixelsPerSecond - oldPPS) < 0.01) { e.Handled = true; return; }

            _clipFullDurations.Clear();
            RebuildTimeline();
            TimelineScrollViewer.ScrollToHorizontalOffset(
                Math.Max(0, mouseSeconds * _pixelsPerSecond - viewportX));
            e.Handled = true;
        }
    }

    
    
    

    
    
    private List<(Guid ClipId, double OrigStart, double OrigEnd)> BuildTrackSnapshot(
        Guid movingClipId, int trackIdx)
    {
        if (trackIdx < 0 || trackIdx >= _tracks.Count)
            return new();
        return _tracks[trackIdx].Clips
            .Where(c => c.Id != movingClipId)
            .OrderBy(c => c.StartOffsetSeconds)
            .Select(c => (c.Id, c.StartOffsetSeconds, c.StartOffsetSeconds + GetClipEffectiveDuration(c)))
            .ToList();
    }

    
    
    
    
    
    
    
    
    
    
    
    
    
    
    private double ResolveFreeInsertStartTime(
        MixClip movingClip, int targetTrackIdx, double proposedStart)
    {
        if (targetTrackIdx < 0 || targetTrackIdx >= _tracks.Count)
            return Math.Max(0.0, proposedStart);

        _dragTrackSnapshot ??= BuildTrackSnapshot(movingClip.Id, targetTrackIdx);
        var snap = _dragTrackSnapshot;
        int N = snap.Count;

        
        foreach (var entry in snap)
        {
            var c = _tracks[targetTrackIdx].Clips.FirstOrDefault(x => x.Id == entry.ClipId);
            if (c != null) c.StartOffsetSeconds = entry.OrigStart;
        }

        if (N == 0)
            return Math.Max(0.0, proposedStart);

        double movingDur    = GetClipEffectiveDuration(movingClip);
        double proposedEnd  = proposedStart + movingDur;
        double proposedCent = proposedStart + movingDur / 2.0;

        
        
        int insertIdx = 0;
        for (int i = 0; i < N; i++)
        {
            double snapCent = (snap[i].OrigStart + snap[i].OrigEnd) / 2.0;
            if (proposedCent > snapCent) insertIdx = i + 1;
        }

        
        bool overlapsLeft  = insertIdx > 0 && proposedStart < snap[insertIdx - 1].OrigEnd;
        bool overlapsRight = insertIdx < N && proposedEnd    > snap[insertIdx].OrigStart;

        if (!overlapsLeft && !overlapsRight)
            return Math.Max(0.0, proposedStart); 

        int M = _dragMoverOrigSlotIdx;

        if (insertIdx == M)
        {
            
            double lb = M > 0 ? snap[M - 1].OrigEnd : 0.0;
            double rb = M < N ? snap[M].OrigStart - movingDur : double.MaxValue;
            return Math.Clamp(Math.Max(0.0, proposedStart), lb, Math.Max(lb, rb));
        }

        
        
        
        
        
        
        int lo = Math.Min(M, insertIdx);
        int hi = Math.Max(M, insertIdx);

        
        
        
        double anchorStart = (lo == M) ? _dragOriginalOffset : snap[lo].OrigStart;

        
        
        
        var newOrder = new List<MixClip>(hi - lo + 1);
        if (insertIdx > M)
        {
            for (int si = M; si < insertIdx; si++)
            {
                var c = _tracks[targetTrackIdx].Clips.FirstOrDefault(x => x.Id == snap[si].ClipId);
                if (c != null) newOrder.Add(c);
            }
            newOrder.Add(movingClip);
        }
        else
        {
            newOrder.Add(movingClip);
            for (int si = insertIdx; si < M; si++)
            {
                var c = _tracks[targetTrackIdx].Clips.FirstOrDefault(x => x.Id == snap[si].ClipId);
                if (c != null) newOrder.Add(c);
            }
        }

        
        double cursor     = anchorStart;
        double moverStart = anchorStart;
        foreach (var c in newOrder)
        {
            double dur = GetClipEffectiveDuration(c);
            if (c.Id == movingClip.Id)
                moverStart = cursor;
            else
                c.StartOffsetSeconds = cursor;
            cursor += dur;
        }

        return moverStart;
    }

    
    
    

    private ContextMenu BuildClipContextMenu(int trackIdx, MixClip clip)
    {
        var cm = new ContextMenu();

        MenuItem Make(string header, Action action, bool enabled = true)
        {
            var mi = new MenuItem { Header = header, IsEnabled = enabled };
            mi.Click += (_, _) => action();
            return mi;
        }

        cm.Items.Add(Make("Clip Audio Settings…",           () => OpenClipSettings(trackIdx, clip)));
        cm.Items.Add(new Separator());
        cm.Items.Add(Make("Copy  (Ctrl+C)",                () => { _selectedClipId = clip.Id; CopyClip(); }));
        cm.Items.Add(Make("Paste at Playhead  (Ctrl+V)",   () => PasteClip(), _clipboard is not null));
        cm.Items.Add(Make("Duplicate  (Ctrl+D)",           () => { _selectedClipId = clip.Id; DuplicateClip(); }));
        cm.Items.Add(new Separator());
        cm.Items.Add(Make("Split at Playhead  (S)",        () => { _selectedClipId = clip.Id; SplitSelectedClipAtPlayhead(); }));
        cm.Items.Add(Make("Delete Left of Playhead",       () => { _selectedClipId = clip.Id; DeleteLeft(); }));
        cm.Items.Add(Make("Delete Right of Playhead",      () => { _selectedClipId = clip.Id; DeleteRight(); }));
        cm.Items.Add(new Separator());
        cm.Items.Add(Make("Move to Track Above",           () => MoveClipToTrack(clip.Id, trackIdx - 1), trackIdx > 0));
        cm.Items.Add(Make("Move to Track Below",           () => MoveClipToTrack(clip.Id, trackIdx + 1), trackIdx < _tracks.Count - 1));
        cm.Items.Add(new Separator());
        cm.Items.Add(Make("Ripple From Here",              () => { _selectedClipId = clip.Id; SetTool(ComposerTool.Ripple); }));
        cm.Items.Add(new Separator());
        cm.Items.Add(Make("Delete Clip",                   () => { _selectedClipId = clip.Id; DeleteSelectedClip(); }));

        return cm;
    }

    private void MoveClipToTrack(Guid clipId, int toTrackIdx)
    {
        var (fromTrackIdx, clip) = FindClipInAllTracks(clipId);
        if (clip is null || fromTrackIdx == toTrackIdx) return;
        if (toTrackIdx < 0 || toTrackIdx >= _tracks.Count) return;

        PushUndo();
        _tracks[fromTrackIdx].Clips.Remove(clip);
        _tracks[toTrackIdx].Clips.Add(clip);
        CleanEmptyTracks();
        
        var (destTrack, _) = FindClipInAllTracks(clipId);
        _selectedTrackIndex = destTrack >= 0 ? destTrack : Math.Max(0, _tracks.Count - 1);
        RebuildTimeline();
    }

    
    
    

    private bool TryGetSelectedClip(out int trackIndex, out MixClip clip)
    {
        if (_selectedClipId == Guid.Empty) { trackIndex = -1; clip = null!; return false; }
        var (ti, c) = FindClipInAllTracks(_selectedClipId);
        if (c is null) { trackIndex = -1; clip = null!; return false; }
        trackIndex = ti;
        clip = c;
        return true;
    }

    private void RefreshSelectedClipPanel()
    {
        if (!TryGetSelectedClip(out int trackIndex, out var clip))
        {
            ClipInfoPlaceholder.Visibility = Visibility.Visible;
            ClipInfoContent.Visibility     = Visibility.Collapsed;
            return;
        }

        ClipInfoPlaceholder.Visibility = Visibility.Collapsed;
        ClipInfoContent.Visibility     = Visibility.Visible;

        string sourceName = FindSoundItem(clip.SoundItemId)?.DisplayName ?? "?";
        double effDur     = GetClipEffectiveDuration(clip);

        PanelSourceLabel.Text   = sourceName;
        PanelTrackLabel.Text    = $"Track {trackIndex + 1}";
        PanelStartLabel.Text    = PanelFormatTime(clip.StartOffsetSeconds);
        PanelDurationLabel.Text = PanelFormatTime(effDur);

        
        PanelVolumeSlider.ValueChanged -= PanelVolumeSlider_ValueChanged;
        PanelVolumeSlider.Value         = Math.Round(clip.Volume * 100.0, 0);
        PanelVolumeSlider.ValueChanged += PanelVolumeSlider_ValueChanged;
        PanelVolumeLabel.Text           = $"{PanelVolumeSlider.Value:0}%";

        PanelFadeInBox.Text  = clip.FadeInSeconds.ToString("0.##",
            System.Globalization.CultureInfo.InvariantCulture);
        PanelFadeOutBox.Text = clip.FadeOutSeconds.ToString("0.##",
            System.Globalization.CultureInfo.InvariantCulture);
    }

    private void PanelVolumeSlider_ValueChanged(object sender,
        RoutedPropertyChangedEventArgs<double> e)
    {
        if (PanelVolumeLabel is null) return;
        PanelVolumeLabel.Text = $"{PanelVolumeSlider.Value:0}%";
    }

    private void PanelApply_Click(object sender, RoutedEventArgs e)
    {
        if (!TryGetSelectedClip(out _, out var clip)) return;

        double effDur  = GetClipEffectiveDuration(clip);
        double maxFade = effDur > 0 ? effDur / 2.0 : 0.0;

        float  newVolume  = (float)(PanelVolumeSlider.Value / 100.0);
        double newFadeIn  = ParsePanelFade(PanelFadeInBox,  maxFade);
        double newFadeOut = ParsePanelFade(PanelFadeOutBox, maxFade);

        if (newFadeIn + newFadeOut > effDur && effDur > 0)
        {
            double scale = effDur / (newFadeIn + newFadeOut);
            newFadeIn  *= scale;
            newFadeOut *= scale;
        }

        bool changed = Math.Abs(newVolume  - clip.Volume)         > 0.001f
                    || Math.Abs(newFadeIn  - clip.FadeInSeconds)  > 0.001
                    || Math.Abs(newFadeOut - clip.FadeOutSeconds) > 0.001;
        if (!changed) return;

        PushUndo();
        clip.Volume         = Math.Clamp(newVolume, 0.0f, 2.0f);
        clip.FadeInSeconds  = newFadeIn;
        clip.FadeOutSeconds = newFadeOut;
        RebuildTimeline();
    }

    private void PanelAudioSettings_Click(object sender, RoutedEventArgs e)
    {
        if (!TryGetSelectedClip(out int trackIndex, out var clip)) return;
        OpenClipSettings(trackIndex, clip);
    }

    private static double ParsePanelFade(TextBox box, double maxFade)
    {
        if (!double.TryParse(box.Text.Trim(),
                System.Globalization.NumberStyles.Any,
                System.Globalization.CultureInfo.InvariantCulture, out double v))
            v = 0.0;
        return Math.Clamp(v, 0.0, maxFade);
    }

    private static string PanelFormatTime(double seconds)
    {
        if (seconds < 60) return $"{seconds:0.00}s";
        int    m = (int)(seconds / 60);
        double s = seconds - m * 60.0;
        return $"{m}:{s:00.00}";
    }

    private void OpenClipSettings(int trackIdx, MixClip clip)
    {
        string sourceName = FindSoundItem(clip.SoundItemId)?.DisplayName ?? "?";
        double duration   = GetClipEffectiveDuration(clip);
        float  oldVolume  = clip.Volume;
        double oldFadeIn  = clip.FadeInSeconds;
        double oldFadeOut = clip.FadeOutSeconds;

        var dlg = new ClipSettingsDialog(Window.GetWindow(this), clip, sourceName, trackIdx + 1, duration);
        if (dlg.ShowDialog() != true) return;

        bool changed = Math.Abs(dlg.ResultVolume  - oldVolume)  > 0.001f
                    || Math.Abs(dlg.ResultFadeIn   - oldFadeIn)  > 0.001
                    || Math.Abs(dlg.ResultFadeOut  - oldFadeOut) > 0.001;
        if (!changed) return;

        PushUndo();
        clip.Volume         = dlg.ResultVolume;
        clip.FadeInSeconds  = dlg.ResultFadeIn;
        clip.FadeOutSeconds = dlg.ResultFadeOut;
        RebuildTimeline();

        var parts = new List<string> { $"{clip.Volume * 100:0}% vol" };
        if (clip.FadeInSeconds  > 0) parts.Add($"FI {clip.FadeInSeconds:0.##}s");
        if (clip.FadeOutSeconds > 0) parts.Add($"FO {clip.FadeOutSeconds:0.##}s");
        StatusText.Text = "Clip audio: " + string.Join(", ", parts);
    }

    
    
    

    private void TimelineScrollViewer_ScrollChanged(object sender, ScrollChangedEventArgs e)
    {
        if (e.VerticalChange != 0)
            LabelScrollViewer.ScrollToVerticalOffset(TimelineScrollViewer.VerticalOffset);
    }

    
    
    

    private void AddSound_Click(object sender, RoutedEventArgs e)
    {
        PickerAddToTrackButton.IsEnabled = _selectedTrackIndex >= 0
                                        && _selectedTrackIndex < _tracks.Count;
        PickerSearchBox.Text = "";
        PopulatePickerList("");
        SoundPickerOverlay.Visibility = Visibility.Visible;
        PickerSearchBox.Focus();
    }

    private void PopulatePickerList(string filter)
    {
        var filtered = _deckSounds
            .Where(s => _cachedSounds.ContainsKey(s.Id))
            .Where(s => string.IsNullOrWhiteSpace(filter)
                || s.DisplayName.Contains(filter, StringComparison.OrdinalIgnoreCase)
                || s.Category.Contains(filter, StringComparison.OrdinalIgnoreCase))
            .OrderBy(s => s.Category).ThenBy(s => s.DisplayName)
            .ToList();

        SoundPickerList.Items.Clear();
        foreach (var item in filtered)
            SoundPickerList.Items.Add(new ListBoxItem
            {
                Content = $"{item.DisplayName}  —  {item.Category}",
                Tag     = item
            });

        if (SoundPickerList.Items.Count > 0)
            SoundPickerList.SelectedIndex = 0;
    }

    private void PickerSearch_TextChanged(object sender, TextChangedEventArgs e) =>
        PopulatePickerList(PickerSearchBox.Text);

    private void PickerCancel_Click(object sender, RoutedEventArgs e) =>
        SoundPickerOverlay.Visibility = Visibility.Collapsed;

    private void PickerAddToTrack_Click(object sender, RoutedEventArgs e)
    {
        if (SoundPickerList.SelectedItem is not ListBoxItem { Tag: SoundItem item }) return;
        if (_selectedTrackIndex < 0 || _selectedTrackIndex >= _tracks.Count) return;
        SoundPickerOverlay.Visibility = Visibility.Collapsed;

        PushUndo();
        
        AddClipToTrack(_selectedTrackIndex, item, _playheadSeconds);
        RebuildTimeline();
        StatusText.Text = $"Added '{item.DisplayName}' to Track {_selectedTrackIndex + 1} at playhead.";
    }

    private void PickerAddNewTrack_Click(object sender, RoutedEventArgs e)
    {
        if (SoundPickerList.SelectedItem is not ListBoxItem { Tag: SoundItem item }) return;
        if (_tracks.Count >= MaxTracks)
        {
            StatusText.Text = $"Maximum {MaxTracks} tracks reached.";
            return;
        }
        SoundPickerOverlay.Visibility = Visibility.Collapsed;

        PushUndo();
        AddTrackWithClip(item, 0.0);
        RebuildTimeline();
        StatusText.Text = $"New track added: {item.DisplayName}";
    }

    
    
    

    private void Preview_Click(object sender, RoutedEventArgs e) => StartPreview();

    private async void StartPreview()
    {
        StopAll();
        if (!HasClips()) { StatusText.Text = "Add a sound to preview your mix."; return; }
        var engine = _engineProvider();
        if (engine is null) { StatusText.Text = "Choose a monitor output in Settings to preview audio."; return; }
        if (!AllSourcesAvailable()) return;
        PreviewButton.IsEnabled = false;
        StopButton.IsEnabled = true;
        StatusText.Text = "Preparing preview…";
        var tracksCopy = DeepCopyTracks();
        var itemsById = BuildSoundItemMap();
        var sounds = _cachedSounds.ToDictionary();
        double previewStart = _playheadSeconds;
        _previewCts = new CancellationTokenSource();
        var token = _previewCts.Token;
        try
        {
            var mix = await Task.Run(() => MixRenderer.Render(tracksCopy, itemsById, sounds), token);
            if (token.IsCancellationRequested || _disposed) return;
            if (mix.Length == 0) throw new InvalidOperationException("This mix has no audio.");
            int sr = CachedSound.TargetFormat.SampleRate * CachedSound.TargetFormat.Channels;
            int start = Math.Clamp((int)(previewStart * CachedSound.TargetFormat.SampleRate) * 2, 0, mix.Length);
            if (start >= mix.Length) { start = 0; previewStart = 0; }
            _previewEngine = engine;
            _previewHandle = engine.Play(new CachedSound(mix), 1f, start, mix.Length, 0, 0);
            _playheadSeconds = previewStart;
            StatusText.Text = "Previewing on your monitor output.";
            StartPreviewTimer(previewStart);
        }
        catch (OperationCanceledException) { }
        catch (Exception ex)
        {
            if (token.IsCancellationRequested || _disposed) return;
            AppLogger.Error("SoundEditor", "Preview failed", ex);
            StopAll();
            StatusText.Text = $"Preview failed: {ex.Message}";
        }
    }

    private void StartPreviewTimer(double previewStartSeconds)
    {
        double startWall = Environment.TickCount64 / 1000.0;

        _previewTimer?.Stop();
        _previewTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(50) };
        _previewTimer.Tick += (_, _) =>
        {
            if (_previewHandle is null || _previewHandle.IsFinished)
            {
                StopAll();
                return;
            }
            double elapsed = Environment.TickCount64 / 1000.0 - startWall;
            _playheadSeconds = previewStartSeconds + elapsed;
            MovePlayheadVisual();
            UpdateTransportTime();
        };
        _previewTimer.Start();
    }

    private void Stop_Click(object sender, RoutedEventArgs e) => StopAll();

    private void StopAll()
    {
        _previewCts?.Cancel();
        _previewCts?.Dispose();
        _previewCts = null;

        _previewTimer?.Stop();
        _previewTimer = null;

        try
        {
            if (_previewHandle is not null)
            {
                _previewEngine?.StopOne(_previewHandle.TopProvider);
                _previewHandle = null;
            }
        }
        catch {  }

        if (PreviewButton is not null)
        {
            PreviewButton.IsEnabled = true;
            StopButton.IsEnabled    = false;
            if (StatusText.Text is "Previewing…" or "Rendering preview…" or "Previewing on your monitor output." or "Preparing preview…")
                StatusText.Text = "Stopped.";
        }
    }

    
    
    

    private bool _saving;

    private async void Save_Click(object sender, RoutedEventArgs e)
    {
        if (IsSaving) return;
        if (!HasClips()) { StatusText.Text = "Add at least one clip before saving."; return; }
        string mixName = MixNameBox.Text.Trim();
        if (string.IsNullOrWhiteSpace(mixName)) { StatusText.Text = "Give your mix a name first."; MixNameBox.Focus(); return; }
        if (!AllSourcesAvailable()) return;
        var target = CaptureSaveTarget?.Invoke();
        if (target is null) { StatusText.Text = "No destination soundboard is available."; return; }
        StopAll();
        _saving = true;
        SaveButton.IsEnabled = false;
        StatusText.Text = "Rendering your mix…";
        var tracks = DeepCopyTracks();
        var items = BuildSoundItemMap();
        var sounds = _cachedSounds.ToDictionary();
        string? outputPath = null;
        try
        {
            var result = await Task.Run(() =>
            {
                var mix = MixRenderer.Render(tracks, items, sounds);
                if (mix.Length == 0) throw new InvalidOperationException("This mix has no audio.");
                Directory.CreateDirectory(AppPaths.SoundsDirectory);
                var item = new SoundItem { DisplayName = mixName, Category = "Mixes", HotkeyInitialized = true, CreatedAt = DateTime.UtcNow };
                item.FilePath = System.IO.Path.Combine(AppPaths.SoundsDirectory, item.Id + ".wav");
                outputPath = item.FilePath;
                using (var writer = new WaveFileWriter(item.FilePath, CachedSound.TargetFormat)) writer.WriteSamples(mix, 0, mix.Length);
                return (Item: item, Sound: new CachedSound(mix));
            });
            if (_disposed) return;
            target(result.Item, result.Sound);
            outputPath = null;
            StatusText.Text = $"Saved {mixName} to your soundboard. Your editable mix stays open.";
            FlushDraft();
        }
        catch (Exception ex)
        {
            if (outputPath is not null && File.Exists(outputPath)) File.Delete(outputPath);
            StatusText.Text = $"Could not save mix: {ex.Message}";
            AppLogger.Error("SoundEditor", "Save failed", ex);
        }
        finally
        {
            _saving = false;
            SaveButton.IsEnabled = true;
            SaveFinished?.Invoke();
        }
    }

    private void Cancel_Click(object sender, RoutedEventArgs e)
    {
        if (IsSaving) return;
        StopAll();
        PushUndo();
        _tracks.Clear();
        _selectedClipId = Guid.Empty;
        _selectedTrackIndex = -1;
        _playheadSeconds = 0;
        MixNameBox.Text = "Untitled Mix";
        RebuildTimeline();
        StatusText.Text = "New mix ready. Undo restores your previous tracks.";
    }

    private bool HasClips() => _tracks.Any(t => t.Clips.Count > 0);

    private MixClip? FindClip(int trackIndex, Guid clipId)
    {
        if (trackIndex < 0 || trackIndex >= _tracks.Count) return null;
        return _tracks[trackIndex].Clips.FirstOrDefault(c => c.Id == clipId);
    }

    private (int TrackIndex, MixClip? Clip) FindClipInAllTracks(Guid clipId)
    {
        for (int i = 0; i < _tracks.Count; i++)
        {
            var c = _tracks[i].Clips.FirstOrDefault(x => x.Id == clipId);
            if (c != null) return (i, c);
        }
        return (-1, null);
    }

    private Dictionary<Guid, SoundItem> BuildSoundItemMap()
    {
        var map = new Dictionary<Guid, SoundItem>();
        foreach (var track in _tracks)
            foreach (var clip in track.Clips)
            {
                if (map.ContainsKey(clip.SoundItemId)) continue;
                var item = FindSoundItem(clip.SoundItemId);
                if (item != null) map[item.Id] = item;
            }
        return map;
    }

    private void Undo_Click(object sender, RoutedEventArgs e) => Undo();
    private void Redo_Click(object sender, RoutedEventArgs e) => Redo();
}
