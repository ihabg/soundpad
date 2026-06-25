using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;

namespace SoundPad.App.Dialogs;

public partial class ColorPickerDialog : Wpf.Ui.Controls.FluentWindow
{
    // ── Presets ───────────────────────────────────────────────────────────────
    private static readonly (string? Hex, string Label)[] Presets =
    {
        (null,      "Default"),
        ("#E53935", "Red"),
        ("#F4511E", "Orange"),
        ("#F9AB00", "Yellow"),
        ("#0F9D58", "Green"),
        ("#039BE5", "Blue"),
        ("#7B1FA2", "Purple"),
        ("#D81B60", "Pink"),
        ("#546E7A", "Gray"),
    };

    // ── Result ────────────────────────────────────────────────────────────────
    // null  = Default (remove color)
    // string = valid uppercase #RRGGBB
    public string? ResultColor { get; private set; }

    // ── State ─────────────────────────────────────────────────────────────────
    private bool    _syncing;
    private string? _selectedPreset;        // tracks which preset hex is highlighted
    private bool    _customIsValid = false; // true when HEX/RGB inputs hold a valid color
    private string? _customHex;             // last valid custom hex, or null

    // Preset swatch buttons — kept to update the selection ring
    private readonly Button[] _presetButtons;

    // ── Constructor ───────────────────────────────────────────────────────────
    public ColorPickerDialog(Window owner, string? currentColor)
    {
        InitializeComponent();
        Owner = owner;

        _presetButtons = new Button[Presets.Length];
        BuildPresets();
        InitializeFrom(currentColor);
    }

    // ── Build preset swatch buttons ───────────────────────────────────────────
    private void BuildPresets()
    {
        var defaultCardBg   = (Brush)Application.Current.Resources["CardBackgroundFillColorDefaultBrush"];
        var cardBorder      = (Brush)Application.Current.Resources["CardBorderBrush"];

        for (int i = 0; i < Presets.Length; i++)
        {
            var (hex, label) = Presets[i];
            int captured = i;

            Brush fill;
            if (hex is not null)
            {
                var c = ParseHexToColor(hex)!.Value;
                fill = new SolidColorBrush(c);
            }
            else
            {
                fill = defaultCardBg;
            }

            var inner = new Border
            {
                Width           = 28,
                Height          = 28,
                CornerRadius    = new CornerRadius(14),
                Background      = fill,
                BorderBrush     = hex is null ? cardBorder : Brushes.Transparent,
                BorderThickness = new Thickness(hex is null ? 1 : 0),
            };

            // "Default" gets a diagonal slash to indicate no color
            if (hex is null)
            {
                var slash = new System.Windows.Shapes.Line
                {
                    X1              = 6,
                    Y1              = 6,
                    X2              = 22,
                    Y2              = 22,
                    Stroke          = (Brush)Application.Current.Resources["TextFillColorTertiaryBrush"],
                    StrokeThickness = 1.5,
                    IsHitTestVisible = false,
                };
                var canvas = new System.Windows.Controls.Canvas
                {
                    Width = 28, Height = 28, IsHitTestVisible = false
                };
                canvas.Children.Add(slash);
                inner.Child = canvas;
            }

            // Selection ring — outer border that becomes accent-colored when active
            var ring = new Border
            {
                Width           = 34,
                Height          = 34,
                CornerRadius    = new CornerRadius(17),
                BorderThickness = new Thickness(2),
                BorderBrush     = Brushes.Transparent,
                Margin          = new Thickness(4, 0, 4, 8),
                Child           = new Border
                {
                    Width        = 28,
                    Height       = 28,
                    CornerRadius = new CornerRadius(14),
                    Child        = inner,
                    HorizontalAlignment = HorizontalAlignment.Center,
                    VerticalAlignment   = VerticalAlignment.Center,
                }
            };

            var btn = new Button
            {
                Content             = ring,
                Background          = Brushes.Transparent,
                BorderBrush         = Brushes.Transparent,
                BorderThickness     = new Thickness(0),
                Padding             = new Thickness(0),
                ToolTip             = label,
                Cursor              = Cursors.Hand,
                HorizontalAlignment = HorizontalAlignment.Left,
            };
            btn.Click += (_, _) => SelectPreset(captured);

            _presetButtons[i] = btn;
            PresetsPanel.Children.Add(btn);
        }
    }

    // ── Initialise from existing color ────────────────────────────────────────
    private void InitializeFrom(string? currentColor)
    {
        if (currentColor is null)
        {
            // No color — highlight Default
            _selectedPreset  = null;
            _customIsValid   = false;
            ApplyButton.IsEnabled = true;
            UpdateSwatchForDefault();
            HighlightPreset(null);
            return;
        }

        // Check whether the current color matches a preset
        var match = Array.FindIndex(Presets, p => p.Hex is not null &&
            string.Equals(p.Hex, currentColor, StringComparison.OrdinalIgnoreCase));

        if (match >= 0)
        {
            _selectedPreset = Presets[match].Hex;
            HighlightPreset(Presets[match].Hex);
            LoadHexIntoControls(Presets[match].Hex!);
        }
        else
        {
            // Custom color — no preset highlighted
            _selectedPreset = null;
            HighlightPreset(null, showRing: false);
            LoadHexIntoControls(currentColor);
        }

        ApplyButton.IsEnabled = true;
    }

