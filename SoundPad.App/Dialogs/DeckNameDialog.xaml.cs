using System.Windows;
using System.Windows.Input;

namespace SoundPad.App.Dialogs;

public partial class DeckNameDialog : Wpf.Ui.Controls.FluentWindow
{
    public string ResultName { get; private set; } = "";

    private readonly IReadOnlyList<string> _existingNames;

    public DeckNameDialog(Window owner, string initialName, IReadOnlyList<string> existingNames)
    {
        _existingNames = existingNames;
        Owner          = owner;
        InitializeComponent();

        DialogTitleBar.Title  = string.IsNullOrEmpty(initialName) ? "New Deck" : "Rename Deck";
        InstructionText.Text  = string.IsNullOrEmpty(initialName)
            ? "Enter a name for the new deck:"
            : "Enter a new name for this deck:";

        NameBox.Text = initialName;
        Loaded += (_, _) => { NameBox.Focus(); NameBox.SelectAll(); };
    }

    private void Ok_Click(object sender, RoutedEventArgs e) => TryConfirm();

    private void Cancel_Click(object sender, RoutedEventArgs e)
    {
        DialogResult = false;
        Close();
    }

    private void NameBox_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter)  { TryConfirm(); e.Handled = true; }
        if (e.Key == Key.Escape) { DialogResult = false; Close(); }
    }

    private void TryConfirm()
    {
        ErrorText.Visibility = Visibility.Collapsed;

        var name = NameBox.Text.Trim();
        if (string.IsNullOrEmpty(name))
        {
            ShowError("Please enter a name.");
            return;
        }

        if (_existingNames.Any(n => string.Equals(n, name, StringComparison.OrdinalIgnoreCase)))
        {
            ShowError($"A deck named \"{name}\" already exists.");
            return;
        }

        ResultName   = name;
        DialogResult = true;
        Close();
    }

    private void ShowError(string msg)
    {
        ErrorText.Text       = msg;
        ErrorText.Visibility = Visibility.Visible;
    }
}
