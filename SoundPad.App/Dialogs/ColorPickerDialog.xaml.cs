using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;

namespace SoundPad.App.Dialogs;

public partial class ColorPickerDialog : Wpf.Ui.Controls.FluentWindow
{
    
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

    
    
    
    public string? ResultColor { get; private set; }

    
    private bool    _syncing;
    private string? _selectedPreset;        
    private bool    _customIsValid = false; 
    private string? _customHex;             

    
    private readonly Button[] _presetButtons;

    
    public ColorPickerDialog(Window owner, string? currentColor)
    {
        InitializeComponent();
        Owner = owner;

        _presetButtons = new Button[Presets.Length];
        BuildPresets();
        InitializeFrom(currentColor);
    }

    
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
                CornerRadius    = new CornerRadius(6),
                Background      = fill,
                BorderBrush     = cardBorder,
                BorderThickness = new Thickness(2),
            };

            
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

            
            var ring = new Border
            {
                Width           = 40,
                Height          = 40,
                CornerRadius    = new CornerRadius(8),
                BorderThickness = new Thickness(3),
                BorderBrush     = Brushes.Transparent,
                Margin          = new Thickness(4, 0, 4, 8),
                Child           = new Border
                {
                    Width        = 28,
                    Height       = 28,
                    CornerRadius = new CornerRadius(6),
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
            System.Windows.Automation.AutomationProperties.SetName(btn, label);
            btn.Click += (_, _) => SelectPreset(captured);

            _presetButtons[i] = btn;
            PresetsPanel.Children.Add(btn);
        }
    }

    
    private void InitializeFrom(string? currentColor)
    {
        if (currentColor is null)
        {
            
            _selectedPreset  = null;
            _customIsValid   = false;
            ApplyButton.IsEnabled = true;
            UpdateSwatchForDefault();
            HighlightPreset(null);
            return;
        }

        
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
            
            _selectedPreset = null;
            HighlightPreset(null, showRing: false);
            LoadHexIntoControls(currentColor);
        }

        ApplyButton.IsEnabled = true;
    }

    
    
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

    
    private void SelectPreset(int index)
    {
        var (hex, _) = Presets[index];
        _selectedPreset = hex;
        HighlightPreset(hex);

        if (hex is null)
        {
            
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

    
    
    private void HighlightPreset(string? activeHex, bool showRing = true)
    {
        var accentBrush = (Brush)Application.Current.Resources["ComicCyanBrush"];

        for (int i = 0; i < Presets.Length; i++)
        {
            if (_presetButtons[i].Content is not Border ring) continue;

            bool isActive = showRing && string.Equals(Presets[i].Hex, activeHex,
                                StringComparison.OrdinalIgnoreCase)
                         || (showRing && activeHex is null && Presets[i].Hex is null);

            ring.BorderBrush = isActive ? accentBrush : Brushes.Transparent;
        }
    }

    
    private void UpdateSwatchForColor(Color c)
    {
        PreviewSwatch.Background = new SolidColorBrush(c) { Opacity = 0.75 };
    }

    private void UpdateSwatchForDefault()
    {
        PreviewSwatch.Background =
            (Brush)Application.Current.Resources["CardBackgroundFillColorDefaultBrush"];
    }

    
    private void HexBox_TextChanged(object sender, TextChangedEventArgs e)
    {
        if (_syncing) return;

        var raw = HexBox.Text.Trim();

        if (string.IsNullOrEmpty(raw))
        {
            
            _customIsValid = false;
            _customHex     = null;
            HighlightPreset(null, showRing: false);
            ErrorText.Visibility  = Visibility.Collapsed;
            ApplyButton.IsEnabled = _selectedPreset is null; 
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

    
    private void RgbBox_TextChanged(object sender, TextChangedEventArgs e)
    {
        if (_syncing) return;
        SyncFromRgb();
    }

    
    private void SyncFromRgb()
    {
        if (!TryParseChannel(RedBox.Text,   out int r) ||
            !TryParseChannel(GreenBox.Text, out int g) ||
            !TryParseChannel(BlueBox.Text,  out int b))
        {
            
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

    
    private void Apply_Click(object sender, RoutedEventArgs e)
    {
        if (_selectedPreset is null && !_customIsValid)
        {
            
            
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
