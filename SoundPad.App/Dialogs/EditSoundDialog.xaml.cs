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
    // ── VisualBlock ───────────────────────────────────────────────────────────
    // Computed from _segments: maps each AudioSegment to its position in the
    // compact edited timeline. Rebuilt whenever _segments changes.
    private readonly struct VisualBlock
    {
        public int    SegmentIndex  { get; init; }
        public double SourceStart   { get; init; }
        public double SourceEnd     { get; init; }
        public double VisualStart   { get; init; }
        public double VisualEnd     { get; init; }
        public double SourceDuration => SourceEnd - SourceStart;
    }

    // ── Audio references ──────────────────────────────────────────────────────
    private readonly CachedSound           _sound;
    private readonly HashSet<(uint, uint)> _takenHotkeys;
    private readonly AudioPlaybackEngine?  _previewEngine;
    private HotkeyBinding?                _pendingHotkey;
    private PlaybackHandle?               _previewHandle;

    // ── Results ───────────────────────────────────────────────────────────────
    public string              ResultName       { get; private set; } = "";
    public string              ResultCategory   { get; private set; } = "General";
    public List<string>?       ResultTags       { get; private set; }
    public float               ResultVolume     { get; private set; } = 1f;
    public double?             ResultTrimStart  { get; private set; }
    public double?             ResultTrimEnd    { get; private set; }
    public double?             ResultFadeIn     { get; private set; }
    public double?             ResultFadeOut    { get; private set; }
    public HotkeyBinding?      ResultHotkey     { get; private set; }
    public bool                WasHotkeyChanged { get; private set; }
    public List<AudioSegment>? ResultSegments    { get; private set; }
    public bool                ResultReverseAudio  { get; private set; }
    public bool                ResultNormalizeAudio { get; private set; }
    public double              ResultPlaybackSpeed  { get; private set; } = 1.0;

    // ── Waveform data ─────────────────────────────────────────────────────────
    private const int WaveformBuckets = 360;
    private static readonly Brush            WaveformBrush  = new SolidColorBrush(Color.FromRgb(100, 149, 237));
    private static readonly DoubleCollection PlayheadDash   = new DoubleCollection { 4, 3 };
    private static readonly double[]         NiceIntervals  = { 0.05, 0.1, 0.2, 0.5, 1.0, 2.0, 5.0, 10.0, 15.0, 30.0, 60.0 };
    private float[]  _peaks    = Array.Empty<float>();
    private double   _duration; // raw source audio duration in seconds

    // ── Trim / fade source values ────────────────────────────────────────────
    // These mirror _segments[0].StartSeconds and _segments[^1].EndSeconds.
    // Kept in sync with segments for text-box display and validation.
    private double _vTrimStart;  // source seconds
    private double _vTrimEnd;    // source seconds
    private double _vFadeIn;
    private double _vFadeOut;
    private bool   _syncingFields;

    // ── Editor tool ───────────────────────────────────────────────────────────
    private enum EditorTool { Select, Cut }
    private EditorTool _tool = EditorTool.Select;

    // ── Segment state ─────────────────────────────────────────────────────────
    private readonly List<AudioSegment> _segments            = new();
    private int                         _selectedSegmentIndex = -1;
    private double?                     _cutHoverVisualSec;    // visual-time hover position (Cut mode)
    private bool                        _cutHoverIsSnapping;   // true when hover has snapped to playhead

    // ── Block-edge / playhead / reorder drag state ───────────────────────────
    private enum DragTarget { None, BlockLeft, BlockRight, Playhead, Block }
    private DragTarget _drag                     = DragTarget.None;
    private int        _trimBlockIdx             = -1;
    private double     _dragAnchorX;
    private double     _dragStartSrcValue;
    private double     _dragStartEditedDuration;

    // ── Block reorder state ───────────────────────────────────────────────────
    private bool _pendingReorder      = false;
    private int  _reorderDragBlockIdx = -1;
    private int  _reorderInsertBefore = 0;

    // ── Undo / Redo ───────────────────────────────────────────────────────────
    private readonly Stack<List<AudioSegment>> _undoStack = new();
    private readonly Stack<List<AudioSegment>> _redoStack = new();

    // ── Copy / Paste ──────────────────────────────────────────────────────────
    private AudioSegment? _copiedSegment;

    // ── Zoom ──────────────────────────────────────────────────────────────────
    private double _zoomFactor = 1.0;

    // ── Playback animation ────────────────────────────────────────────────────
    private DispatcherTimer? _playTimer;
    private DateTime         _playStartTime;
    private double           _playStartVisualSec; // visual-time position when preview began
    private double           _playheadVisualSec;  // current playhead position in visual/edited time

    // ── Constructor ───────────────────────────────────────────────────────────

    public EditSoundDialog(Window owner,
        SoundItem             item,
        CachedSound           sound,
        IEnumerable<string>   availableCategories,
        HashSet<(uint, uint)> takenHotkeys,
        AudioPlaybackEngine?  previewEngine)
    {
        InitializeComponent();
        Owner          = owner;
        _sound         = sound;
        _takenHotkeys  = takenHotkeys;
        _previewEngine = previewEngine;
        _pendingHotkey = item.Hotkey;
        _duration      = sound.Duration.TotalSeconds;

        // Base trim/fade values from item (overridden below if Segments are present).
        _vTrimStart = item.TrimStartSeconds ?? 0;
        _vTrimEnd   = item.TrimEndSeconds   ?? _duration;
        _vFadeIn    = item.FadeInSeconds    ?? 0;
        _vFadeOut   = item.FadeOutSeconds   ?? 0;

        // Initialize segment list.
        // If the item has saved segments, use them and derive outer trim from boundaries.
        // Otherwise create one block spanning the current trim range.
        if (item.Segments is not null && item.Segments.Count > 0)
        {
            _segments.AddRange(item.Segments);
            _vTrimStart = _segments[0].StartSeconds;
            _vTrimEnd   = _segments[^1].EndSeconds;
        }
        else
        {
            _segments.Add(new AudioSegment(_vTrimStart, _vTrimEnd));
        }
        _playheadVisualSec = 0; // visual time always starts at 0

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

        // ── Tags
        TagsBox.Text = item.Tags is { Count: > 0 } ? string.Join(", ", item.Tags) : "";

        // ── Effects
        ReverseCheckBox.IsChecked   = item.ReverseAudio;
        NormalizeCheckBox.IsChecked = item.NormalizeAudio;
        double safeSpeed = EffectProcessor.GetSafePlaybackSpeed(item);
        SpeedSlider.Value   = safeSpeed;
        SpeedValueText.Text = $"{safeSpeed:F2}×";

        // ── Volume
        int pct = (int)Math.Round(item.Volume * 100);
        VolumeSlider.Value = pct;
        VolumePctText.Text = $"{pct}%";
        VolumeSlider.ValueChanged += (_, e) => VolumePctText.Text = $"{(int)e.NewValue}%";

        // ── Pre-fill trim/fade text boxes from canonical values.
        // Fill TrimStart/TrimEnd from segment boundaries (not from item.TrimStartSeconds,
        // which is null when a saved Segments list is present).
        _syncingFields = true;
        if (_vTrimStart > 0)
            TrimStartBox.Text = _vTrimStart.ToString("G", CultureInfo.InvariantCulture);
        if (_vTrimEnd < _duration)
            TrimEndBox.Text   = _vTrimEnd.ToString("G", CultureInfo.InvariantCulture);
        if (item.FadeInSeconds.HasValue)
            FadeInBox.Text    = item.FadeInSeconds.Value.ToString("G", CultureInfo.InvariantCulture);
        if (item.FadeOutSeconds.HasValue)
            FadeOutBox.Text   = item.FadeOutSeconds.Value.ToString("G", CultureInfo.InvariantCulture);
        _syncingFields = false;

        TrimStartBox.TextChanged += TrimField_TextChanged;
        TrimEndBox.TextChanged   += TrimField_TextChanged;
        FadeInBox.TextChanged    += TrimField_TextChanged;
        FadeOutBox.TextChanged   += TrimField_TextChanged;

        // Clear cut hairline when mouse leaves canvas (safe: InitializeComponent has run).
        WaveformCanvas.MouseLeave += (_, _) =>
        {
            _cutHoverVisualSec  = null;
            _cutHoverIsSnapping = false;
            _pendingReorder     = false;
            if (_tool == EditorTool.Cut) RedrawCanvas();
        };

        // ── Hotkey
        RefreshHotkeyDisplay();

        // ── Duration / preview
        DurationText.Text       = $"Duration: {_duration:F2}s";
        PreviewButton.IsEnabled = previewEngine is not null;

        Closing += (_, _) => StopPreview();
        Loaded  += (_, _) => { NameBox.SelectAll(); NameBox.Focus(); };
        NameBox.KeyDown += (_, e) => { if (e.Key == Key.Enter) Save_Click(this, e); };
    }

    // ── Visual block helpers ──────────────────────────────────────────────────

    // Packs _segments into adjacent visual blocks with no gaps.
    // Block i occupies edited timeline [VisualStart, VisualEnd] corresponding
    // to source audio [SourceStart, SourceEnd].
    private List<VisualBlock> BuildVisualBlocks()
    {
        var result = new List<VisualBlock>(_segments.Count);
        double cursor = 0;
        for (int i = 0; i < _segments.Count; i++)
        {
            var seg = _segments[i];
            double dur = Math.Max(0, seg.EndSeconds - seg.StartSeconds);
            result.Add(new VisualBlock
            {
                SegmentIndex = i,
                SourceStart  = seg.StartSeconds,
                SourceEnd    = seg.EndSeconds,
                VisualStart  = cursor,
                VisualEnd    = cursor + dur
            });
            cursor += dur;
        }
        return result;
    }

    private double ComputeEditedDuration()
        => _segments.Sum(s => Math.Max(0, s.EndSeconds - s.StartSeconds));

    private static int FindBlockByVisualTime(List<VisualBlock> blocks, double vSec)
    {
        for (int i = 0; i < blocks.Count; i++)
            if (vSec >= blocks[i].VisualStart && vSec <= blocks[i].VisualEnd)
                return i;
        return -1;
    }

    // ── Coordinate helpers (visual/edited time ↔ canvas pixels) ──────────────

    private static double VisualToX(double vSec, double w, double editedDur)
    {
        if (editedDur <= 0 || w <= 0) return 0;
        return Math.Clamp(vSec / editedDur * w, 0, w);
    }

    private static double XToVisual(double x, double w, double editedDur)
    {
        if (w <= 0 || editedDur <= 0) return 0;
        return Math.Clamp(x / w * editedDur, 0, editedDur);
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

    // ── Canvas events ─────────────────────────────────────────────────────────

    private void WaveformScrollViewer_SizeChanged(object sender, SizeChangedEventArgs e)
        => SetCanvasWidth();

    private void SetCanvasWidth()
    {
        if (WaveformScrollViewer is null || WaveformCanvas is null) return;
        double viewW = WaveformScrollViewer.ActualWidth;
        if (viewW <= 0) return;
        double canvasW = viewW * _zoomFactor;
        WaveformCanvas.Width = canvasW;
        if (RulerCanvas is not null) RulerCanvas.Width = canvasW;
        RedrawCanvas();
    }

    private void ZoomSlider_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        _zoomFactor = e.NewValue;
        if (ZoomText is not null)
            ZoomText.Text = $"{(int)_zoomFactor}×";
        SetCanvasWidth();
        Dispatcher.InvokeAsync(ScrollToShowPlayhead, DispatcherPriority.Render);
    }

    private void Waveform_MouseDown(object sender, MouseButtonEventArgs e)
    {
        var    pos       = e.GetPosition(WaveformCanvas);
        double x         = pos.X;
        double y         = pos.Y;
        double w         = WaveformCanvas.ActualWidth;
        double h         = WaveformCanvas.ActualHeight;
        double editedDur = ComputeEditedDuration();

        if (_tool == EditorTool.Cut)
        {
            var (vSec, _) = ResolveSnapCut(x, w, editedDur);
            SplitBlockAtVisualTime(vSec); // PushUndo inside, after guards
        }
        else // Select
        {
            // Playhead handle hit zone: within 8px of playhead x and in the bottom 18px.
            double phX = VisualToX(Math.Clamp(_playheadVisualSec, 0, editedDur), w, editedDur);
            if (Math.Abs(x - phX) <= 8 && y >= h - 18)
            {
                StopPreview(); // stop any running preview before dragging
                _drag = DragTarget.Playhead;
                WaveformCanvas.CaptureMouse();
            }
            else
            {
                var blocks              = BuildVisualBlocks();
                var (edgeBlock, isLeft) = FindNearestBlockEdge(blocks, x, w, editedDur);

                if (edgeBlock >= 0)
                {
                    PushUndo();
                    _drag                    = isLeft ? DragTarget.BlockLeft : DragTarget.BlockRight;
                    _trimBlockIdx            = edgeBlock;
                    _dragAnchorX             = x;
                    _dragStartSrcValue       = isLeft
                        ? _segments[edgeBlock].StartSeconds
                        : _segments[edgeBlock].EndSeconds;
                    _dragStartEditedDuration = editedDur;
                    WaveformCanvas.CaptureMouse();
                }
                else
                {
                    double vSec = XToVisual(x, w, editedDur);
                    int    hit  = FindBlockByVisualTime(blocks, vSec);

                    _playheadVisualSec    = Math.Clamp(vSec, 0, editedDur);
                    _selectedSegmentIndex = hit >= 0
                        ? (hit == _selectedSegmentIndex ? -1 : hit)
                        : -1;

                    // Start potential block reorder if a block is now selected and
                    // there are multiple blocks to reorder.
                    _pendingReorder = false;
                    if (_selectedSegmentIndex >= 0 && _segments.Count > 1)
                    {
                        _pendingReorder      = true;
                        _reorderDragBlockIdx = _selectedSegmentIndex;
                        _dragAnchorX         = x;
                    }

                    UpdateSelectionUI();
                    RedrawCanvas();
                }
            }
        }

        e.Handled = true;
    }

    private void Waveform_MouseMove(object sender, MouseEventArgs e)
    {
        var    pos = e.GetPosition(WaveformCanvas);
        double x   = pos.X;
        double y   = pos.Y;
        double w   = WaveformCanvas.ActualWidth;
        double h   = WaveformCanvas.ActualHeight;

        // ── Playhead drag ─────────────────────────────────────────────────────
        if (_drag == DragTarget.Playhead)
        {
            double editedDur   = ComputeEditedDuration();
            _playheadVisualSec = Math.Clamp(XToVisual(x, w, editedDur), 0, editedDur);
            WaveformCanvas.Cursor = Cursors.Hand;
            RedrawCanvas();
            return;
        }

        // ── Block-edge trim drag ──────────────────────────────────────────────
        if (_drag == DragTarget.BlockLeft || _drag == DragTarget.BlockRight)
        {
            double timeDelta = (x - _dragAnchorX) / w * _dragStartEditedDuration;
            int    idx       = _trimBlockIdx;

            if (_drag == DragTarget.BlockLeft)
            {
                double maxStart = _segments[idx].EndSeconds - 0.02;
                double newSrc   = Math.Clamp(_dragStartSrcValue + timeDelta, 0, maxStart);
                _segments[idx]  = _segments[idx] with { StartSeconds = newSrc };
                if (idx == 0)
                {
                    _vTrimStart = newSrc;
                    WriteTextBox(TrimStartBox, newSrc, isDefault: newSrc <= 0);
                }
            }
            else
            {
                double minEnd  = _segments[idx].StartSeconds + 0.02;
                double newSrc  = Math.Clamp(_dragStartSrcValue + timeDelta, minEnd, _duration);
                _segments[idx] = _segments[idx] with { EndSeconds = newSrc };
                if (idx == _segments.Count - 1)
                {
                    _vTrimEnd = newSrc;
                    WriteTextBox(TrimEndBox, newSrc, isDefault: newSrc >= _duration);
                }
            }

            _playheadVisualSec = Math.Clamp(_playheadVisualSec, 0, ComputeEditedDuration());
            RedrawCanvas();
            return;
        }

        // ── Block reorder (active drag) ───────────────────────────────────────
        if (_drag == DragTarget.Block)
        {
            double rDur = ComputeEditedDuration();
            var    rBlk = BuildVisualBlocks();
            _reorderInsertBefore = ComputeInsertBefore(rBlk, x, w, rDur);
            WaveformCanvas.Cursor = Cursors.SizeAll;
            RedrawCanvas();
            return;
        }

        // ── Pending reorder — activate once threshold is exceeded ─────────────
        if (_pendingReorder && Math.Abs(x - _dragAnchorX) > 5)
        {
            double rDur = ComputeEditedDuration();
            var    rBlk = BuildVisualBlocks();
            _drag                = DragTarget.Block;
            _reorderInsertBefore = ComputeInsertBefore(rBlk, x, w, rDur);
            WaveformCanvas.CaptureMouse();
            RedrawCanvas();
            return;
        }

        if (_tool == EditorTool.Cut)
        {
            double editedDur             = ComputeEditedDuration();
            var (snappedSec, isSnapping) = ResolveSnapCut(x, w, editedDur);
            _cutHoverVisualSec           = snappedSec;
            _cutHoverIsSnapping          = isSnapping;
            WaveformCanvas.Cursor        = Cursors.Cross;
            RedrawCanvas();
            return;
        }

        // Select mode: cursor priority — playhead handle > block edge > default.
        double edur   = ComputeEditedDuration();
        double phXCur = VisualToX(Math.Clamp(_playheadVisualSec, 0, edur), w, edur);
        if (Math.Abs(x - phXCur) <= 8 && y >= h - 18)
        {
            WaveformCanvas.Cursor = Cursors.Hand;
            return;
        }

        var blocks  = BuildVisualBlocks();
        var (eb, _) = FindNearestBlockEdge(blocks, x, w, edur);
        WaveformCanvas.Cursor = eb >= 0 ? Cursors.SizeWE : Cursors.Arrow;
    }

    private void Waveform_MouseUp(object sender, MouseButtonEventArgs e)
    {
        if (_drag == DragTarget.Block)
            PerformReorder(_reorderDragBlockIdx, _reorderInsertBefore);

        _drag           = DragTarget.None;
        _pendingReorder = false;
        WaveformCanvas.ReleaseMouseCapture();
        if (WaveformCanvas is not null && _tool != EditorTool.Cut)
            WaveformCanvas.Cursor = Cursors.Arrow;
    }

    // ── Text box ↔ segment sync ───────────────────────────────────────────────

    private void TrimField_TextChanged(object sender, TextChangedEventArgs e)
    {
        if (_syncingFields) return;

        double newStart = ParseField(TrimStartBox.Text) ?? 0;
        double newEnd   = ParseField(TrimEndBox.Text)   ?? _duration;
        _vFadeIn  = ParseField(FadeInBox.Text)  ?? 0;
        _vFadeOut = ParseField(FadeOutBox.Text) ?? 0;

        _vTrimStart = Math.Clamp(newStart, 0, _duration);
        _vTrimEnd   = Math.Clamp(newEnd, _vTrimStart, _duration);
        if (_segments.Count > 0)
        {
            _segments[0]  = _segments[0]  with { StartSeconds = _vTrimStart };
            _segments[^1] = _segments[^1] with { EndSeconds   = _vTrimEnd };
        }

        _playheadVisualSec = Math.Clamp(_playheadVisualSec, 0, ComputeEditedDuration());

        if (WaveformCanvas?.ActualWidth > 0)
            RedrawCanvas();
    }

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

    // ── Segment operations ────────────────────────────────────────────────────

    private void SplitBlockAtVisualTime(double vSec)
    {
        var blocks = BuildVisualBlocks();
        int bidx   = FindBlockByVisualTime(blocks, vSec);
        if (bidx < 0) return;

        var    block  = blocks[bidx];
        double offset = vSec - block.VisualStart;
        double srcSec = block.SourceStart + offset;

        if (srcSec <= block.SourceStart + 0.01 || srcSec >= block.SourceEnd - 0.01) return;

        PushUndo();
        int idx = block.SegmentIndex;
        var seg = _segments[idx];
        _segments.RemoveAt(idx);
        _segments.Insert(idx,     new AudioSegment(seg.StartSeconds, srcSec));
        _segments.Insert(idx + 1, new AudioSegment(srcSec, seg.EndSeconds));
        _selectedSegmentIndex = -1;
        UpdateSelectionUI();
        RedrawCanvas();
    }

    private void DeleteSelectedSegment()
    {
        if (_selectedSegmentIndex < 0 || _selectedSegmentIndex >= _segments.Count) return;
        if (_segments.Count <= 1) return;
        RemoveSegmentAt(_selectedSegmentIndex);
    }

    private void RemoveSegmentAt(int idx)
    {
        if (idx < 0 || idx >= _segments.Count || _segments.Count <= 1) return;
        PushUndo();
        _segments.RemoveAt(idx);
        if (_selectedSegmentIndex == idx)
            _selectedSegmentIndex = -1;
        else if (_selectedSegmentIndex > idx)
            _selectedSegmentIndex--;
        SyncTrimFromSegments();
        _playheadVisualSec = Math.Clamp(_playheadVisualSec, 0, ComputeEditedDuration());
        UpdateSelectionUI();
        RedrawCanvas();
    }

    // Derives _vTrimStart/_vTrimEnd from the current first/last segments and
    // writes them back to the text boxes without triggering TextChanged.
    private void SyncTrimFromSegments()
    {
        if (_segments.Count == 0) return;
        _vTrimStart = _segments[0].StartSeconds;
        _vTrimEnd   = _segments[^1].EndSeconds;
        _syncingFields = true;
        TrimStartBox.Text = _vTrimStart > 0       ? _vTrimStart.ToString("G6", CultureInfo.InvariantCulture) : "";
        TrimEndBox.Text   = _vTrimEnd < _duration ? _vTrimEnd.ToString("G6",   CultureInfo.InvariantCulture)  : "";
        _syncingFields = false;
    }

    private void UpdateSelectionUI()
    {
        bool hasSelection = _selectedSegmentIndex >= 0 && _selectedSegmentIndex < _segments.Count;

        if (DeleteCutButton is not null)
            DeleteCutButton.IsEnabled = hasSelection && _segments.Count > 1;

        if (CopyButton is not null)
            CopyButton.IsEnabled = hasSelection;

        if (PasteButton is not null)
            PasteButton.IsEnabled = _copiedSegment is not null;

        if (BlockInfoText is null) return;

        if (hasSelection)
        {
            var blocks = BuildVisualBlocks();
            if (_selectedSegmentIndex < blocks.Count)
            {
                var blk = blocks[_selectedSegmentIndex];
                double dur = blk.SourceEnd - blk.SourceStart;
                BlockInfoText.Text =
                    $"Block {_selectedSegmentIndex + 1} of {_segments.Count}" +
                    $"  ·  Source {blk.SourceStart:F2}s – {blk.SourceEnd:F2}s" +
                    $"  ·  Duration {dur:F2}s";
            }
        }
        else
        {
            double editedDur = ComputeEditedDuration();
            BlockInfoText.Text = _segments.Count > 1
                ? $"{_segments.Count} blocks  ·  Edited: {editedDur:F2}s  (original: {_duration:F2}s)"
                : "";
        }
    }

    // ── Canvas drawing (ripple/compact layout) ────────────────────────────────

    private void RedrawCanvas()
    {
        var canvas = WaveformCanvas;
        if (canvas is null) return; // guard: fires during BAML loading before WaveformCanvas is assigned
        double w = canvas.ActualWidth;
        double h = canvas.ActualHeight;
        if (w <= 0 || h <= 0) return;

        canvas.Children.Clear();

        double cy     = h / 2.0;
        double maxAmp = cy * 0.88;

        var    blocks    = BuildVisualBlocks();
        double editedDur = blocks.Count > 0 ? blocks[^1].VisualEnd : 0;
        if (editedDur <= 0) return;

        if (DurationText is not null)
        {
            DurationText.Text = _segments.Count <= 1
                ? $"Duration: {_duration:F2}s"
                : $"Edited: {editedDur:F2}s  (original: {_duration:F2}s)";
        }

        // ── Draw each visual block ────────────────────────────────────────────
        for (int bi = 0; bi < blocks.Count; bi++)
        {
            var    blk = blocks[bi];
            double bx1 = VisualToX(blk.VisualStart, w, editedDur);
            double bx2 = VisualToX(blk.VisualEnd,   w, editedDur);
            double bw  = bx2 - bx1;
            if (bw <= 0) continue;

            AddRect(canvas, bx1, bw, h,
                new SolidColorBrush(Color.FromArgb(18, 100, 149, 237)));

            if (_peaks.Length > 0 && _duration > 0)
            {
                int pStart = (int)(blk.SourceStart / _duration * (_peaks.Length - 1));
                int pEnd   = (int)(blk.SourceEnd   / _duration * (_peaks.Length - 1));
                pStart = Math.Clamp(pStart, 0, _peaks.Length - 1);
                pEnd   = Math.Clamp(pEnd,   pStart, _peaks.Length - 1);

                for (int b = pStart; b <= pEnd; b++)
                {
                    double srcTime     = b / (double)(_peaks.Length - 1) * _duration;
                    double offsetInBlk = srcTime - blk.SourceStart;
                    double vSec        = blk.VisualStart + offsetInBlk;
                    double px          = VisualToX(vSec, w, editedDur);
                    double amp         = _peaks[b] * maxAmp;
                    canvas.Children.Add(new Line
                    {
                        X1 = px, Y1 = cy - amp, X2 = px, Y2 = cy + amp,
                        Stroke = WaveformBrush, StrokeThickness = 1.0,
                        IsHitTestVisible = false
                    });
                }
            }

            if (bi == _selectedSegmentIndex)
            {
                bool isDragging = _drag == DragTarget.Block && bi == _reorderDragBlockIdx;
                AddRect(canvas, bx1, bw, h,
                    new SolidColorBrush(Color.FromArgb((byte)(isDragging ? 20 : 40), 100, 149, 237)));
                var bdr = new Rectangle
                {
                    Width = bw, Height = h, Fill = Brushes.Transparent,
                    Stroke = new SolidColorBrush(Color.FromArgb(210, 100, 149, 237)),
                    StrokeThickness = 1.5,
                    IsHitTestVisible = false
                };
                if (isDragging) bdr.StrokeDashArray = new DoubleCollection { 4, 3 };
                Canvas.SetLeft(bdr, bx1); Canvas.SetTop(bdr, 0);
                canvas.Children.Add(bdr);
            }

            if (bi < blocks.Count - 1)
            {
                canvas.Children.Add(new Line
                {
                    X1 = bx2, Y1 = 0, X2 = bx2, Y2 = h,
                    Stroke = new SolidColorBrush(Color.FromArgb(170, 255, 255, 255)),
                    StrokeThickness = 1.5,
                    IsHitTestVisible = false
                });
            }
        }

        // ── Fade-in gradient ──────────────────────────────────────────────────
        if (_vFadeIn > 0)
        {
            double fiW = Math.Min(_vFadeIn / editedDur * w, w);
            if (fiW > 0)
            {
                var r = new Rectangle
                {
                    Width = fiW, Height = h,
                    Fill = new LinearGradientBrush(
                        Color.FromArgb(90, 50, 210, 100), Color.FromArgb(0, 50, 210, 100),
                        new Point(0, 0), new Point(1, 0)),
                    IsHitTestVisible = false
                };
                Canvas.SetLeft(r, 0); Canvas.SetTop(r, 0);
                canvas.Children.Add(r);
            }
        }

        // ── Fade-out gradient ─────────────────────────────────────────────────
        if (_vFadeOut > 0)
        {
            double foW = Math.Min(_vFadeOut / editedDur * w, w);
            double foX = w - foW;
            if (foW > 0)
            {
                var r = new Rectangle
                {
                    Width = foW, Height = h,
                    Fill = new LinearGradientBrush(
                        Color.FromArgb(0, 220, 130, 0), Color.FromArgb(90, 220, 130, 0),
                        new Point(0, 0), new Point(1, 0)),
                    IsHitTestVisible = false
                };
                Canvas.SetLeft(r, foX); Canvas.SetTop(r, 0);
                canvas.Children.Add(r);
            }
        }

        // ── Block edge handles (draggable trim zones) ─────────────────────────
        // Green on first-block left, orange-red on last-block right, blue elsewhere.
        const double HW = 4, HH = 14;
        for (int bi = 0; bi < blocks.Count; bi++)
        {
            double lx = VisualToX(blocks[bi].VisualStart, w, editedDur);
            double rx = VisualToX(blocks[bi].VisualEnd,   w, editedDur);

            var lh = new Rectangle
            {
                Width = HW, Height = HH, RadiusX = 1, RadiusY = 1,
                Fill = bi == 0
                    ? new SolidColorBrush(Color.FromArgb(220, 50, 210, 80))
                    : new SolidColorBrush(Color.FromArgb(200, 100, 180, 255)),
                IsHitTestVisible = false
            };
            Canvas.SetLeft(lh, lx); Canvas.SetTop(lh, 0);
            canvas.Children.Add(lh);

            var rh = new Rectangle
            {
                Width = HW, Height = HH, RadiusX = 1, RadiusY = 1,
                Fill = bi == blocks.Count - 1
                    ? new SolidColorBrush(Color.FromArgb(220, 255, 80, 50))
                    : new SolidColorBrush(Color.FromArgb(200, 100, 180, 255)),
                IsHitTestVisible = false
            };
            Canvas.SetLeft(rh, rx - HW); Canvas.SetTop(rh, h - HH);
            canvas.Children.Add(rh);
        }

        // ── Block reorder insertion indicator ─────────────────────────────────
        if (_drag == DragTarget.Block)
        {
            double insertX;
            if (_reorderInsertBefore <= 0)
                insertX = 0;
            else if (_reorderInsertBefore >= blocks.Count)
                insertX = w;
            else
                insertX = VisualToX(blocks[_reorderInsertBefore].VisualStart, w, editedDur);

            canvas.Children.Add(new Line
            {
                X1 = insertX, Y1 = 0, X2 = insertX, Y2 = h,
                Stroke = new SolidColorBrush(Color.FromArgb(240, 100, 200, 255)),
                StrokeThickness = 3,
                IsHitTestVisible = false
            });
        }

        // ── Playhead (white dashed line + draggable triangle handle) ─────────
        double phX = VisualToX(Math.Clamp(_playheadVisualSec, 0, editedDur), w, editedDur);
        canvas.Children.Add(new Line
        {
            X1 = phX, Y1 = 0, X2 = phX, Y2 = h,
            Stroke = Brushes.White, StrokeThickness = 1.5,
            StrokeDashArray = PlayheadDash,
            IsHitTestVisible = false
        });
        // Upward-pointing triangle anchored to the canvas bottom — the drag handle.
        canvas.Children.Add(new Polygon
        {
            Points = new PointCollection
            {
                new Point(phX - 6, h),
                new Point(phX + 6, h),
                new Point(phX,     h - 10)
            },
            Fill             = Brushes.White,
            IsHitTestVisible = false
        });

        // ── Cut hairline (yellow; brighter + thicker when snapped to playhead) ──
        if (_tool == EditorTool.Cut && _cutHoverVisualSec.HasValue)
        {
            double hx = VisualToX(_cutHoverVisualSec.Value, w, editedDur);
            canvas.Children.Add(new Line
            {
                X1 = hx, Y1 = 0, X2 = hx, Y2 = h,
                Stroke = _cutHoverIsSnapping
                    ? new SolidColorBrush(Color.FromArgb(255, 255, 220, 30))  // solid bright when snapped
                    : new SolidColorBrush(Color.FromArgb(210, 255, 255, 80)), // semi-transparent normal
                StrokeThickness  = _cutHoverIsSnapping ? 1.5 : 1.0,
                IsHitTestVisible = false
            });
        }

        RedrawRuler();
    }

    private static void AddRect(Canvas canvas, double x, double width, double height, Brush fill)
    {
        var r = new Rectangle { Width = width, Height = height, Fill = fill, IsHitTestVisible = false };
        Canvas.SetLeft(r, x); Canvas.SetTop(r, 0);
        canvas.Children.Add(r);
    }

    private void RedrawRuler()
    {
        var ruler = RulerCanvas;
        if (ruler is null) return;
        double w = ruler.ActualWidth;
        double h = ruler.ActualHeight;
        if (w <= 0 || h <= 0) return;

        ruler.Children.Clear();

        double editedDur = ComputeEditedDuration();
        if (editedDur <= 0) return;

        // Choose a tick interval that gives ~60px between major ticks.
        double pxPerSec   = w / editedDur;
        double rawInterval = 60.0 / pxPerSec;
        double tickInterval = NiceIntervals.FirstOrDefault(iv => iv >= rawInterval, NiceIntervals[^1]);

        double minorInterval = tickInterval / 2.0;
        bool   showMinor     = minorInterval * pxPerSec >= 8;

        int decimals  = tickInterval < 0.1 ? 2 : tickInterval < 1.0 ? 1 : 0;
        string fmt    = decimals > 0 ? $"F{decimals}" : "0";

        var labelBrush = new SolidColorBrush(Color.FromArgb(200, 200, 200, 200));
        var tickBrush  = new SolidColorBrush(Color.FromArgb(160, 180, 180, 180));
        var minorBrush = new SolidColorBrush(Color.FromArgb(90,  150, 150, 150));

        // Ruler baseline
        ruler.Children.Add(new Line
        {
            X1 = 0, Y1 = h - 1, X2 = w, Y2 = h - 1,
            Stroke = tickBrush, StrokeThickness = 1, IsHitTestVisible = false
        });

        // Major ticks + labels
        for (double t = 0; t <= editedDur + tickInterval * 0.01; t += tickInterval)
        {
            double x = VisualToX(t, w, editedDur);
            ruler.Children.Add(new Line
            {
                X1 = x, Y1 = h - 7, X2 = x, Y2 = h - 1,
                Stroke = tickBrush, StrokeThickness = 1, IsHitTestVisible = false
            });

            string label = t >= 60
                ? $"{(int)(t / 60)}:{(int)(t % 60):D2}"
                : t.ToString(fmt, System.Globalization.CultureInfo.InvariantCulture);
            var tb = new TextBlock
            {
                Text = label, FontSize = 9,
                Foreground = labelBrush, IsHitTestVisible = false
            };
            Canvas.SetLeft(tb, x + 2);
            Canvas.SetTop(tb, 1);
            ruler.Children.Add(tb);
        }

        // Minor ticks (halfway between major ticks)
        if (showMinor)
        {
            for (double t = minorInterval; t < editedDur; t += tickInterval)
            {
                double x = VisualToX(t, w, editedDur);
                ruler.Children.Add(new Line
                {
                    X1 = x, Y1 = h - 4, X2 = x, Y2 = h - 1,
                    Stroke = minorBrush, StrokeThickness = 1, IsHitTestVisible = false
                });
            }
        }
    }

    // ── Undo / Redo ───────────────────────────────────────────────────────────

    private void PushUndo()
    {
        _undoStack.Push(_segments.ToList()); // AudioSegment is a record — shallow copy is safe
        _redoStack.Clear();
        UpdateUndoButton();
        UpdateRedoButton();
    }

    private void Undo()
    {
        if (_undoStack.Count == 0) return;
        _redoStack.Push(_segments.ToList());
        var snapshot = _undoStack.Pop();
        _segments.Clear();
        _segments.AddRange(snapshot);
        _selectedSegmentIndex = _selectedSegmentIndex >= _segments.Count ? -1 : _selectedSegmentIndex;
        SyncTrimFromSegments();
        _playheadVisualSec = Math.Clamp(_playheadVisualSec, 0, ComputeEditedDuration());
        UpdateSelectionUI();
        UpdateUndoButton();
        UpdateRedoButton();
        RedrawCanvas();
    }

    private void Redo()
    {
        if (_redoStack.Count == 0) return;
        _undoStack.Push(_segments.ToList());
        var snapshot = _redoStack.Pop();
        _segments.Clear();
        _segments.AddRange(snapshot);
        _selectedSegmentIndex = Math.Clamp(_selectedSegmentIndex, -1, _segments.Count - 1);
        SyncTrimFromSegments();
        _playheadVisualSec = Math.Clamp(_playheadVisualSec, 0, ComputeEditedDuration());
        UpdateSelectionUI();
        UpdateUndoButton();
        UpdateRedoButton();
        RedrawCanvas();
    }

    private void UpdateUndoButton()
    {
        if (UndoButton is not null)
            UndoButton.IsEnabled = _undoStack.Count > 0;
    }

    private void UpdateRedoButton()
    {
        if (RedoButton is not null)
            RedoButton.IsEnabled = _redoStack.Count > 0;
    }

    private void UndoButton_Click(object sender, RoutedEventArgs e) => Undo();
    private void RedoButton_Click(object sender, RoutedEventArgs e) => Redo();

    // ── Snap-cut resolution ───────────────────────────────────────────────────

    // Returns the visual-time position where a Cut-tool click should split.
    // If "Snap cut to playhead" is checked, the mouse is within 10px of the
    // playhead, and the playhead falls in the same block as the mouse click,
    // the cut is redirected to _playheadVisualSec exactly.
    private (double vSec, bool snapped) ResolveSnapCut(double mouseX, double w, double editedDur)
    {
        double vSec = XToVisual(mouseX, w, editedDur);
        if (SnapToPlayheadCheckBox?.IsChecked != true) return (vSec, false);

        double phX = VisualToX(Math.Clamp(_playheadVisualSec, 0, editedDur), w, editedDur);
        if (Math.Abs(mouseX - phX) > 10) return (vSec, false);

        var blocks        = BuildVisualBlocks();
        int clickBlock    = FindBlockByVisualTime(blocks, vSec);
        int playheadBlock = FindBlockByVisualTime(blocks, _playheadVisualSec);
        if (clickBlock >= 0 && clickBlock == playheadBlock)
            return (_playheadVisualSec, true);

        return (vSec, false);
    }

    // ── Block-edge hit detection ──────────────────────────────────────────────

    private static (int blockIdx, bool isLeft) FindNearestBlockEdge(
        List<VisualBlock> blocks, double x, double w, double editedDur, double threshold = 7.0)
    {
        int    bestBlock = -1;
        bool   bestIsLeft = false;
        double bestDist  = threshold;

        for (int i = 0; i < blocks.Count; i++)
        {
            double lx = VisualToX(blocks[i].VisualStart, w, editedDur);
            double rx = VisualToX(blocks[i].VisualEnd,   w, editedDur);

            double rd = Math.Abs(x - rx);
            double ld = Math.Abs(x - lx);

            // Right edge of earlier block takes priority at ties (process right before left).
            if (rd < bestDist) { bestDist = rd; bestBlock = i; bestIsLeft = false; }
            if (ld < bestDist) { bestDist = ld; bestBlock = i; bestIsLeft = true; }
        }

        return (bestBlock, bestIsLeft);
    }

    // Returns the index before which the dragged block should be inserted
    // (0 = before block 0, Count = append at end), based on which half of each
    // block the mouse is over.
    private static int ComputeInsertBefore(List<VisualBlock> blocks, double mouseX, double w, double editedDur)
    {
        for (int i = 0; i < blocks.Count; i++)
        {
            double midX = VisualToX((blocks[i].VisualStart + blocks[i].VisualEnd) / 2.0, w, editedDur);
            if (mouseX < midX) return i;
        }
        return blocks.Count;
    }

    // Moves the segment at fromIdx so it appears at the position indicated by
    // insertBefore (in the pre-removal index space).
    private void PerformReorder(int fromIdx, int insertBefore)
    {
        if (fromIdx < 0 || fromIdx >= _segments.Count) return;
        int toIdx = insertBefore > fromIdx ? insertBefore - 1 : insertBefore;
        if (toIdx == fromIdx) return;

        PushUndo();
        var seg = _segments[fromIdx];
        _segments.RemoveAt(fromIdx);
        _segments.Insert(toIdx, seg);
        _selectedSegmentIndex = toIdx;
        SyncTrimFromSegments();
        _playheadVisualSec = Math.Clamp(_playheadVisualSec, 0, ComputeEditedDuration());
        UpdateSelectionUI();
        RedrawCanvas();
    }

    // ── Tool toggle handlers ──────────────────────────────────────────────────

    private void SelectToolButton_Checked(object sender, RoutedEventArgs e) => ActivateSelectTool();
    private void CutToolButton_Checked(object sender, RoutedEventArgs e)    => ActivateCutTool();

    private void ActivateSelectTool()
    {
        _tool               = EditorTool.Select;
        _cutHoverVisualSec  = null;
        _cutHoverIsSnapping = false;
        // Guard: BAML wires Checked before IsChecked="True" is set, so this can fire
        // while CutToolButton and WaveformCanvas are still null during loading.
        if (SelectToolButton is not null) SelectToolButton.IsChecked = true;
        if (CutToolButton    is not null) CutToolButton.IsChecked    = false;
        if (WaveformCanvas   is not null) WaveformCanvas.Cursor      = Cursors.Arrow;
        UpdateSelectionUI();
        RedrawCanvas();
    }

    private void ActivateCutTool()
    {
        _tool                 = EditorTool.Cut;
        _selectedSegmentIndex = -1;
        _pendingReorder       = false;
        if (_drag != DragTarget.None)
        {
            _drag = DragTarget.None;
            WaveformCanvas?.ReleaseMouseCapture();
        }
        if (CutToolButton    is not null) CutToolButton.IsChecked    = true;
        if (SelectToolButton is not null) SelectToolButton.IsChecked = false;
        if (WaveformCanvas   is not null) WaveformCanvas.Cursor      = Cursors.Cross;
        UpdateSelectionUI();
        RedrawCanvas();
    }

    // ── Block deletion handlers ───────────────────────────────────────────────

    private void DeleteCutButton_Click(object sender, RoutedEventArgs e) => DeleteSelectedSegment();

    private void Waveform_MouseRightDown(object sender, MouseButtonEventArgs e)
    {
        double x         = e.GetPosition(WaveformCanvas).X;
        double w         = WaveformCanvas.ActualWidth;
        double editedDur = ComputeEditedDuration();
        double vSec      = XToVisual(x, w, editedDur);

        var blocks = BuildVisualBlocks();
        int hit    = FindBlockByVisualTime(blocks, vSec);
        if (hit < 0 || _segments.Count <= 1) return;

        RemoveSegmentAt(blocks[hit].SegmentIndex);
        e.Handled = true;
    }

    // ── Keyboard shortcuts ────────────────────────────────────────────────────

    private void Window_PreviewKeyDown(object sender, KeyEventArgs e)
    {
        bool textFocused = Keyboard.FocusedElement is System.Windows.Controls.TextBox
                        or Wpf.Ui.Controls.TextBox
                        or ComboBox;
        if (textFocused) return;

        switch (e.Key)
        {
            case Key.A:
                ActivateSelectTool();
                e.Handled = true;
                break;
            case Key.C when (Keyboard.Modifiers & ModifierKeys.Control) != 0:
                CopySelectedBlock();
                e.Handled = true;
                break;
            case Key.C:
                ActivateCutTool();
                e.Handled = true;
                break;
            case Key.V when (Keyboard.Modifiers & ModifierKeys.Control) != 0:
                PasteBlock();
                e.Handled = true;
                break;
            case Key.Delete:
                DeleteSelectedSegment();
                e.Handled = true;
                break;
            case Key.Space:
                Preview_Click(this, e);
                e.Handled = true;
                break;
            case Key.Z when (Keyboard.Modifiers & ModifierKeys.Control) != 0
                         && (Keyboard.Modifiers & ModifierKeys.Shift) != 0:
                Redo();
                e.Handled = true;
                break;
            case Key.Z when (Keyboard.Modifiers & ModifierKeys.Control) != 0:
                Undo();
                e.Handled = true;
                break;
            case Key.Y when (Keyboard.Modifiers & ModifierKeys.Control) != 0:
                Redo();
                e.Handled = true;
                break;
        }
    }

    // ── Editor segment computation for preview playback ────────────────────────
    // Converts the current visual playhead position into (S, E) sample pairs
    // matching the audio segments the preview engine will actually play.

    private List<(int S, int E)> ComputeEditorSegments(double startVisualSec)
    {
        int sr     = _sound.SampleRate * _sound.Channels;
        var blocks = BuildVisualBlocks();
        var result = new List<(int S, int E)>();

        foreach (var blk in blocks)
        {
            if (startVisualSec >= blk.VisualEnd) continue; // block is before playhead

            int s = Math.Clamp((int)(blk.SourceStart * sr), 0, _sound.TotalSamples);
            int e = Math.Clamp((int)(blk.SourceEnd   * sr), 0, _sound.TotalSamples);
            if (s >= e) continue;

            if (startVisualSec > blk.VisualStart)
            {
                // Playhead sits inside this block; skip the already-played portion.
                double srcOffset = startVisualSec - blk.VisualStart;
                s = Math.Clamp(s + (int)(srcOffset * sr), s, e);
            }

            if (s < e) result.Add((s, e));
        }

        if (result.Count == 0)
        {
            // Fallback: play from beginning of first segment.
            int s = Math.Clamp((int)(_segments[0].StartSeconds   * sr), 0, _sound.TotalSamples);
            int e = Math.Clamp((int)(_segments[^1].EndSeconds * sr), s, _sound.TotalSamples);
            if (s < e) result.Add((s, e));
        }

        return result;
    }

    // ── Hotkey ────────────────────────────────────────────────────────────────

    private void RefreshHotkeyDisplay()
    {
        if (_pendingHotkey is not null)
        {
            HotkeyDisplayText.Text       = _pendingHotkey.DisplayText;
            HotkeyDisplayText.Foreground =
                (Brush)Application.Current.Resources["SystemAccentColorPrimaryBrush"];
            ClearHotkeyButton.IsEnabled  = true;
        }
        else
        {
            HotkeyDisplayText.Text       = "No hotkey assigned";
            HotkeyDisplayText.Foreground =
                (Brush)Application.Current.Resources["TextFillColorTertiaryBrush"];
            ClearHotkeyButton.IsEnabled  = false;
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

    private async void Preview_Click(object sender, RoutedEventArgs e)
    {
        if (_previewEngine is null) return;

        if (_previewHandle is not null && !_previewHandle.IsFinished)
        {
            StopPreview();
            return;
        }

        StopPreview();

        double currentSpeed  = SpeedSlider?.Value ?? 1.0;
        bool   dialogEffects = ReverseCheckBox?.IsChecked  == true
                            || NormalizeCheckBox?.IsChecked == true
                            || Math.Abs(currentSpeed - 1.0) > 0.001;

        if (dialogEffects)
        {
            await PreviewWithEffectsAsync(currentSpeed);
            return;
        }

        double editedDur      = ComputeEditedDuration();
        double startVisualSec = (_playheadVisualSec > 0 && _playheadVisualSec < editedDur)
            ? _playheadVisualSec : 0;

        float uiVol  = (float)(VolumeSlider.Value / 100.0);
        float gain   = uiVol * uiVol;
        int   sr     = _sound.SampleRate * _sound.Channels;
        int   fadeInS  = (_vFadeIn  > 0 && startVisualSec <= 0) ? (int)(_vFadeIn  * sr) : 0;
        int   fadeOutS = _vFadeOut  > 0                         ? (int)(_vFadeOut  * sr) : 0;

        var  segments  = ComputeEditorSegments(startVisualSec);
        if (segments.Count == 0) return;

        bool useSimple = segments.Count == 1 && fadeInS == 0 && fadeOutS == 0
            && segments[0].S == 0 && segments[0].E == _sound.TotalSamples;

        _previewHandle = useSimple
            ? _previewEngine.Play(_sound, gain)
            : _previewEngine.Play(_sound, gain, segments, fadeInS, fadeOutS);

        _playStartVisualSec = startVisualSec;
        _playStartTime      = DateTime.Now;
        _playheadVisualSec  = startVisualSec;

        _playTimer       = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(40) };
        _playTimer.Tick += (_, _) => TickPlayhead();
        _playTimer.Start();

        PreviewButton.Content  = "Stop Preview";
        PreviewStatusText.Text = "Playing…";
    }

    private async Task PreviewWithEffectsAsync(double currentSpeed)
    {
        if (_previewEngine is null) return;

        PreviewButton.IsEnabled = false;
        PreviewStatusText.Text  = "Rendering effects…";

        bool   rev   = ReverseCheckBox?.IsChecked  == true;
        bool   norm  = NormalizeCheckBox?.IsChecked == true;
        double speed = Math.Clamp(currentSpeed, 0.5, 2.0);
        var    segs  = ComputeEditorSegments(0);

        float[] rendered;
        try
        {
            rendered = await Task.Run(() =>
                EffectProcessor.Render(_sound, segs, rev, norm, speed));
        }
        catch
        {
            PreviewStatusText.Text  = "Effect render failed.";
            PreviewButton.IsEnabled = true;
            return;
        }

        if (rendered.Length == 0)
        {
            PreviewStatusText.Text  = "Nothing to preview.";
            PreviewButton.IsEnabled = true;
            return;
        }

        var previewSound = new CachedSound(rendered);
        float uiVol  = (float)(VolumeSlider.Value / 100.0);
        float gain   = uiVol * uiVol;
        int   sr     = previewSound.SampleRate * previewSound.Channels;
        int   fadeInS  = _vFadeIn  > 0 ? (int)(_vFadeIn  * sr) : 0;
        int   fadeOutS = _vFadeOut > 0 ? (int)(_vFadeOut * sr) : 0;
        var   fullSegs = new List<(int S, int E)> { (0, previewSound.TotalSamples) };

        _previewHandle      = _previewEngine.Play(previewSound, gain, fullSegs, fadeInS, fadeOutS);
        _playStartVisualSec = 0;
        _playStartTime      = DateTime.Now;
        _playheadVisualSec  = 0;

        _playTimer       = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(40) };
        _playTimer.Tick += (_, _) => TickPlayhead();
        _playTimer.Start();

        PreviewButton.IsEnabled = true;
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

        _playheadVisualSec = _playStartVisualSec + (DateTime.Now - _playStartTime).TotalSeconds;
        double editedDur   = ComputeEditedDuration();

        if (_playheadVisualSec >= editedDur)
        {
            _playheadVisualSec = editedDur;
            RedrawCanvas();
            StopPreview();
            return;
        }

        if (WaveformCanvas.ActualWidth > 0)
        {
            RedrawCanvas();
            if (_zoomFactor > 1) ScrollToShowPlayhead();
        }
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

        if (PreviewButton     is not null) PreviewButton.Content  = "Play Preview";
        if (PreviewStatusText is not null) PreviewStatusText.Text = "";
    }

    // ── Copy / Paste ──────────────────────────────────────────────────────────

    private void CopySelectedBlock()
    {
        if (_selectedSegmentIndex < 0 || _selectedSegmentIndex >= _segments.Count) return;
        _copiedSegment = _segments[_selectedSegmentIndex];
        UpdateSelectionUI();
    }

    private void PasteBlock()
    {
        if (_copiedSegment is null) return;
        PushUndo();
        int insertAfter = _selectedSegmentIndex >= 0 && _selectedSegmentIndex < _segments.Count
            ? _selectedSegmentIndex
            : _segments.Count - 1;
        _segments.Insert(insertAfter + 1, _copiedSegment);
        _selectedSegmentIndex = insertAfter + 1;
        SyncTrimFromSegments();
        UpdateSelectionUI();
        RedrawCanvas();
    }

    private void CopyButton_Click(object  sender, RoutedEventArgs e) => CopySelectedBlock();
    private void PasteButton_Click(object sender, RoutedEventArgs e) => PasteBlock();

    // ── Scroll to keep playhead visible ──────────────────────────────────────

    private void ScrollToShowPlayhead()
    {
        if (WaveformScrollViewer is null || WaveformCanvas is null) return;
        double editedDur = ComputeEditedDuration();
        if (editedDur <= 0) return;
        double w     = WaveformCanvas.ActualWidth;
        double phX   = VisualToX(_playheadVisualSec, w, editedDur);
        double viewW = WaveformScrollViewer.ViewportWidth;
        WaveformScrollViewer.ScrollToHorizontalOffset(Math.Max(0, phX - viewW / 2));
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

        // Trim boundary validation only applies to the single-block path.
        // For multi-block saves, ResultSegments is the source of truth and
        // TrimStart/TrimEnd text-box values are irrelevant (set to null on save).
        if (_segments.Count <= 1)
        {
            double effectiveEnd = trimEnd ?? _duration;
            if (trimStart.HasValue && trimStart.Value >= effectiveEnd)
            {
                TrimErrorText.Text       = "Trim Start must be less than Trim End.";
                TrimErrorText.Visibility = Visibility.Visible;
                return;
            }

            if (trimEnd.HasValue && trimEnd.Value > _duration)
            {
                TrimErrorText.Text       = $"Trim End cannot exceed the sound duration ({_duration:F2}s).";
                TrimErrorText.Visibility = Visibility.Visible;
                return;
            }
        }

        ResultName     = name;
        ResultCategory = string.IsNullOrWhiteSpace(CategoryBox.Text)
            ? "General" : CategoryBox.Text.Trim();
        var parsedTags = TagsBox.Text
            .Split(',')
            .Select(t => t.Trim())
            .Where(t => !string.IsNullOrEmpty(t))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
        ResultTags   = parsedTags.Count > 0 ? parsedTags : null;
        ResultVolume = (float)(VolumeSlider.Value / 100.0);
        ResultFadeIn   = fadeIn;
        ResultFadeOut  = fadeOut;
        ResultHotkey   = _pendingHotkey;

        // Single block → store as TrimStart/TrimEnd for backward compatibility.
        // Multiple blocks → store explicit Segments list; TrimStart/TrimEnd are
        // encoded in the first/last segment boundaries and set to null.
        if (_segments.Count <= 1)
        {
            ResultTrimStart = trimStart;
            ResultTrimEnd   = trimEnd;
            ResultSegments  = null;
        }
        else
        {
            ResultTrimStart = null;
            ResultTrimEnd   = null;
            ResultSegments  = new List<AudioSegment>(_segments);
        }

        ResultReverseAudio   = ReverseCheckBox.IsChecked  == true;
        ResultNormalizeAudio = NormalizeCheckBox.IsChecked == true;
        ResultPlaybackSpeed  = Math.Clamp(SpeedSlider.Value, 0.5, 2.0);

        DialogResult = true;
    }

    private void Cancel_Click(object sender, RoutedEventArgs e) => DialogResult = false;

    private void SpeedSlider_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        if (SpeedValueText is not null)
            SpeedValueText.Text = $"{e.NewValue:F2}×";
    }

    private void ResetEffects_Click(object sender, RoutedEventArgs e)
    {
        ReverseCheckBox.IsChecked   = false;
        NormalizeCheckBox.IsChecked = false;
        SpeedSlider.Value           = 1.0;
    }

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
