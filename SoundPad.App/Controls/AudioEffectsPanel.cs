using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using SoundPad.App.Audio;

namespace SoundPad.App.Controls;

public sealed class EffectValueChangedEventArgs(string id, double oldValue, double newValue) : EventArgs
{
    public string Id { get; } = id;
    public double OldValue { get; } = oldValue;
    public double NewValue { get; } = newValue;
}

public sealed class AudioEffectsPanel : UserControl
{
    private readonly Dictionary<string, FrameworkElement> _controls = new();
    private readonly Dictionary<string, TextBlock> _labels = new();
    private readonly Dictionary<string, FrameworkElement> _sections = new();
    private readonly ComboBox _effectSelector = new() { MinHeight = 34, MaxDropDownHeight = 260, Margin = new Thickness(0, 0, 0, 10) };
    private readonly TextBlock _activeSummary = new() { FontSize = 11, TextWrapping = TextWrapping.Wrap, Opacity = .75, Margin = new Thickness(0, 8, 0, 0) };
    private Dictionary<string, double> _values = new();
    private bool _updating;
    public event EventHandler<EffectValueChangedEventArgs>? Changed;
    public event EventHandler? GestureCompleted;
    public bool IsInGesture { get; private set; }
    public Dictionary<string, double> Values => new(_values);
    public FrameworkElement GetEditor(string id) => _controls[id];
    public Slider GetSlider(string id) => (Slider)_controls[id];
    public CheckBox GetToggle(string id) => (CheckBox)_controls[id];
    public TextBlock GetValueLabel(string id) => _labels[id];

    public AudioEffectsPanel()
    {
        var layout = new StackPanel();
        var root = new StackPanel();
        var header = new DockPanel { Margin = new Thickness(0, 0, 0, 10) };
        var reset = new Wpf.Ui.Controls.Button { Content = "Reset", Appearance = Wpf.Ui.Controls.ControlAppearance.Secondary, FontSize = 11, Padding = new Thickness(9, 4, 9, 4) };
        DockPanel.SetDock(reset, Dock.Right); header.Children.Add(reset);
        var title = new TextBlock { Text = "EFFECTS", FontFamily = new System.Windows.Media.FontFamily("Impact"), FontSize = 21, VerticalAlignment = VerticalAlignment.Center };
        title.SetResourceReference(TextBlock.ForegroundProperty, "ComicCyanBrush");
        header.Children.Add(title); root.Children.Add(header);
        _effectSelector.ToolTip = "Choose an effect to adjust. Other enabled effects stay active.";
        root.Children.Add(_effectSelector);
        foreach (var effect in AudioEffectCatalog.Definitions)
        {
            var section = new StackPanel { Visibility = effect.IsPlaybackControl ? Visibility.Visible : Visibility.Collapsed };
            if (effect.IsPlaybackControl)
            {
                section.Margin = new Thickness(0, 0, 0, 16);
                var playbackHeader = new DockPanel { Margin = new Thickness(0, 0, 0, 8) };
                var playbackReset = new Wpf.Ui.Controls.Button { Content = "Reset", Appearance = Wpf.Ui.Controls.ControlAppearance.Secondary, FontSize = 11, Padding = new Thickness(9, 4, 9, 4) };
                playbackReset.Click += (_, _) => ResetDefinitions(d => d.Id == effect.Id);
                DockPanel.SetDock(playbackReset, Dock.Right); playbackHeader.Children.Add(playbackReset);
                var playbackTitle = new TextBlock { Text = effect.Name.ToUpperInvariant(), FontFamily = new System.Windows.Media.FontFamily("Impact"), FontSize = 21, VerticalAlignment = VerticalAlignment.Center };
                playbackTitle.SetResourceReference(TextBlock.ForegroundProperty, "ComicCyanBrush");
                playbackHeader.Children.Add(playbackTitle); section.Children.Add(playbackHeader);
                layout.Children.Add(section);
            }
            else
            {
                _sections[effect.Id] = section;
                root.Children.Add(section);
                _effectSelector.Items.Add(new ComboBoxItem { Content = effect.Name, Tag = effect.Id });
            }
            _values[effect.Id] = effect.DefaultValue;
            if (effect.IsToggle)
            {
                var toggle = new CheckBox { Content = effect.Name, FontSize = 12, Margin = new Thickness(0, 0, 0, 10), ToolTip = effect.Hint };
                toggle.Checked += (_, _) => Update(effect, 1); toggle.Unchecked += (_, _) => Update(effect, 0);
                _controls[effect.Id] = toggle; section.Children.Add(toggle);
            }
            else
            {
                var row = new DockPanel { Margin = new Thickness(0, 3, 0, 5) };
                var label = new TextBlock { Width = effect.ValueFormat is null ? 48 : 64, FontSize = 12, TextAlignment = TextAlignment.Right, VerticalAlignment = VerticalAlignment.Center };
                DockPanel.SetDock(label, Dock.Right); row.Children.Add(label); _labels[effect.Id] = label;
                var slider = new Slider { Minimum = effect.Minimum, Maximum = effect.Maximum, Value = effect.DefaultValue, VerticalAlignment = VerticalAlignment.Center, ToolTip = effect.Hint };
                slider.ValueChanged += (_, e) => Update(effect, e.NewValue);
                slider.AddHandler(Thumb.DragStartedEvent, new DragStartedEventHandler((_, _) => IsInGesture = true));
                slider.AddHandler(Thumb.DragCompletedEvent, new DragCompletedEventHandler((_, _) => EndGesture()));
                _controls[effect.Id] = slider; row.Children.Add(slider); section.Children.Add(row);
                section.Children.Add(new TextBlock { Text = effect.Hint, TextWrapping = TextWrapping.Wrap, FontSize = 10, Opacity = .65, Margin = new Thickness(0, 0, 0, 8) });
            }
        }
        _effectSelector.SelectionChanged += (_, _) =>
        {
            string? selected = (_effectSelector.SelectedItem as ComboBoxItem)?.Tag as string;
            foreach (var section in _sections)
                section.Value.Visibility = section.Key == selected ? Visibility.Visible : Visibility.Collapsed;
        };
        _effectSelector.SelectedIndex = 0;
        root.Children.Add(_activeSummary);
        layout.Children.Add(root);
        Content = layout;
        reset.Click += (_, _) => Reset();
        SetValues(new Dictionary<string, double>());
    }