    // Writes a valid hex into all controls (HEX box, RGB sliders/boxes, swatch)
    // without triggering sync loops.
    private void LoadHexIntoControls(string hex)
    {
        var color = ParseHexToColor(hex);
        if (color is null) return;

        _syncing = true;

        HexBox.Text   = hex.StartsWith('#') ? hex : "#" + hex;
        RedBox.Text   = color.Value.R.ToString();
        GreenBox.Text = color.Value.G.ToString();
        BlueBox.Text  = color.Value.B.ToString();
        RedSlider.Value   = color.Value.R;
        GreenSlider.Value = color.Value.G;
        BlueSlider.Value  = color.Value.B;

        _syncing       = false;
        _customIsValid = true;
        _customHex     = "#" + hex.TrimStart('#').ToUpperInvariant();

        UpdateSwatchForColor(color.Value);
        ErrorText.Visibility = Visibility.Collapsed;
    }

    // ── Preset selection ──────────────────────────────────────────────────────
    private void SelectPreset(int index)
    {
        var (hex, _) = Presets[index];
        _selectedPreset = hex;
        HighlightPreset(hex);

        if (hex is null)
        {
            // Default: clear custom inputs and show card default background
            _syncing = true;
            HexBox.Text   = "";
            RedBox.Text   = "";
            GreenBox.Text = "";
            BlueBox.Text  = "";
            RedSlider.Value   = 0;
            GreenSlider.Value = 0;
            BlueSlider.Value  = 0;
            _syncing = false;

            _customIsValid        = false;
            _customHex            = null;
            ErrorText.Visibility  = Visibility.Collapsed;
            ApplyButton.IsEnabled = true;
            UpdateSwatchForDefault();
        }
        else
        {
            LoadHexIntoControls(hex);
            ApplyButton.IsEnabled = true;
        }
    }

    // Updates the selection ring on each preset button.
    // showRing=false is used when no preset is active (custom color entered).
    private void HighlightPreset(string? activeHex, bool showRing = true)
    {
        var accentBrush = (Brush)Application.Current.Resources["SystemAccentColorPrimaryBrush"];

        for (int i = 0; i < Presets.Length; i++)
        {
            if (_presetButtons[i].Content is not Border ring) continue;

            bool isActive = showRing && string.Equals(Presets[i].Hex, activeHex,
                                StringComparison.OrdinalIgnoreCase)
                         || (showRing && activeHex is null && Presets[i].Hex is null);

            ring.BorderBrush = isActive ? accentBrush : Brushes.Transparent;
        }
    }

    // ── Swatch helpers ────────────────────────────────────────────────────────
    private void UpdateSwatchForColor(Color c)
    {
        PreviewSwatch.Background = new SolidColorBrush(c) { Opacity = 0.75 };
    }

    private void UpdateSwatchForDefault()
    {
        PreviewSwatch.Background =
            (Brush)Application.Current.Resources["CardBackgroundFillColorDefaultBrush"];
    }

    // ── HEX text changed ──────────────────────────────────────────────────────
    private void HexBox_TextChanged(object sender, TextChangedEventArgs e)
    {
        if (_syncing) return;

        var raw = HexBox.Text.Trim();

        if (string.IsNullOrEmpty(raw))
        {
            // User cleared the box — deselect presets, don't error
            _customIsValid = false;
            _customHex     = null;
            HighlightPreset(null, showRing: false);
            ErrorText.Visibility  = Visibility.Collapsed;
            ApplyButton.IsEnabled = _selectedPreset is null; // only Default stays valid
            UpdateSwatchForDefault();
            return;
        }

        var stripped = raw.TrimStart('#').ToUpperInvariant();
        if (stripped.Length == 6 && IsValidHex(stripped))
        {
            var color = ParseHexToColor("#" + stripped)!.Value;

            _syncing = true;
            RedBox.Text   = color.R.ToString();
            GreenBox.Text = color.G.ToString();
            BlueBox.Text  = color.B.ToString();
            RedSlider.Value   = color.R;
            GreenSlider.Value = color.G;
            BlueSlider.Value  = color.B;
            _syncing = false;

            _customIsValid        = true;
            _customHex            = "#" + stripped;
            ErrorText.Visibility  = Visibility.Collapsed;
            ApplyButton.IsEnabled = true;

            // Check whether it matches a preset; if so, highlight it
            var match = Array.FindIndex(Presets, p => p.Hex is not null &&
                string.Equals(p.Hex, _customHex, StringComparison.OrdinalIgnoreCase));
            _selectedPreset = match >= 0 ? Presets[match].Hex : null;
            HighlightPreset(_selectedPreset, showRing: match >= 0);

            UpdateSwatchForColor(color);
        }
        else
        {
            _customIsValid        = false;
            _customHex            = null;
            ErrorText.Text        = "Enter a valid 6-digit HEX color, e.g. #FFAA00";
            ErrorText.Visibility  = Visibility.Visible;
            ApplyButton.IsEnabled = false;
            HighlightPreset(null, showRing: false);
        }
    }

