using System.Windows;

namespace SoundPad.App.Dialogs;

public partial class ConfirmDeleteRecordingDialog : Wpf.Ui.Controls.FluentWindow
{
    public ConfirmDeleteRecordingDialog(Window owner, string clipName)
    {
        InitializeComponent();
        Owner = owner;
        ClipNameText.Text = clipName;
        Loaded += (_, _) => CancelButton.Focus();
    }

    private void Delete_Click(object sender, RoutedEventArgs e) => DialogResult = true;
    private void Cancel_Click(object sender, RoutedEventArgs e) => DialogResult = false;
}