    public void Reset()
        => ResetDefinitions(effect => !effect.IsPlaybackControl);

    private void ResetDefinitions(Func<AudioEffectDefinition, bool> include)
    {
        IsInGesture = true;
        try
        {
            foreach (var effect in AudioEffectCatalog.Definitions.Where(include))
            {
                if (_controls[effect.Id] is CheckBox toggle) toggle.IsChecked = effect.DefaultValue >= .5;
                else ((Slider)_controls[effect.Id]).Value = effect.DefaultValue;
            }
        }
        finally { EndGesture(); }
    }

    private void EndGesture()
    {
        IsInGesture = false;
        GestureCompleted?.Invoke(this, EventArgs.Empty);
    }

    public void SetValues(IReadOnlyDictionary<string, double> values)
    {
        _updating = true;
        try
        {
            _values = AudioEffectCatalog.Sanitize(values);
            foreach (var effect in AudioEffectCatalog.Definitions)
            {
                double value = _values[effect.Id];
                if (_controls[effect.Id] is CheckBox toggle) toggle.IsChecked = value >= .5;
                else { ((Slider)_controls[effect.Id]).Value = value; _labels[effect.Id].Text = Format(effect, value); }
            }
        }
        finally { _updating = false; }
        RefreshSummary();
    }

    private void Update(AudioEffectDefinition effect, double value)
    {
        if (_updating) return;
        double old = _values[effect.Id]; _values[effect.Id] = effect.Sanitize(value);
        if (_labels.TryGetValue(effect.Id, out var label)) label.Text = Format(effect, _values[effect.Id]);
        RefreshSummary();
        Changed?.Invoke(this, new EffectValueChangedEventArgs(effect.Id, old, _values[effect.Id]));
    }

    private static string Format(AudioEffectDefinition effect, double value) => effect.ValueFormat is not null
        ? value.ToString(effect.ValueFormat, System.Globalization.CultureInfo.InvariantCulture)
        : effect.Id == "speed" ? $"{value:F2}×" : $"{value:F2}";

    private void RefreshSummary()
    {
        var active = AudioEffectCatalog.Definitions.Where(effect => !effect.IsPlaybackControl && Math.Abs(_values.GetValueOrDefault(effect.Id, effect.DefaultValue) - effect.DefaultValue) > .001)
            .Select(effect => effect.IsToggle ? effect.Name : $"{effect.Name} {Format(effect, _values[effect.Id])}").ToList();
        _activeSummary.Text = active.Count == 0 ? "No effects enabled" : "Active: " + string.Join(" · ", active);
        foreach (ComboBoxItem item in _effectSelector.Items)
        {
            var effect = AudioEffectCatalog.Definitions.Single(e => e.Id == (string)item.Tag);
            bool enabled = Math.Abs(_values.GetValueOrDefault(effect.Id, effect.DefaultValue) - effect.DefaultValue) > .001;
            item.Content = enabled ? $"{effect.Name} ✓" : effect.Name;
        }
    }
}
