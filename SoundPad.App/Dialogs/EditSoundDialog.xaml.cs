using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Input;

namespace SoundPad.App.Dialogs;

// Modal dialog for renaming a sound, changing its category, and viewing its hotkey.
public partial class EditSoundDialog : Wpf.Ui.Controls.FluentWindow
{
    public string ResultName     { get; private set; } = "";
    public string ResultCategory { get; private set; } = "General";

    public EditSoundDialog(Window owner, string currentName, string currentCategory,
                            IEnumerable<string> existingCategories, string hotkeyDisplay)
    {
        InitializeComponent();
        Owner = owner;

        NameBox.Text = currentName;

        CategoryBox.Items.Add("General");
        foreach (var cat in existingCategories.Where(c => c != "General").OrderBy(c => c))
            CategoryBox.Items.Add(cat);
        CategoryBox.Text = string.IsNullOrWhiteSpace(currentCategory) ? "General" : currentCategory;

        HotkeyDisplayText.Text = string.IsNullOrWhiteSpace(hotkeyDisplay) ? "No hotkey assigned" : hotkeyDisplay;

        NameBox.KeyDown += (_, e) => { if (e.Key == Key.Enter) Accept(); };
        Loaded += (_, _) => { NameBox.SelectAll(); NameBox.Focus(); };
    }

    private void Save_Click(object sender, RoutedEventArgs e) => Accept();

    private void Cancel_Click(object sender, RoutedEventArgs e) => DialogResult = false;

    private void Accept()
    {
        var name = NameBox.Text.Trim();
        if (string.IsNullOrWhiteSpace(name))
        {
            MessageBox.Show("Name cannot be empty.", "Edit Sound",
                            MessageBoxButton.OK, MessageBoxImage.Warning);
            NameBox.Focus();
            return;
        }

        ResultName     = name;
        ResultCategory = string.IsNullOrWhiteSpace(CategoryBox.Text) ? "General" : CategoryBox.Text.Trim();
        DialogResult   = true;
    }
}
