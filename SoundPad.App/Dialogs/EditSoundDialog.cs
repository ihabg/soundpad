using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;

namespace SoundPad.App.Dialogs;

// Simple modal dialog for renaming a sound and changing its category.
// Built entirely in code (no XAML) so it lives in a single file.
public class EditSoundDialog : Window
{
    private readonly TextBox  _nameBox;
    private readonly ComboBox _categoryBox;

    // Set by Accept() when the user clicks Save.
    public string ResultName     { get; private set; } = "";
    public string ResultCategory { get; private set; } = "General";

    public EditSoundDialog(Window owner, string currentName, string currentCategory,
                           IEnumerable<string> existingCategories)
    {
        Owner                 = owner;
        Title                 = "Edit Sound";
        Width                 = 360;
        Height                = 230;
        ResizeMode            = ResizeMode.NoResize;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        Background            = new SolidColorBrush(Color.FromRgb(0x24, 0x24, 0x24));

        var outer = new StackPanel { Margin = new Thickness(20) };

        // ── Name ────────────────────────────────────────────────────────────
        outer.Children.Add(MakeLabel("Name"));
        _nameBox = new TextBox
        {
            Text    = currentName,
            Height  = 30,
            Padding = new Thickness(8, 4, 8, 4),
            Margin  = new Thickness(0, 4, 0, 14),
            Background   = new SolidColorBrush(Color.FromRgb(0x33, 0x33, 0x33)),
            Foreground   = new SolidColorBrush(Colors.White),
            BorderBrush  = new SolidColorBrush(Color.FromRgb(0x55, 0x55, 0x55)),
            CaretBrush   = new SolidColorBrush(Colors.White)
        };
        _nameBox.KeyDown += (_, e) => { if (e.Key == Key.Enter) Accept(); };
        outer.Children.Add(_nameBox);

        // ── Category ─────────────────────────────────────────────────────────
        outer.Children.Add(MakeLabel("Category"));
        _categoryBox = new ComboBox
        {
            IsEditable  = true,
            Height      = 30,
            Margin      = new Thickness(0, 4, 0, 20),
            Background  = new SolidColorBrush(Color.FromRgb(0x33, 0x33, 0x33)),
            Foreground  = new SolidColorBrush(Colors.White),
            BorderBrush = new SolidColorBrush(Color.FromRgb(0x55, 0x55, 0x55))
        };

        // Seed existing categories; "General" is always first.
        _categoryBox.Items.Add("General");
        foreach (var cat in existingCategories.Where(c => c != "General").OrderBy(c => c))
            _categoryBox.Items.Add(cat);

        _categoryBox.Text = string.IsNullOrWhiteSpace(currentCategory) ? "General" : currentCategory;
        outer.Children.Add(_categoryBox);

        // ── Buttons ───────────────────────────────────────────────────────────
        var btnRow = new StackPanel
        {
            Orientation         = Orientation.Horizontal,
            HorizontalAlignment = HorizontalAlignment.Right
        };

        var saveBtn = MakeDialogButton("Save", "#2A5C2A", "#3A7A3A");
        saveBtn.Width  = 88;
        saveBtn.Margin = new Thickness(0, 0, 8, 0);
        saveBtn.Click += (_, _) => Accept();

        var cancelBtn = MakeDialogButton("Cancel", "#3C3C3C", "#555555");
        cancelBtn.Width = 88;
        cancelBtn.Click += (_, _) => { DialogResult = false; };

        btnRow.Children.Add(saveBtn);
        btnRow.Children.Add(cancelBtn);
        outer.Children.Add(btnRow);

        Content = outer;

        // Select all text so the user can start typing immediately.
        Loaded += (_, _) => { _nameBox.SelectAll(); _nameBox.Focus(); };
    }

    private void Accept()
    {
        var name = _nameBox.Text.Trim();
        if (string.IsNullOrWhiteSpace(name))
        {
            MessageBox.Show("Name cannot be empty.", "Edit Sound",
                            MessageBoxButton.OK, MessageBoxImage.Warning);
            _nameBox.Focus();
            return;
        }

        ResultName     = name;
        ResultCategory = string.IsNullOrWhiteSpace(_categoryBox.Text) ? "General"
                                                                       : _categoryBox.Text.Trim();
        DialogResult   = true;
    }

    private static TextBlock MakeLabel(string text) => new TextBlock
    {
        Text       = text,
        Foreground = new SolidColorBrush(Color.FromRgb(0xBB, 0xBB, 0xBB)),
        FontSize   = 12
    };

    private static Button MakeDialogButton(string label, string bgHex, string borderHex)
    {
        var bg     = (Color)ColorConverter.ConvertFromString(bgHex);
        var border = (Color)ColorConverter.ConvertFromString(borderHex);
        return new Button
        {
            Content     = label,
            Height      = 30,
            Foreground  = new SolidColorBrush(Colors.White),
            Background  = new SolidColorBrush(bg),
            BorderBrush = new SolidColorBrush(border),
            Cursor      = Cursors.Hand
        };
    }
}
