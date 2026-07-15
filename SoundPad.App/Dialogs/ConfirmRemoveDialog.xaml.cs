using System.Windows;

namespace SoundPad.App.Dialogs;




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
