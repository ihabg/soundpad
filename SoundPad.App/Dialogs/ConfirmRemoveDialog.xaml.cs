using System.Windows;

namespace SoundPad.App.Dialogs;

// Fluent-styled confirmation dialog for removing a sound from the library.
// IsCancel="True" on the Cancel button means Escape and the title-bar X both
// close the dialog without removing anything (ShowDialog returns false/null).
public partial class ConfirmRemoveDialog : Wpf.Ui.Controls.FluentWindow
{
    public ConfirmRemoveDialog(Window owner, string soundName)
    {
        InitializeComponent();
        Owner = owner;
        SoundNameText.Text = $"\"{soundName}\"";
    }

    private void Remove_Click(object sender, RoutedEventArgs e) => DialogResult = true;
    private void Cancel_Click(object sender, RoutedEventArgs e) => DialogResult = false;
}