    // ── RGB slider changed ────────────────────────────────────────────────────
    private void RgbSlider_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        if (_syncing) return;
        _syncing = true;

        if (sender == RedSlider)   RedBox.Text   = ((int)RedSlider.Value).ToString();
        if (sender == GreenSlider) GreenBox.Text = ((int)GreenSlider.Value).ToString();
        if (sender == BlueSlider)  BlueBox.Text  = ((int)BlueSlider.Value).ToString();

        _syncing = false;
        SyncFromRgb();
    }

    // ── RGB text box changed ──────────────────────────────────────────────────
    private void RgbBox_TextChanged(object sender, TextChangedEventArgs e)
    {
        if (_syncing) return;
        SyncFromRgb();
    }

    // Reads R/G/B text boxes; if all valid updates HEX + swatch.
    private void SyncFromRgb()
    {
        if (!TryParseChannel(RedBox.Text,   out int r) ||
            !TryParseChannel(GreenBox.Text, out int g) ||
            !TryParseChannel(BlueBox.Text,  out int b))
        {
            // Only show error if at least one box has content
            bool anyContent = !string.IsNullOrEmpty(RedBox.Text)
                           || !string.IsNullOrEmpty(GreenBox.Text)
                           || !string.IsNullOrEmpty(BlueBox.Text);
            if (anyContent)
            {
                ErrorText.Text        = "R, G, and B must each be a number from 0 to 255";
                ErrorText.Visibility  = Visibility.Visible;
                ApplyButton.IsEnabled = false;
            }
            else
            {
                ErrorText.Visibility  = Visibility.Collapsed;
                ApplyButton.IsEnabled = _selectedPreset is null;
            }
            _customIsValid = false;
            _customHex     = null;
            return;
        }

        // All valid — update slider positions without triggering ValueChanged loop
        _syncing = true;
        RedSlider.Value   = r;
        GreenSlider.Value = g;
        BlueSlider.Value  = b;
        _syncing = false;

        string hex = $"#{r:X2}{g:X2}{b:X2}";

        _syncing    = true;
        HexBox.Text = hex;
        _syncing    = false;

        _customIsValid        = true;
        _customHex            = hex;
        ErrorText.Visibility  = Visibility.Collapsed;
        ApplyButton.IsEnabled = true;

        var color = Color.FromRgb((byte)r, (byte)g, (byte)b);
        var match = Array.FindIndex(Presets, p => p.Hex is not null &&
            string.Equals(p.Hex, hex, StringComparison.OrdinalIgnoreCase));
        _selectedPreset = match >= 0 ? Presets[match].Hex : null;
        HighlightPreset(_selectedPreset, showRing: match >= 0);
        UpdateSwatchForColor(color);
    }

    // ── Parsing helpers ───────────────────────────────────────────────────────
    private static Color? ParseHexToColor(string hex)
    {
        var stripped = hex.TrimStart('#');
        if (stripped.Length != 6) return null;
        if (!int.TryParse(stripped, NumberStyles.HexNumber, null, out int rgb)) return null;
        byte r = (byte)((rgb >> 16) & 0xFF);
        byte g = (byte)((rgb >> 8)  & 0xFF);
        byte b = (byte)( rgb        & 0xFF);
        return Color.FromRgb(r, g, b);
    }

    private static bool IsValidHex(string s)
        => s.Length == 6 && int.TryParse(s, NumberStyles.HexNumber, null, out _);

    private static bool TryParseChannel(string text, out int value)
    {
        value = 0;
        if (string.IsNullOrWhiteSpace(text)) return false;
        return int.TryParse(text.Trim(), out value) && value >= 0 && value <= 255;
    }

    // ── Apply / Cancel ────────────────────────────────────────────────────────
    private void Apply_Click(object sender, RoutedEventArgs e)
    {
        if (_selectedPreset is null && !_customIsValid)
        {
            // Only reachable if Apply is somehow enabled with no valid color.
            // Guard: treat as Default.
            ResultColor  = null;
            DialogResult = true;
            return;
        }

        ResultColor  = _customIsValid ? _customHex : null;
        DialogResult = true;
    }

    private void Cancel_Click(object sender, RoutedEventArgs e) => DialogResult = false;

    private void Window_PreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Escape) { DialogResult = false; e.Handled = true; }
        if (e.Key == Key.Enter && ApplyButton.IsEnabled) { Apply_Click(sender, e); e.Handled = true; }
    }
}
